// ============================================================================
// FileName: SIPRegistrarCore.cs
//
// Description:
// RFC3822 compliant SIP Registrar.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 21 Jan 2006	Aaron Clauson	Created.
// 22 Nov 2007  Aaron Clauson   Fixed bug where binding refresh was generating a 
//                              duplicate exception if the uac endpoint changed but the contact did not.
// 29 Dec 2020  Aaron Clauson   Modified for .NET Core and tailored For ASP.NET server project.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using signalrtc.DataAccess;

namespace signalrtc
{
    public enum RegisterResultEnum
    {
        Unknown = 0,
        Trying = 1,
        Forbidden = 2,
        Authenticated = 3,
        AuthenticationRequired = 4,
        Failed = 5,
        Error = 6,
        RequestWithNoUser = 7,
        RemoveAllRegistrations = 9,
        DuplicateRequest = 10,
        AuthenticatedFromCache = 11,
        RequestWithNoContact = 12,
        NonRegisterMethod = 13,
        DomainNotServiced = 14,
        IntervalTooBrief = 15,
        SwitchboardPaymentRequired = 16,
    }

    /// <summary>
    /// The registrar core is the class that actually does the work of receiving registration requests and populating and
    /// maintaining the SIP registrations list.
    /// 
    /// From RFC 3261 Chapter "10.2 Constructing the REGISTER Request"
    /// - Request-URI: The Request-URI names the domain of the location service for which the registration is meant.
    /// - The To header field contains the address of record whose registration is to be created, queried, or modified.  
    ///   The To header field and the Request-URI field typically differ, as the former contains a user name. 
    /// 
    /// [ed Therefore:
    /// - The Request-URI indicates the domain for the registration and should match the domain in the To address of record.
    /// - The To address of record contains the username of the user that is attempting to authenticate the request.]
    /// 
    /// Method of operation:
    ///  - New SIP messages received by the SIP Transport layer and queued before being sent to RegistrarCode for processing. For requests
    ///    or response that match an existing REGISTER transaction the SIP Transport layer will handle the retransmit or drop the request if
    ///    it's already being processed.
    ///  - Any non-REGISTER requests received by the RegistrarCore are responded to with not supported,
    ///  - If a persistence is being used to store registered contacts there will generally be a number of threads running for the
    ///    persistence class. Of those threads there will be one that runs calling the SIPRegistrations.IdentifyDirtyContacts. This call identifies
    ///    expired contacts and initiates the sending of any keep alive and OPTIONs requests.
    /// </summary>
    public class RegistrarCore
    {
        private const int MAX_REGISTER_QUEUE_SIZE = 1000;
        private const int MAX_PROCESS_REGISTER_SLEEP = 10000;
        private const string REGISTRAR_THREAD_NAME_PREFIX = "sipregistrar-core";

        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<RegistrarCore>();

        private int m_minimumBindingExpiry = SIPRegistrarBindingsManager.MINIMUM_EXPIRY_SECONDS;

        private SIPTransport m_sipTransport;
        private SIPRegistrarBindingsManager m_registrarBindingsManager;
        private SIPAccountDataLayer m_sipAccountsDataLayer;
        private SIPRegistrarBindingDataLayer m_sipRegistrarBindingDataLayer;
        private SIPDomainManager m_sipDomainManager;

        private string m_serverAgent = SIPServerConstants.SIP_USERAGENT_STRING;
        private ConcurrentQueue<SIPNonInviteTransaction> m_registerQueue = new ConcurrentQueue<SIPNonInviteTransaction>();
        private AutoResetEvent m_registerARE = new AutoResetEvent(false);
        private bool _exit = false;

        /// <summary>
        /// This event fires when a registration attempt fails. As well as the registration
        /// result rhe remote SIP end point of the client is supplied to the handler.
        /// </summary>
        public event Action<SIPEndPoint, RegisterResultEnum, SIPRequest> OnRegisterFailure;

        public int BacklogLength
        {
            get { return m_registerQueue.Count; }
        }

