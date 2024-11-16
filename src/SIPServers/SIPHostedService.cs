//-----------------------------------------------------------------------------
// Filename: SIPHostedService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net Core
// server application to manage the SIP transport and server cores. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using signalrtc.DataAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Internal;

namespace signalrtc
{
    /// <summary>
    /// A hosted service to manage a SIP transport layer. This class is designed to be a long running
    /// singleton. Once created the SIP transport channels listen for incoming messages.
    /// </summary>
    public class SIPHostedService : IHostedService
    {
        public const int DEFAULT_SIP_LISTEN_PORT = 5060;
        public const int DEFAULT_SIPS_LISTEN_PORT = 5061;
        public const int MAX_REGISTRAR_BINDINGS = 10;
        public const int REGISTRAR_CORE_WORKER_THREADS = 1;
        public const int B2BUA_CORE_WORKER_THREADS = 1;
        public const int SUBSCRIBER_CORE_WORKER_THREADS = 1;

        private readonly ILogger<SIPHostedService> _logger;
        private readonly IConfiguration _config;
        private readonly X509Certificate2 _tlsCertificate;

        private SIPTransport _sipTransport;
        private SIPDialPlanManager _sipDialPlanManager;
        private RegistrarCore _registrarCore;
        private SIPRegistrarBindingsManager _bindingsManager;
        private SIPB2BUserAgentCore _b2bUserAgentCore;
        private SIPCallManager _sipCallManager;
        private CDRDataLayer _cdrDataLayer;
        private SIPDomainManager _sipDomainManager;
        private SIPSubscriberCore _subscriberCore;
        private SIPFail2Ban _sipFail2Ban;

        // Fields for setting the Contact header in SIP messages.
        public string _publicContactHostname;
        public IPAddress _publicContactIPv4;
        public IPAddress _publicContactIPv6;
        public Func<IPAddress, bool> _isPrivateSubnet = (ipaddr) => false;

