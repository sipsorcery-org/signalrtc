// ============================================================================
// FileName: SIPFail2Ban.cs
//
// Description:
// This class blocks SIP traffic based on historical behaviours. For example 
// if SIP requests from an IP address repeatedly attempt to register to a 
// non-existent domain.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Feb 2021	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace signalrtc
{
    public enum BanReasonsEnum
    {
        None,
        ExcessiveRegistrationFailures,
        ExcessiveRetrasnmits,
        ExcessiveAcceptCallFailures,
    }

    public class BanEntry
    {
        /// <summary>
        /// The duration in minutes for each ban step. The step doubles each time
        /// a remote party gets banned.
        /// </summary>
        public const int BAN_DURATION_STEP_MINUTES = 5;

        public SIPEndPoint Source;
        public int RequestCount;
        public int ResponseCount;

        public DateTime LastRetransmitAt;
        public int RetransmitCount;
        public DateTime LastRegistrationFailureAt;
        public int RegistrationFailureCount;
        public DateTime LastAcceptCallFailureAt;
        public int AcceptCallFailures;

        /// <summary>
        /// The timestamp the source was banned at.
        /// </summary>
        public DateTime BannedAt;

        /// <summary>
        /// The duration in minutes for the current ban.
        /// </summary>
        public int BanDurationMinutes;

        /// <summary>
        /// The reason the current ban was imposed.
        /// </summary>
        public BanReasonsEnum BanReason;

        /// <summary>
        /// The number of times this source address has been banned. To be banned multiple
        /// times a previous ban must expire and then have the ban rules triggered again.
        /// </summary>
        public int BanCounts;

        public bool IsBanned => BanReason != BanReasonsEnum.None;

        public void BanExpired()
        {
            LastRetransmitAt = DateTime.MinValue;
            RetransmitCount = 0;
            LastRegistrationFailureAt = DateTime.MinValue;
            RegistrationFailureCount = 0;
            LastAcceptCallFailureAt = DateTime.MinValue;
            AcceptCallFailures = 0;
            BannedAt = DateTime.MinValue;
            BanReason = BanReasonsEnum.None;
        }
    }

    /// <summary>
    /// This class blocks SIP traffic based on historical behaviours. For example 
    /// if SIP requests from an IP address repeatedly attempt to register to a 
    /// non-existent domain.
    /// </summary>
    public class SIPFail2Ban
    {
        /// <summary>
        /// If the lat hit on a specific rule was this many minutes ago the count for that rule 
        /// will be reset. This is intended to deal with cases where a call desintation
        /// could be accidentally mistyped etc.
        /// </summary>
        public const int BAN_RESET_COUNT_AFTER_MINUTES = 10;

        /// <summary>
        /// Hostile user agents often send an INVITE request and then ignore failure
        /// responses resulting in multiple retransmits per request. A friendly 
        /// user agent is unlikely to require more than a handful of retransmits.
        /// </summary>
        public const int BAN_THRESHOLD_RETRANSMIT_COUNT = 20;

        /// <summary>
        /// Hostile user agents will repeatedly attempt to register. If a source 
        /// incurs this number of registration failures it will be banned.
        /// </summary>
        public const int BAN_THRESHOLD_FAILED_REGISTRATIONS_COUNT = 5;

        /// <summary>
        /// Hostile user agents will repeatedly attempt to place calls to arbitrary
        /// desinations. If a source receives this many call attempt failures it will be 
        /// banned.
        /// </summary>
        public const int BAN_THRESHOLD_ACCEPT_CALL_FAILURE_COUNT = 5;

        /// <summary>
        /// Violating a ban rule increments the violation count. If the rule was violated with
        /// a request that used an IP address in the URI then the violation count is increaed
        /// to this number.
        /// </summary>
        public const int RULE_VIOLATION_COUNT_FOR_IPADDRESS = 3;

        private readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SIPFail2Ban>();

        private SIPTransport _sipTransport;

        /// <summary>
        /// Optional function that can be used to classify specific subnets as private and
        /// immune to ban rules.
        /// </summary>
        public Func<IPAddress, bool> IsPrivateSubnet;

        private ConcurrentDictionary<IPAddress, BanEntry> _banList = new ConcurrentDictionary<IPAddress, BanEntry>();

        public SIPFail2Ban(SIPTransport sipTransport)
        {
            _sipTransport = sipTransport;

            _sipTransport.SIPRequestInTraceEvent += SIPRequestIn;
            //_sipTransport.SIPRequestOutTraceEvent += SIPRequestOut;
            _sipTransport.SIPResponseInTraceEvent += SIPResponseIn;
            //_sipTransport.SIPResponseOutTraceEvent += SIPResponseOut;
            _sipTransport.SIPRequestRetransmitTraceEvent += SIPRequestRetransmit;
            _sipTransport.SIPResponseRetransmitTraceEvent += SIPResponseRetransmit;
        }

        public BanReasonsEnum IsBanned(SIPEndPoint remoteEP)
        {
            if (_banList.TryGetValue(remoteEP.Address, out var banEntry))
            {
                if (banEntry.IsBanned && DateTime.Now.Subtract(banEntry.BannedAt).TotalMinutes > banEntry.BanDurationMinutes)
                {
                    // The current ban has expired.
                    logger.LogDebug($"Ban for {remoteEP} has expired for ban number {banEntry.BanCounts}.");
                    banEntry.BanExpired();
                    return BanReasonsEnum.None;
                }
                else
                {
                    return banEntry.BanReason;
                }
            }
            else
            {
                return BanReasonsEnum.None;
            }
        }

        public void RegistrationFailure(SIPEndPoint remoteEP, RegisterResultEnum result, SIPRequest registerRequest)
        {
            if (result == RegisterResultEnum.DomainNotServiced || result == RegisterResultEnum.Forbidden)
            {
                if (IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
                {
                    var banEntry = _banList.GetOrAdd(remoteEP.Address, (addr) => new BanEntry { Source = remoteEP });

                    if(DateTime.Now.Subtract(banEntry.LastRegistrationFailureAt).TotalMinutes > BAN_RESET_COUNT_AFTER_MINUTES)
                    {
                        // Reset count.
                        banEntry.RegistrationFailureCount = 0;
                    }

                    int violationCount = 1;
                    if(IPAddress.TryParse(registerRequest.URI.HostAddress, out _))
                    {
                        // Malicious UA's typically only use the IP address when scanning and brute forcing.
                        violationCount = RULE_VIOLATION_COUNT_FOR_IPADDRESS;
                    }

                    banEntry.LastRegistrationFailureAt = DateTime.Now;
                    banEntry.RegistrationFailureCount += violationCount;

                    ApplyBanRules(ref banEntry);
                }
            }
        }

        public void AcceptCallFailure(SIPEndPoint remoteEP, CallFailureEnum result, SIPRequest inviteRequest)
        {
            if (IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
            {
                var banEntry = _banList.GetOrAdd(remoteEP.Address, (addr) => new BanEntry { Source = remoteEP });

                if (DateTime.Now.Subtract(banEntry.LastAcceptCallFailureAt).TotalMinutes > BAN_RESET_COUNT_AFTER_MINUTES)
                {
                    // Reset count.
                    banEntry.AcceptCallFailures = 0;
                }

                int violationCount = 1;
                if (IPAddress.TryParse(inviteRequest.URI.HostAddress, out _))
                {
                    // Malicious UA's typically only use the IP address when scanning and brute forcing.
                    violationCount = RULE_VIOLATION_COUNT_FOR_IPADDRESS;
                }

                banEntry.LastAcceptCallFailureAt = DateTime.Now;
                banEntry.AcceptCallFailures += violationCount;

                ApplyBanRules(ref banEntry);
            }
        }

        private void SIPRequestIn(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest req)
        {
            if (IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
            {
                var banEntry = _banList.GetOrAdd(remoteEP.Address, (addr) => new BanEntry { Source = remoteEP });
                banEntry.RequestCount++;

                ApplyBanRules(ref banEntry);
            }
        }

        private void SIPResponseIn(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPResponse resp)
        {
            if (IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
            {
                var banEntry = _banList.GetOrAdd(remoteEP.Address, (addr) => new BanEntry { Source = remoteEP });
                banEntry.ResponseCount++;

                ApplyBanRules(ref banEntry);
            }
        }

        private void SIPRequestRetransmit(SIPTransaction tx, SIPRequest req, int count)
        {
            var remoteAddr = req.RemoteSIPEndPoint?.Address;
            if (remoteAddr != null && IsPrivateSubnet?.Invoke(remoteAddr) == false)
            {
                var banEntry = _banList.GetOrAdd(remoteAddr, (addr) => new BanEntry { Source = req.RemoteSIPEndPoint });

                if (DateTime.Now.Subtract(banEntry.LastRetransmitAt).TotalMinutes > BAN_RESET_COUNT_AFTER_MINUTES)
                {
                    // Reset count.
                    banEntry.RetransmitCount = 0;
                }

                banEntry.LastRetransmitAt = DateTime.Now;
                banEntry.RetransmitCount++;

                ApplyBanRules(ref banEntry);
            }
        }

        private void SIPResponseRetransmit(SIPTransaction tx, SIPResponse resp, int count)
        {
            var remoteAddr = resp.RemoteSIPEndPoint?.Address;
            if (remoteAddr != null && IsPrivateSubnet?.Invoke(remoteAddr) == false)
            {
                var banEntry = _banList.GetOrAdd(remoteAddr, (addr) => new BanEntry { Source = resp.RemoteSIPEndPoint });

                if (DateTime.Now.Subtract(banEntry.LastRetransmitAt).TotalMinutes > BAN_RESET_COUNT_AFTER_MINUTES)
                {
                    // Reset count.
                    banEntry.RetransmitCount = 0;
                }

                banEntry.LastRetransmitAt = DateTime.Now;
                banEntry.RetransmitCount++;

                ApplyBanRules(ref banEntry);
            }
        }

        private void ApplyBanRules(ref BanEntry banEntry)
        {
            if (!banEntry.IsBanned)
            {
                if (banEntry.RegistrationFailureCount >= BAN_THRESHOLD_FAILED_REGISTRATIONS_COUNT)
                {
                    banEntry.BanReason = BanReasonsEnum.ExcessiveRegistrationFailures;
                }
                else if (banEntry.RetransmitCount >= BAN_THRESHOLD_RETRANSMIT_COUNT)
                {
                    banEntry.BanReason = BanReasonsEnum.ExcessiveRetrasnmits;
                }
                else if (banEntry.AcceptCallFailures >= BAN_THRESHOLD_ACCEPT_CALL_FAILURE_COUNT)
                {
                    banEntry.BanReason = BanReasonsEnum.ExcessiveAcceptCallFailures;
                }

                if (banEntry.BanReason != BanReasonsEnum.None)
                {
                    banEntry.BannedAt = DateTime.Now;
                    banEntry.BanCounts++;
                    banEntry.BanDurationMinutes = Convert.ToInt32(BanEntry.BAN_DURATION_STEP_MINUTES * Math.Pow(2, banEntry.BanCounts - 1));
                    logger.LogWarning($"Banning {banEntry.Source} for {banEntry.BanReason} duration {banEntry.BanDurationMinutes} minutes.");
                }
            }
        }
    }
}