        public RegistrarCore(
            SIPTransport sipTransport,
            SIPRegistrarBindingsManager registrarBindingsManager,
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            SIPDomainManager sipDomainManager)
        {
            m_sipTransport = sipTransport;

            m_sipAccountsDataLayer = new SIPAccountDataLayer(dbContextFactory);
            m_sipRegistrarBindingDataLayer = new SIPRegistrarBindingDataLayer(dbContextFactory);

            m_registrarBindingsManager = registrarBindingsManager;
            m_sipDomainManager = sipDomainManager;
        }

        public void Start(int threadCount)
        {
            Logger.LogInformation($"SIPRegistrarCore starting with {threadCount} threads.");

            for (int index = 1; index <= threadCount; index++)
            {
                int i = index;
                Thread thread = new Thread(() => ProcessRegisterRequest($"{REGISTRAR_THREAD_NAME_PREFIX}-{i}"));
                thread.Start();
            }
        }

        public void Stop()
        {
            _exit = true;
            m_registerARE.Set();
        }

        public void AddRegisterRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest registerRequest)
        {
            if (registerRequest.Method != SIPMethodsEnum.REGISTER)
            {
                SIPResponse notSupportedResponse = SIPResponse.GetResponse(registerRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Registration requests only");
                m_sipTransport.SendResponseAsync(notSupportedResponse).Wait();
            }
            else
            {
                long requestedExpiry = GetRequestedExpiry(registerRequest);

                if (requestedExpiry > 0 && requestedExpiry < m_minimumBindingExpiry)
                {
                    Logger.LogDebug("Bad register request, no expiry of " + requestedExpiry + " to small from " + remoteEndPoint + ".");
                    SIPResponse tooFrequentResponse = SIPResponse.GetResponse(registerRequest, SIPResponseStatusCodesEnum.IntervalTooBrief, null);
                    tooFrequentResponse.Header.MinExpires = m_minimumBindingExpiry;
                    m_sipTransport.SendResponseAsync(tooFrequentResponse).Wait();
                }
                else
                {
                    if (m_registerQueue.Count < MAX_REGISTER_QUEUE_SIZE)
                    {
                        SIPNonInviteTransaction regTx = new SIPNonInviteTransaction(m_sipTransport, registerRequest, null);
                        m_registerQueue.Enqueue(regTx);
                    }
                    else
                    {
                        Logger.LogError("Register queue exceeded maximum queue size " + MAX_REGISTER_QUEUE_SIZE + ", overloaded response sent.");
                        SIPResponse overloadedResponse = SIPResponse.GetResponse(registerRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "Registrar overloaded, please try again shortly");
                        m_sipTransport.SendResponseAsync(overloadedResponse).Wait();
                    }

                    m_registerARE.Set();
                }
            }
        }

        private void ProcessRegisterRequest(string threadName)
        {
            Thread.CurrentThread.Name = threadName;

            while (!_exit)
            {
                if (m_registerQueue.Count > 0)
                {
                    if (m_registerQueue.TryDequeue(out var registrarTransaction))
                    {
                        DateTime startTime = DateTime.Now;
                        RegisterResultEnum result = Register(registrarTransaction);
                        TimeSpan duration = DateTime.Now.Subtract(startTime);

                        if (result != RegisterResultEnum.Authenticated)
                        {
                            OnRegisterFailure?.Invoke(registrarTransaction.TransactionRequest.RemoteSIPEndPoint, result, registrarTransaction.TransactionRequest);
                        }
                    }
                }
                else if (!_exit)
                {
                    m_registerARE.WaitOne(MAX_PROCESS_REGISTER_SLEEP);
                }
            }

            Logger.LogWarning($"ProcessRegisterRequest thread {Thread.CurrentThread.Name} stopping.");
        }

        private long GetRequestedExpiry(SIPRequest registerRequest)
        {
            long contactHeaderExpiry = (registerRequest.Header.Contact != null && registerRequest.Header.Contact.Count > 0) ? registerRequest.Header.Contact[0].Expires : -1;
            return (contactHeaderExpiry == -1) ? registerRequest.Header.Expires : contactHeaderExpiry;
        }

        private RegisterResultEnum Register(SIPNonInviteTransaction registerTransaction)
        {
            try
            {
                SIPRequest sipRequest = registerTransaction.TransactionRequest;
                SIPURI registerURI = sipRequest.URI;
                SIPToHeader toHeader = sipRequest.Header.To;
                string toUser = toHeader.ToURI.User;
                string canonicalDomain = m_sipDomainManager.GetCanonicalDomain(toHeader.ToURI.HostAddress);
                long requestedExpiry = GetRequestedExpiry(sipRequest);

                if (canonicalDomain == null)
                {
                    Logger.LogWarning($"Register request for {toHeader.ToURI.HostAddress} rejected as no matching domain found.");
                    SIPResponse noDomainResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                    registerTransaction.SendResponse(noDomainResponse);
                    return RegisterResultEnum.DomainNotServiced;
                }
                else
                {
                    SIPAccount sipAccount = m_sipAccountsDataLayer.GetSIPAccount(toUser, canonicalDomain).Result;

                    if (sipAccount == null)
                    {
                        Logger.LogWarning($"RegistrarCore SIP account {toUser}@{canonicalDomain} does not exist.");
                        SIPResponse forbiddenResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, null);
                        registerTransaction.SendResponse(forbiddenResponse);
                        return RegisterResultEnum.Forbidden;
                    }
                    else
                    {
                        SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator.AuthenticateSIPRequest(
                            registerTransaction.TransactionRequest.LocalSIPEndPoint, 
                            registerTransaction.TransactionRequest.RemoteSIPEndPoint, 
                            sipRequest, 
                            sipAccount.ToSIPAccountModel());

                        if (!authenticationResult.Authenticated)
                        {
                            // 401 Response with a fresh nonce needs to be sent.
                            SIPResponse authReqdResponse = SIPResponse.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                            authReqdResponse.Header.AuthenticationHeaders = new List<SIPAuthenticationHeader> { authenticationResult.AuthenticationRequiredHeader };
                            registerTransaction.SendResponse(authReqdResponse);

                            if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden)
                            {
                                Logger.LogWarning($"Forbidden {sipAccount.AOR} does not exist, {sipRequest.Header.ProxyReceivedFrom}, {sipRequest.Header.UserAgent}.");
                                return RegisterResultEnum.Forbidden;
                            }
                            else
                            {
                                return RegisterResultEnum.AuthenticationRequired;
                            }
                        }
                        else
                        {
                            if (sipRequest.Header.Contact == null || sipRequest.Header.Contact.Count == 0)
                            {
                                // No contacts header to update bindings with, return a list of the current bindings.
                                //List<SIPRegistrarBinding> bindings = m_registrarBindingsManager.GetBindings(sipAccount.ID);
                                List<SIPRegistrarBinding> bindings = m_sipRegistrarBindingDataLayer.GetForSIPAccount(new Guid(sipAccount.ID)).ToList();
                                //List<SIPContactHeader> contactsList = m_registrarBindingsManager.GetContactHeader(); // registration.GetContactHeader(true, null);
                                if (bindings != null)
                                {
                                    sipRequest.Header.Contact = GetContactHeader(bindings);
                                }

                                SIPResponse okResponse = GetOkResponse(sipRequest);
                                registerTransaction.SendResponse(okResponse);
                                Logger.LogDebug($"Empty registration request successful for {sipAccount.AOR} from {sipRequest.Header.ProxyReceivedFrom}.");
                            }
                            else
                            {
                                SIPEndPoint uacRemoteEndPoint = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedFrom) ?? registerTransaction.TransactionRequest.RemoteSIPEndPoint;
                                SIPEndPoint proxySIPEndPoint = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedOn);
                                SIPEndPoint registrarEndPoint = registerTransaction.TransactionRequest.LocalSIPEndPoint;

                                SIPResponseStatusCodesEnum updateResult = SIPResponseStatusCodesEnum.Ok;
                                string updateMessage = null;

                                DateTime startTime = DateTime.Now;

                                List<SIPRegistrarBinding> bindingsList = m_registrarBindingsManager.UpdateBindings(
                                    sipAccount,
                                    proxySIPEndPoint,
                                    uacRemoteEndPoint,
                                    registrarEndPoint,
                                    sipRequest.Header.Contact,
                                    sipRequest.Header.CallId,
                                    sipRequest.Header.CSeq,
                                    sipRequest.Header.Expires,
                                    sipRequest.Header.UserAgent,
                                    out updateResult,
                                    out updateMessage);

                                TimeSpan duration = DateTime.Now.Subtract(startTime);
                                Logger.LogDebug($"Binding update time for {sipAccount.AOR} took {duration.TotalMilliseconds}ms.");

                                if (updateResult == SIPResponseStatusCodesEnum.Ok)
                                {
                                    string proxySocketStr = (proxySIPEndPoint != null) ? " (proxy=" + proxySIPEndPoint.ToString() + ")" : null;

                                    Logger.LogDebug($"Bindings for {sipAccount.AOR}:");
                                    for (int i = 0; i < bindingsList.Count(); i++)
                                    {
                                        var binding = bindingsList[i];
                                        Logger.LogDebug($" {i}: {binding.ContactURI}, expiry {binding.Expiry}s.");
                                    }

                                    sipRequest.Header.Contact = GetContactHeader(bindingsList);
                                    SIPResponse okResponse = GetOkResponse(sipRequest);
                                    registerTransaction.SendResponse(okResponse);
                                }
                                else
                                {
                                    // The binding update failed even though the REGISTER request was authorised. This is probably due to a 
                                    // temporary problem connecting to the bindings data store. Send Ok but set the binding expiry to the minimum so
                                    // that the UA will try again as soon as possible.
                                    Logger.LogError($"Registration request successful but binding update failed for {sipAccount.AOR} from {registerTransaction.TransactionRequest.RemoteSIPEndPoint}.");
                                    sipRequest.Header.Contact[0].Expires = m_minimumBindingExpiry;
                                    SIPResponse okResponse = GetOkResponse(sipRequest);
                                    registerTransaction.SendResponse(okResponse);
                                }
                            }

                            return RegisterResultEnum.Authenticated;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                string regErrorMessage = "Exception registrarcore registering. " + excp.Message + "\r\n" + registerTransaction.TransactionRequest.ToString();
                Logger.LogError(regErrorMessage);

                SIPResponse errorResponse = SIPResponse.GetResponse(registerTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                registerTransaction.SendResponse(errorResponse);

                return RegisterResultEnum.Error;
            }
        }

        private int GetBindingExpiry(List<SIPRegistrarBinding> bindings, string bindingURI)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return -1;
            }
            else
            {
                foreach (SIPRegistrarBinding binding in bindings)
                {
                    if (binding.ContactURI == bindingURI)
                    {
                        return (int)binding.Expiry;
                    }
                }
                return -1;
            }
        }

        /// <summary>
        /// Gets a SIP contact header for this address-of-record based on the bindings list.
        /// </summary>
        /// <returns></returns>
        private List<SIPContactHeader> GetContactHeader(List<SIPRegistrarBinding> bindings)
        {
            if (bindings != null && bindings.Count > 0)
            {
                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (SIPRegistrarBinding binding in bindings)
                {
                    SIPContactHeader bindingContact = new SIPContactHeader(null, SIPURI.ParseSIPURIRelaxed(binding.ContactURI));
                    if (!string.IsNullOrEmpty(binding.ExpiryTime))
                    {
                        bindingContact.Expires = Convert.ToInt32(DateTime.Parse(binding.ExpiryTime).Subtract(DateTime.UtcNow).TotalSeconds % Int32.MaxValue);
                    }
                    contactHeaderList.Add(bindingContact);
                }

                return contactHeaderList;
            }
            else
            {
                return null;
            }
        }

        private SIPResponse GetOkResponse(SIPRequest sipRequest)
        {
            SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
            SIPHeader requestHeader = sipRequest.Header;
            okResponse.Header = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

            // RFC3261 has a To Tag on the example in section "24.1 Registration".
            if (okResponse.Header.To.ToTag == null || okResponse.Header.To.ToTag.Trim().Length == 0)
            {
                okResponse.Header.To.ToTag = CallProperties.CreateNewTag();
            }

            okResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
            okResponse.Header.Vias = requestHeader.Vias;
            okResponse.Header.Server = m_serverAgent;
            okResponse.Header.MaxForwards = Int32.MinValue;
            okResponse.Header.SetDateHeader();

            return okResponse;
        }
    }
}
