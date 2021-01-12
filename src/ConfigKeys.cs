﻿//-----------------------------------------------------------------------------
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

namespace devcall
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
        /// This is the default SIP domain that will be used for managing SIP accounts.
        /// </summary>
        public const string SIP_DOMAIN = "SIPDomain";

        /// <summary>
        /// The port that the SIP transport layer will attempt to listen on.
        /// </summary>
        public const string SIP_LISTEN_PORT = "SIPListenPort";

        /// <summary>
        /// The public IP address that will be used in SIP Contact headers set by the SIP transport
        /// layer. Typically this is the public IP address of the SIP load balancer.
        /// </summary>
        public const string SIP_PUBLIC_IPADDRESS = "SIPPublicIPAddress";
    }
}