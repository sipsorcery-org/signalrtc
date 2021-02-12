//-----------------------------------------------------------------------------
// Filename: ConfigKeys.cs
//
// Description: List of the configuration keys that are used by the application. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace signalrtc
{
    /// <summary>
    /// Convenience class to hold the keys that are used to get configuration settings from
    /// the appSettings files and elsewhere.
    /// </summary>
    public static class ConfigKeys
    {
        /// <summary>
        /// The Azure key vault to load secrets and certificates from.
        /// </summary>
        public const string KEY_VAULT_NAME = "KeyVaultName";

        /// <summary>
        /// The name of the certificate in the Azure Key Vault to use for the HTTPS
        /// end point.
        /// </summary>
        public const string KEY_VAULT_HTTPS_CERTIFICATE_NAME = "KeyVaultHttpsCertificateName";

        /// <summary>
        /// The path to a certificate file to use for HTTPS and SIPS connections. The use of a 
        /// certificate file should only be for local development. Ideally in production, where a 
        /// load balancer is used, the certificate should be pulled from a central store.
        /// </summary>
        public const string HTTPS_CERTIFICATE_PATH = "HttpsCertificatePath";

        /// <summary>
        /// If using GitHub OAuth for authorisation this is the name of the application.
        /// </summary>
        public const string GITHUB_OAUTH_APPNAME = "GitHub:AppName";

        /// <summary>
        /// If using GitHub OAuth for authorisation this is the client ID of the application.
        /// </summary>
        public const string GITHUB_OAUTH_CLIENTID = "GitHub:ClientID";

        /// <summary>
        /// If using GitHub OAuth for authorisation this is the client secret for the application.
        /// </summary>
        public const string GITHUB_OAUTH_CLIENTSECRET = "GitHub:ClientSecret";

        /// <summary>
        /// The list of users that have been granted administrative privileges.
        /// </summary>
        public const string ADMIN_USERNAMES = "Admins";

        /// <summary>
        /// This is the default SIP domain that will be used for managing SIP accounts.
        /// </summary>
        public const string SIP_DOMAIN = "SIPDomain";

        /// <summary>
        /// The port that the SIP transport layer will attempt to listen on.
        /// </summary>
        public const string SIP_LISTEN_PORT = "SIPSorcery:SIPListenPort";

        /// <summary>
        /// The port that the SIP transport layer will attempt to listen on for TLS conections.
        /// </summary>
        public const string SIP_TLS_LISTEN_PORT = "SIPSorcery:SIPTlsListenPort";

        /// <summary>
        /// The contact host that will be used in Contact headers set by the SIP transport
        /// layer. Typically this is a hostname pointing to the SIP load balancer.
        /// </summary>
        public const string SIP_PUBLIC_CONTACT_HOSTNAME = "SIPSorcery:ContactHost:PublicHostname";

        /// <summary>
        /// The contact host that will be used in Contact headers set by the SIP transport
        /// layer for IPv4 SIP messages. Typically this is the public IPv4 address of the 
        /// SIP load balancer.
        /// </summary>
        public const string SIP_PUBLIC_CONTACT_PUBLICIPV4 = "SIPSorcery:ContactHost:PublicIPv4";

        /// <summary>
        /// The contact host that will be used in Contact headers set by the SIP transport
        /// layer for IPv6 SIP messages. Typically this is the public IPv6 address of the 
        /// SIP load balancer.
        /// </summary>
        public const string SIP_PUBLIC_CONTACT_PUBLICIPV6 = "SIPSorcery:ContactHost:PublicIPv6";

        /// <summary>
        /// A list of private subnets (private meaning the SIP traffic should not traverse the 
        /// Load Balancer) that should not have the public Contact Host set.
        /// </summary>
        public const string SIP_PRIVATE_CONTACT_SUBNETS = "SIPSorcery:ContactHost:PrivateSubnets";

        /// <summary>
        /// The URL of the Janus Server REST end point.
        /// </summary>
        public const string JANUS_URL = "JanusURL";

        /// <summary>
        /// The URL for the SIPSorcery WebRTC echo test daemon. It can be utilised to create
        /// a WebRTC echo test session.
        /// </summary>
        public const string SIPSORCERY_ECHOTEST_REST_URL = "SIPSorceryEchoTestRestURL";

        /// <summary>
        /// The URL for the aiortc Python daemon functioning as a WebRTC echo test.
        /// </summary>
        public const string AIORTC_ECHOTEST_REST_URL = "AioRTCEchoTestURL";
    }
}
