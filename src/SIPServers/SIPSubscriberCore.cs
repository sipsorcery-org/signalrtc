// ============================================================================
// FileName: SIPSubscriberCore.cs
//
// Description:
// SIP server agent to implement a rudimentary server to allow SIP clients to
// subscribe for notification events.
//
// Note the notification events are not wired up and instead this server will
// return a dummy notification for the SIP event types that it understands.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Feb 2021	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using signalrtc.DataAccess;

namespace signalrtc
{
    /// <summary>
    /// The subscriber core implements a rudimentary server to allow SIP clients to subscribe for notification events.
    /// </summary>
    public class SIPSubscriberCore
    {
        private const int MAX_SUBSCRIBE_QUEUE_SIZE = 1000;
        private const int MAX_PROCESS_SUBSCRIBE_SLEEP = 10000;
        private const string SUBSCRIBER_THREAD_NAME_PREFIX = "sipsubscriber-core";

        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<SIPSubscriberCore>();

        private SIPTransport m_sipTransport;
        private SIPAccountDataLayer m_sipAccountsDataLayer;
        private SIPDomainManager m_sipDomainManager;

        private string m_serverAgent = SIPConstants.SIP_USERAGENT_STRING;
        private ConcurrentQueue<SIPNonInviteTransaction> m_subscribeQueue = new ConcurrentQueue<SIPNonInviteTransaction>();
        private AutoResetEvent m_subscribeARE = new AutoResetEvent(false);
        private bool _exit = false;

        public int BacklogLength
        {
            get { return m_subscribeQueue.Count; }
        }

        public SIPSubscriberCore(
            SIPTransport sipTransport,
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            SIPDomainManager sipDomainManager)
        {
            m_sipTransport = sipTransport;

            m_sipAccountsDataLayer = new SIPAccountDataLayer(dbContextFactory);
            m_sipDomainManager = sipDomainManager;
        }

        public void Start(int threadCount)
        {
            Logger.LogInformation($"SIPSubscriberCore starting with {threadCount} threads.");

            for (int index = 1; index <= threadCount; index++)
            {
                int i = index;
                Thread thread = new Thread(() => ProcessSubscribeRequest($"{SUBSCRIBER_THREAD_NAME_PREFIX}-{i}"));
                thread.Start();
            }
        }

        public void Stop()
        {
            _exit = true;
            m_subscribeARE.Set();
        }