        public SIPHostedService(
            ILogger<SIPHostedService> logger,
            IConfiguration config,
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            X509Certificate2 tlsCertificate,
            SIPDialPlanManager sipDialPlanManager)
        {
            _logger = logger;
            _config = config;
            _tlsCertificate = tlsCertificate;
            _sipDialPlanManager = sipDialPlanManager;

            _sipTransport = new SIPTransport();

            // Not using the default trace logs from SIP transport as we don't want to 
            // log requests from banned addresses.
            EnableTraceLogs();

            _sipDomainManager = new SIPDomainManager(dbContextFactory);
            _sipDomainManager.Load().Wait();

            _bindingsManager = new SIPRegistrarBindingsManager(new SIPRegistrarBindingDataLayer(dbContextFactory), MAX_REGISTRAR_BINDINGS);
            _registrarCore = new RegistrarCore(_sipTransport, _bindingsManager, dbContextFactory, _sipDomainManager);
            _b2bUserAgentCore = new SIPB2BUserAgentCore(_sipTransport, dbContextFactory, _sipDialPlanManager, _sipDomainManager);
            _sipCallManager = new SIPCallManager(_sipTransport, null, dbContextFactory);
            _cdrDataLayer = new CDRDataLayer(dbContextFactory);
            _subscriberCore = new SIPSubscriberCore(_sipTransport, dbContextFactory, _sipDomainManager);
            _sipFail2Ban = new SIPFail2Ban(_sipTransport);

            _registrarCore.OnRegisterFailure += _sipFail2Ban.RegistrationFailure;
            _b2bUserAgentCore.OnAcceptCallFailure += _sipFail2Ban.AcceptCallFailure;

            SIPCDR.CDRCreated += _cdrDataLayer.Add;
            SIPCDR.CDRAnswered += _cdrDataLayer.Update;
            SIPCDR.CDRUpdated += _cdrDataLayer.Update;
            SIPCDR.CDRHungup += _cdrDataLayer.Update;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("SIP hosted service starting...");

            // Get application configuration settings.
            int listenPort = _config.GetValue<int>(ConfigKeys.SIP_LISTEN_PORT, DEFAULT_SIP_LISTEN_PORT);
            int tlsListenPort = _config.GetValue<int>(ConfigKeys.SIP_TLS_LISTEN_PORT, DEFAULT_SIPS_LISTEN_PORT);

            // Set the fields used for setting the SIP Contact header.
            _publicContactHostname = _config.GetValue<string>(ConfigKeys.SIP_PUBLIC_CONTACT_HOSTNAME, null);
            IPAddress.TryParse(_config.GetValue<string>(ConfigKeys.SIP_PUBLIC_CONTACT_PUBLICIPV4, null), out _publicContactIPv4);
            IPAddress.TryParse(_config.GetValue<string>(ConfigKeys.SIP_PUBLIC_CONTACT_PUBLICIPV6, null), out _publicContactIPv6);

            IConfigurationSection sipPrivateSubnets = _config.GetSection(ConfigKeys.SIP_PRIVATE_CONTACT_SUBNETS);

            _logger.LogInformation($"SIP transport public contact hostname set to {_publicContactHostname}.");
            _logger.LogInformation($"SIP transport public contact IPv4 set to {_publicContactIPv4}.");
            _logger.LogInformation($"SIP transport public contact IPv6 set to {_publicContactIPv6}.");

            if (sipPrivateSubnets != null)
            {
                List<Func<IPAddress, bool>> isInSubnetFunctions = new List<Func<IPAddress, bool>>();

                foreach (string subnet in sipPrivateSubnets.Get<string[]>())
                {
                    _logger.LogInformation($"SIP transport private subnet {subnet}.");

                    if (IPNetwork.TryParse(subnet, out var network))
                    {
                        isInSubnetFunctions.Add((ipaddr) =>
                            ipaddr.AddressFamily == network.BaseAddress.AddressFamily
                           && network.Contains(ipaddr));
                    }
                    else
                    {
                        _logger.LogWarning($"IP network not recognised {subnet}.");
                    }
                }

                if (isInSubnetFunctions.Count > 0)
                {
                    _isPrivateSubnet = (ipaddr) =>
                    {
                        foreach (var isInFunc in isInSubnetFunctions)
                        {
                            if (isInFunc(ipaddr))
                            {
                                return true;
                            }
                        }

                        return false;
                    };
                }

                _sipFail2Ban.IsPrivateSubnet = _isPrivateSubnet;
            }

            // Create SIP channels.
            if (Socket.OSSupportsIPv6)
            {
                if (_tlsCertificate != null)
                {
                    _sipTransport.AddSIPChannel(new SIPTLSChannel(_tlsCertificate, new IPEndPoint(IPAddress.IPv6Any, tlsListenPort), true));
                }

                _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, listenPort), true));
                _sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.IPv6Any, listenPort), true));
            }
            else
            {
                if (_tlsCertificate != null)
                {
                    _sipTransport.AddSIPChannel(new SIPTLSChannel(_tlsCertificate, new IPEndPoint(IPAddress.Any, tlsListenPort)));
                }

                _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, listenPort)));
                _sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, listenPort)));
            }

            var listeningEP = _sipTransport.GetSIPChannels().First().ListeningSIPEndPoint;
            _logger.LogInformation($"SIP transport listening on {listeningEP}.");

            _sipTransport.CustomiseRequestHeader = CustomiseContact;
            _sipTransport.CustomiseResponseHeader = CustomiseContact;

            _bindingsManager.Start();
            _registrarCore.Start(REGISTRAR_CORE_WORKER_THREADS);
            _b2bUserAgentCore.Start(B2BUA_CORE_WORKER_THREADS);
            _subscriberCore.Start(SUBSCRIBER_CORE_WORKER_THREADS);

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            // Warm up the dialplan so it's ready for the first call.
            _ = Task.Run(async () =>
            {
                var dp = await _sipDialPlanManager.LoadDialPlan();
                if (dp != null)
                {
                    _sipDialPlanManager.CompileDialPlan(dp.DialPlanScript, DateTime.Parse(dp.LastUpdate));
                }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("SIP hosted service stopping...");

            _registrarCore.Stop();
            _subscriberCore.Stop();
            _b2bUserAgentCore.Stop();
            _bindingsManager.Stop();

            _sipTransport?.Shutdown();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Because this is a server user agent the SIP transport must start listening for client user agents.
        /// </summary>
        private async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                var banResult = _sipFail2Ban.IsBanned(remoteEndPoint);
                if (banResult == BanReasonsEnum.None)
                {
                    if (sipRequest.Header.From != null &&
                    sipRequest.Header.From.FromTag != null &&
                    sipRequest.Header.To != null &&
                    sipRequest.Header.To.ToTag != null)
                    {
                        _sipCallManager.ProcessInDialogueRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                    }
                    else
                    {
                        switch (sipRequest.Method)
                        {
                            case SIPMethodsEnum.BYE:
                            case SIPMethodsEnum.CANCEL:
                                // BYE's and CANCEL's should always have dialog fields set.
                                SIPResponse badResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, null);
                                await _sipTransport.SendResponseAsync(badResponse);
                                break;

                            case SIPMethodsEnum.INVITE:
                                if (!await WasRejected(sipRequest))
                                {
                                    _logger.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");
                                    _b2bUserAgentCore.AddInviteRequest(sipRequest);
                                }
                                break;

                            case SIPMethodsEnum.OPTIONS:
                                SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                                optionsResponse.Header.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader(sipRequest.URI.Scheme) };
                                optionsResponse.Header.Server = SIPServerConstants.SIP_USERAGENT_STRING;
                                await _sipTransport.SendResponseAsync(optionsResponse);
                                break;

                            case SIPMethodsEnum.REGISTER:
                                if (!await WasRejected(sipRequest))
                                {
                                    _registrarCore.AddRegisterRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                                }
                                break;

                            case SIPMethodsEnum.SUBSCRIBE:
                                if (!await WasRejected(sipRequest))
                                {
                                    _subscriberCore.AddSubscribeRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                                }
                                break;

                            default:
                                var notAllowedResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                                await _sipTransport.SendResponseAsync(notAllowedResp);
                                break;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"Ban list hit for {remoteEndPoint} and {banResult} dropped request {sipRequest.StatusLine}, user agent {sipRequest.Header.UserAgent}.");
                }
            }
            catch (Exception reqExcp)
            {
                _logger.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
            }
        }

        /// <summary>
        /// Apply verification logic to incoming requests and reject if invalid. Note the verification
        /// rules here do not suit all requests. The rules are suitable for requests that are likely
        /// to require authentication such as INVITE, REGISTER, SUBSCRIBE etc.
        /// </summary>
        /// <param name="req">The incoming SIP request to check.</param>
        /// <returns>True if the request failed validation and was rejected. False if the request is valid.</returns>
        private async Task<bool> WasRejected(SIPRequest req)
        {
            var errorMessge = req switch
            {
                var x when x.Header.To == null => "Missing To header",
                var x when string.IsNullOrWhiteSpace(x.Header.To.ToURI.User) => "Missing username on To header",
                var x when x.Header.From == null || x.Header.From.FromURI == null => "Missing From header URI",
                var x when string.IsNullOrWhiteSpace(x.Header.From.FromURI.User) => "Missing username on From header",
                var x when x.Header.Contact == null || x.Header.Contact.Count == 0 => "Missing Contact header",
                _ => null
            };

            if (errorMessge != null)
            {
                SIPResponse errResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BadRequest, errorMessge);
                await _sipTransport.SendResponseAsync(errResponse);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// This customisation method is used because a one sized fits all "Contact Host" does not work
        /// for forwarding calls when the server can be on either side of the load balancer.
        /// </summary>
        /// <param name="localEP">The local end point that's been chosen by the transport layer to
        /// send this request with.</param>
        /// <param name="dstEP">The destination end point the request is going to be sent to.</param>
        /// <param name="request">The request being sent.</param>
        /// <returns>For requests that need to have their Contact header changed a new SIP Header instance
        /// is returned otherwise null to indicate the original SIP header object can be used.</returns>
        private SIPHeader CustomiseContact(SIPEndPoint localEP, SIPEndPoint dstEP, SIPRequest request)
        {
            if (request.Method == SIPMethodsEnum.INVITE)
            {
                return CustomiseInviteContactHeader(dstEP, request.Header);
            }
            return null;
        }

        /// <summary>
        /// This customisation method is used because a one sized fits all "Contact Host" does not work
        /// for forwarding calls when the server can be on either side of the load balancer.
        /// </summary>
        /// <param name="localEP">The local end point that's been chosen by the transport layer to
        /// send this response with.</param>
        /// <param name="dstEP">The destination end point the response is going to be sent to.</param>
        /// <param name="response">The response being sent.</param>
        /// <returns>For responses that need to have their Contact header changed a new SIP Header instance
        /// is returned otherwise null to indicate the original SIP header object can be used.</returns>
        private SIPHeader CustomiseContact(SIPEndPoint localEP, SIPEndPoint dstEP, SIPResponse response)
        {
            if (response.Header.CSeqMethod == SIPMethodsEnum.INVITE || response.Header.CSeqMethod == SIPMethodsEnum.OPTIONS)
            {
                return CustomiseInviteContactHeader(dstEP, response.Header);
            }
            return null;
        }

        /// <summary>
        /// Customises the Contact header for an INVITE request or response based on the destination
        /// it is being sent to.
        /// </summary>
        /// <param name="dstEP">The destination end point for the send.</param>
        /// <param name="inviteHeader">The original header from the INVITE request or response.</param>
        /// <returns>If an adjustment was made then a new SIP header instance is returned. If not, null is returned.</returns>
        private SIPHeader CustomiseInviteContactHeader(SIPEndPoint dstEP, SIPHeader inviteHeader)
        {
            var dstAddress = dstEP.GetIPEndPoint().Address;

            // Don't customise if the destination is a private IP address (which means it's staying
            // on the same subnet or virtual subnet).
            if (!_isPrivateSubnet(dstAddress) && inviteHeader.Contact != null && inviteHeader.Contact.Count == 1)
            {
                // The priority is use the public IP address fields if they are available (saves DNS lookups) and
                // falls back to the hostname if they are not. The exception is if the Contact URI is using a secure
                // SIP scheme "sips" in which case the priority is to use any available hostname first.

                // Port of 0 is set when user agents set Contact Host to "0.0.0.0:0" which is the method to
                // get the transport layer to set it at send time.
                bool isDefaultPort = inviteHeader.Contact[0].ContactURI.IsDefaultPort() || inviteHeader.Contact[0].ContactURI.HostPort == "0";

                if (inviteHeader.Contact[0].ContactURI.Scheme == SIPSchemesEnum.sips &&
                    !string.IsNullOrWhiteSpace(_publicContactHostname))
                {
                    var copy = inviteHeader.Copy();
                    copy.Contact[0].ContactURI.Host = _publicContactHostname;
                    return copy;
                }
                else if (_publicContactIPv4 != null &&
                    (dstAddress.AddressFamily == AddressFamily.InterNetwork || dstAddress.IsIPv4MappedToIPv6))
                {
                    var copy = inviteHeader.Copy();
                    copy.Contact[0].ContactURI.Host = isDefaultPort ? _publicContactIPv4.ToString() : $"{_publicContactIPv4}:{inviteHeader.Contact[0].ContactURI.HostPort}";
                    return copy;
                }
                else if (dstAddress.AddressFamily == AddressFamily.InterNetworkV6 && _publicContactIPv6 != null)
                {
                    var copy = inviteHeader.Copy();
                    copy.Contact[0].ContactURI.Host = isDefaultPort ? $"[{_publicContactIPv6}]" : $"[{_publicContactIPv6}]:{inviteHeader.Contact[0].ContactURI.HostPort}";
                    return copy;
                }
                else if (!string.IsNullOrWhiteSpace(_publicContactHostname))
                {
                    var copy = inviteHeader.Copy();
                    copy.Contact[0].ContactURI.Host = _publicContactHostname;
                    return copy;
                }
            }

            return null;
        }

        public void EnableTraceLogs()
        {
            _sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                if (_sipFail2Ban.IsBanned(remoteEP) == BanReasonsEnum.None)
                {
                    _logger.LogDebug($"Request received: {localEP}<-{remoteEP} {req.StatusLine}");
                    _logger.LogTrace(req.ToString());
                }
            };

            _sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                _logger.LogDebug($"Request sent: {localEP}->{remoteEP} {req.StatusLine}");
                _logger.LogTrace(req.ToString());
            };

            _sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                _logger.LogDebug($"Response received: {localEP}<-{remoteEP} {resp.ShortDescription}");
                _logger.LogTrace(resp.ToString());
            };

            _sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                _logger.LogDebug($"Response sent: {localEP}->{remoteEP} {resp.ShortDescription}");
                _logger.LogTrace(resp.ToString());
            };

            _sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                _logger.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            _sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                _logger.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }
    }
}
