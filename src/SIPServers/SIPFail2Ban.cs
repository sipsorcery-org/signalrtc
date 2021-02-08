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
        ExcessiveRetrasnmits
    }

    public class BanEntry
    {
        public SIPEndPoint Source;
        public int RequestCount;
        public int ResponseCount;
        public DateTime? LastRetransmitAt;
        public int RetransmitCount;
        public DateTime? LastRegistrationFailureAt;
        public int RegistrationFailureCount;
        public DateTime? BannedAt;
        //public int BanLengthSeconds;
        public BanReasonsEnum BanReason;

        public bool IsBanned => BanReason != BanReasonsEnum.None;
    }

    /// <summary>
    /// This class blocks SIP traffic based on historical behaviours. For example 
    /// if SIP requests from an IP address repeatedly attempt to register to a 
    /// non-existent domain.
    /// </summary>
    public class SIPFail2Ban
    {
        /// <summary>
        /// Hostile user agents often send an INVITE request and then ignore failure
        /// responses resulting in multiple retransmits per request. A friendly 
        /// user agent is unlikely to require more than a handful of retransmits.
        /// </summary>
        public const int BAN_THRESHOLD_RETRANSMIT_COUNT = 50;

        /// <summary>
        /// The period in seconds since the last retrasmit occurred at which the 
        /// retransmit count will be reset.
        /// </summary>
        public const int BAN_THRESHOLD_RETRANSMIT_WINDOW = 120;

        /// <summary>
        /// Hostile user agents will repeatedly attempt to register. If a source 
        /// incurs this number of registration failures it will be banned.
        /// </summary>
        public const int BAN_THRESHOLD_FAILED_REGISTRATIONS_COUNT = 5;

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
            if(_banList.TryGetValue(remoteEP.Address, out var banEntry))
            {
                return banEntry.BanReason;
            }
            else
            {
                return BanReasonsEnum.None;
            }
        }

        public void RegistrationFailure(SIPEndPoint remoteEP, RegisterResultEnum result)
        {
            if (result == RegisterResultEnum.DomainNotServiced || result == RegisterResultEnum.Forbidden)
            {
                if (IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
                {
                    var banEntry = _banList.GetOrAdd(remoteEP.Address, (addr) => new BanEntry { Source = remoteEP });
                    banEntry.LastRegistrationFailureAt = DateTime.Now;
                    banEntry.RegistrationFailureCount++;

                    ApplyBanRules(ref banEntry);
                }
            }
        }

        private void SIPRequestIn(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest req)
        {
            if(IsPrivateSubnet?.Invoke(remoteEP.Address) == false)
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

                if (banEntry.BanReason != BanReasonsEnum.None)
                {
                    banEntry.BannedAt = DateTime.Now;
                    logger.LogWarning($"Banning {banEntry.Source} for {banEntry.BanReason}");
                }
            }
        }
    }
}