        public void AddSubscribeRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest subscribeRequest)
        {
            if (subscribeRequest.Method != SIPMethodsEnum.SUBSCRIBE)
            {
                SIPResponse notSupportedResponse = SIPResponse.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Subscribe requests only");
                m_sipTransport.SendResponseAsync(notSupportedResponse).Wait();
            }
            else
            {
                if (m_subscribeQueue.Count < MAX_SUBSCRIBE_QUEUE_SIZE)
                {
                    SIPNonInviteTransaction subTx = new SIPNonInviteTransaction(m_sipTransport, subscribeRequest, null);
                    m_subscribeQueue.Enqueue(subTx);
                }
                else
                {
                    Logger.LogError($"Subscribe queue exceeded maximum queue size {MAX_SUBSCRIBE_QUEUE_SIZE}, overloaded response sent.");
                    SIPResponse overloadedResponse = SIPResponse.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "Subscriber core overloaded, please try again shortly");
                    m_sipTransport.SendResponseAsync(overloadedResponse).Wait();
                }

                m_subscribeARE.Set();
            }
        }

        private void ProcessSubscribeRequest(string threadName)
        {
            Thread.CurrentThread.Name = threadName;

            while (!_exit)
            {
                if (m_subscribeQueue.Count > 0)
                {
                    if (m_subscribeQueue.TryDequeue(out var subscribeTransaction))
                    {
                        DoSubscribe(subscribeTransaction);
                    }
                }
                else if (!_exit)
                {
                    m_subscribeARE.WaitOne(MAX_PROCESS_SUBSCRIBE_SLEEP);
                }
            }

            Logger.LogWarning($"ProcessSubscribeRequest thread {Thread.CurrentThread.Name} stopping.");
        }

        private void DoSubscribe(SIPNonInviteTransaction subTx)
        {
            SIPRequest req = subTx.TransactionRequest;
            string user = req.Header.From.FromURI.User;
            string domain = req.Header.From.FromURI.HostAddress;

            string canonicalDomain = m_sipDomainManager.GetCanonicalDomain(domain);

            if (canonicalDomain == null)
            {
                Logger.LogWarning($"Subscribe Register request for {req.Header.From.FromURI.Host} rejected as no matching domain found.");
                SIPResponse noDomainResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                subTx.SendResponse(noDomainResponse);
            }
            else
            {
                SIPAccount sipAccount = m_sipAccountsDataLayer.GetSIPAccount(user, canonicalDomain).Result;

                if (sipAccount == null)
                {
                    Logger.LogWarning($"SubscriberCore SIP account {user}@{canonicalDomain} does not exist.");
                    SIPResponse forbiddenResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Forbidden, null);
                    subTx.SendResponse(forbiddenResponse);
                }
                else
                {
                    SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator.AuthenticateSIPRequest(req.LocalSIPEndPoint, req.RemoteSIPEndPoint, req, sipAccount);

                    if (!authenticationResult.Authenticated)
                    {
                        // 401 Response with a fresh nonce needs to be sent.
                        SIPResponse authReqdResponse = SIPResponse.GetResponse(req, authenticationResult.ErrorResponse, null);
                        authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                        subTx.SendResponse(authReqdResponse);

                        if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden)
                        {
                            Logger.LogWarning($"Forbidden {sipAccount.AOR} does not exist, received from {req.RemoteSIPEndPoint}, user agent {req.Header.UserAgent}.");
                        }
                    }
                    else
                    {
                        SIPResponse okResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                        subTx.SendResponse(okResponse);
                        Logger.LogDebug($"Subscription request for {user}@{domain} was successful.");

                        // Give the subscribe response time to be sent.
                        Thread.Sleep(500);

                        if (req.Header.Expires > 0)
                        {
                            SendInitialNotification(req, sipAccount);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sends in initial notification request for a new subscription where it makes sense to do so.
        /// </summary>
        /// <param name="subscribeRequest">The client request that resulted in the subscription creation.</param>
        /// <param name="sipAccount">The authenticated SIP account that created the subscription.</param>
        private void SendInitialNotification(SIPRequest subscribeRequest, ISIPAccount sipAccount)
        {
            if (SIPEventPackageType.IsValid(subscribeRequest.Header.Event))
            {
                var eventPackage = SIPEventPackageType.Parse(subscribeRequest.Header.Event);

                if(eventPackage == SIPEventPackagesEnum.MessageSummary)
                {
                    // Send a dummy message waiting indicator message so that a client can verify a notification request can
                    // be received.
                    var dstUri = subscribeRequest.Header.Contact[0].ContactURI;
                    var accountURI = new SIPURI(sipAccount.SIPUsername, sipAccount.SIPDomain, null, dstUri.Scheme, dstUri.Protocol);

                    var notifyReq = SIPRequest.GetRequest(SIPMethodsEnum.NOTIFY,
                        subscribeRequest.Header.Contact[0].ContactURI,
                        new SIPToHeader(null, accountURI.CopyOf(), CallProperties.CreateNewTag()),
                        new SIPFromHeader(null, accountURI.CopyOf(), CallProperties.CreateNewTag()));
                    notifyReq.Header.CallId = subscribeRequest.Header.CallId;
                    notifyReq.Header.CSeq = subscribeRequest.Header.CSeq++;
                    notifyReq.Header.Server = m_serverAgent;
                    notifyReq.Header.Event = SIPEventPackageType.MESSAGE_SUMMARY_EVENT_VALUE;
                    notifyReq.Header.SubscriptionState = "active";
                    notifyReq.Header.SetDateHeader();
                    notifyReq.Header.ContentType = SIPMIMETypes.MWI_CONTENT_TYPE;
                    notifyReq.Body = "Messages-Waiting: no";

                    // A little trick here is using the remote SIP end point as the destination rather than the Contact URI.
                    // Ideally some extra logic to check for IPv4 NAT should be applied. But since this server is known to
                    // be operating in the cloud and only send NOTIFY requests to Internet clients it should be a reasonable
                    // option.
                    SIPNonInviteTransaction notifyTx = new SIPNonInviteTransaction(m_sipTransport, notifyReq, subscribeRequest.RemoteSIPEndPoint);
                    notifyTx.SendRequest();
                }
            }
        }
    }
}
