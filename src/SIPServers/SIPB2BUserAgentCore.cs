// ============================================================================
// FileName: SIPB2BUserAgentCore.cs
//
// Description:
// SIP server core that handles incoming call requests by acting as a 
// Back-to-Back User Agent (B2BUA).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using signalrtc.DataAccess;

namespace signalrtc
{
    public enum CallFailureEnum
    {
        NotFound,
        NoSIPAccount
    }

    /// <summary>
    /// This class acts as a server agent that processes incoming calls (INVITE requests)
    /// by acting as a Back-to-Back User Agent (B2BUA). 
    /// </summary>
    /// <remarks>
    /// The high level method of operations is:
    /// - Wait for an INVITE request from a User Agent Client (UAC),
    /// - Answer UAC by creating a User Agent Server (UAS),
    /// - Apply business logic to determine forwarding destination fof call from UAC,
    /// - Create a new UAC and "link" it to the UAS,
    /// - Start the UAC call to the forward destination.
    /// </remarks>
    public class SIPB2BUserAgentCore
    {
        private const int MAX_INVITE_QUEUE_SIZE = 5;
        private const int MAX_PROCESS_INVITE_SLEEP = 10000;
        private const string B2BUA_THREAD_NAME_PREFIX = "sipb2bua-core";

        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<RegistrarCore>();

        private AutoResetEvent _inviteARE = new AutoResetEvent(false);
        private ConcurrentQueue<UASInviteTransaction> _inviteQueue = new ConcurrentQueue<UASInviteTransaction>();
        private bool _exit = false;

        private SIPTransport _sipTransport;
        private SIPAccountDataLayer _sipAccountDataLayer;
        private SIPCallManager _sipCallManager;
        private SIPDialPlanManager _sipdialPlan;
        private SIPDomainManager _sipDomainManager;

        /// <summary>
        /// This event fires when an incoming call request is not accepted by the server side of 
        /// the B2BUA core.
        /// </summary>
        public event Action<SIPEndPoint, CallFailureEnum, SIPRequest> OnAcceptCallFailure;

        public SIPB2BUserAgentCore(
            SIPTransport sipTransport,
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            SIPDialPlanManager sipDialPlan,
            SIPDomainManager sipDomainManager)
        {
            if (sipTransport == null)
            {
                throw new ArgumentNullException(nameof(sipTransport));
            }

            _sipTransport = sipTransport;
            _sipCallManager = new SIPCallManager(_sipTransport, null, dbContextFactory);
            _sipdialPlan = sipDialPlan;
            _sipDomainManager = sipDomainManager;
            _sipAccountDataLayer = new SIPAccountDataLayer(dbContextFactory);
        }

        public void Start(int threadCount)
        {
            Logger.LogInformation($"SIPB2BUserAgentCore starting with {threadCount} threads.");

            for (int index = 1; index <= threadCount; index++)
            {
                int i = index;
                Thread thread = new Thread(() => ProcessInviteRequest($"{B2BUA_THREAD_NAME_PREFIX}-{i}"));
                thread.Start();
            }
        }

        public void Stop()
        {
            _exit = true;
            _inviteARE.Set();
        }

        public void AddInviteRequest(SIPRequest inviteRequest)
        {
            if (inviteRequest.Method != SIPMethodsEnum.INVITE)
            {
                SIPResponse notSupportedResponse = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Invite requests only");
                _sipTransport.SendResponseAsync(notSupportedResponse).Wait();
            }
            else
            {
                if (_inviteQueue.Count < MAX_INVITE_QUEUE_SIZE)
                {
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(_sipTransport, inviteRequest, null);
                    var trying = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.Trying, null);
                    uasTransaction.SendProvisionalResponse(trying).Wait();

                    _inviteQueue.Enqueue(uasTransaction);
                }
                else
                {
                    Logger.LogWarning($"Invite queue exceeded max queue size {MAX_INVITE_QUEUE_SIZE} overloaded response sent.");
                    SIPResponse overloadedResponse = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "B2BUA overloaded, please try again shortly");
                    _sipTransport.SendResponseAsync(overloadedResponse).Wait();
                }

                _inviteARE.Set();
            }
        }

        private void ProcessInviteRequest(string threadName)
        {
            Thread.CurrentThread.Name = threadName;

            while (!_exit)
            {
                if (_inviteQueue.Count > 0)
                {
                    if (_inviteQueue.TryDequeue(out var uasTransaction))
                    {
                        var sipAccount = GetCaller(uasTransaction).Result;

                        if (uasTransaction.TransactionFinalResponse == null)
                        {
                            Forward(uasTransaction, sipAccount).Wait();
                        }
                    }
                }
                else if (!_exit)
                {
                    _inviteARE.WaitOne(MAX_PROCESS_INVITE_SLEEP);
                }
            }

            Logger.LogWarning("ProcessInviteRequest thread " + Thread.CurrentThread.Name + " stopping.");
        }

        /// <summary>
        /// Attempts to lookup the caller based on the "To" header host. If the host matches a
        /// hosted domain then an attempt will be made to retrieve the SIP account for the caller.
        /// If a SIP account is not found for a hosted domain the call is rejected with a 403 response.
        /// If the host does not match then the caller is treated as a public non-authenticated caller.
        /// </summary>
        /// <returns>The caller's SIP account or null for non-hosted domains.</returns>
        private async Task<ISIPAccount> GetCaller(UASInviteTransaction uasTx)
        {
            var invReq = uasTx.TransactionRequest;

            if (invReq.Header.From == null || invReq.Header.From.FromURI == null)
            {
                uasTx.SendFinalResponse(SIPResponse.GetResponse(invReq, SIPResponseStatusCodesEnum.BadRequest, "From header malformed"));
                return null;
            }
            else
            {
                string canonicalDomain = _sipDomainManager.GetCanonicalDomain(invReq.Header.From.FromURI.HostAddress);

                if (canonicalDomain == null)
                {
                    // The caller is from a non-hosted domain. Will be treated as a public non-authenticated caller.
                    return null;
                }
                else
                {
                    Logger.LogDebug($"B2B incoming caller was for hosted domain {canonicalDomain}, looking up caller for {invReq.Header.From.FromURI.User}.");

                    var sipAccount = await _sipAccountDataLayer.GetSIPAccount(invReq.Header.From.FromURI.User, canonicalDomain);

                    if (sipAccount == null)
                    {
                        Logger.LogWarning($"B2B no SIP account found for caller {invReq.Header.From.FromURI.User}@{canonicalDomain}, rejecting.");
                        uasTx.SendFinalResponse(SIPResponse.GetResponse(invReq, SIPResponseStatusCodesEnum.Forbidden, null));

                        OnAcceptCallFailure?.Invoke(uasTx.TransactionRequest.RemoteSIPEndPoint, CallFailureEnum.NoSIPAccount, invReq);
                    }

                    return sipAccount;
                }
            }
        }

        private async Task Forward(UASInviteTransaction uasTx, ISIPAccount callerSIPAccount)
        {
            var invReq = uasTx.TransactionRequest;
            //uasTx.TransactionStateChanged += (tx) => Logger.LogDebug($"B2B uas tx state changed to {tx.TransactionState}.");
            //uasTx.TransactionTraceMessage += (tx, msg) => Logger.LogDebug($"B2B uas tx trace message. {msg}");

            Logger.LogDebug($"B2B commencing forward for caller {invReq.Header.From.FromURI} to {invReq.URI}.");

            SIPB2BUserAgent b2bua = new SIPB2BUserAgent(_sipTransport, null, uasTx, callerSIPAccount);

            bool isAuthenticated = false;
            if (callerSIPAccount != null)
            {
                isAuthenticated = b2bua.AuthenticateCall();
            }

            if (callerSIPAccount == null || isAuthenticated == true)
            {
                b2bua.CallAnswered += (uac, resp) => ForwardCallAnswered(uac, b2bua);

                var dst = await _sipdialPlan.Lookup(uasTx, null);

                if (dst == null)
                {
                    Logger.LogInformation($"B2BUA lookup did not return a destination. Rejecting UAS call.");

                    var notFoundResp = SIPResponse.GetResponse(uasTx.TransactionRequest, SIPResponseStatusCodesEnum.NotFound, null);
                    uasTx.SendFinalResponse(notFoundResp);

                    OnAcceptCallFailure?.Invoke(uasTx.TransactionRequest.RemoteSIPEndPoint, CallFailureEnum.NotFound, invReq);
                }
                else
                {
                    Logger.LogInformation($"B2BUA forwarding call to {dst.Uri}.");
                    b2bua.Call(dst);
                }
            }
        }

        private void ForwardCallAnswered(ISIPClientUserAgent uac, SIPB2BUserAgent b2bua)
        {
            if (uac.SIPDialogue != null)
            {
                _sipCallManager.BridgeDialogues(uac.SIPDialogue, b2bua.SIPDialogue);
            }
        }
    }
}
