// ============================================================================
// FileName: SIPDomainManager.cs
//
// Description:
// Maintains a list of domains and domain aliases that can be used by various
// SIP Server agents. For example allows a SIP Registrar or Proxy to check the 
// domain on an incoming request to see if it is serviced at this location.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Jul 2008	Aaron Clauson	Created, Hobart, Australia.
// 30 Dec 2020  Aaron Clauson   Added to server project.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using signalrtc.DataAccess;

namespace signalrtc
{
    /// <summary>
    /// This class maintains a list of domains that are being maintained by this process.
    /// </summary>
    public class SIPDomainManager
    {
        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<SIPDomainManager>();

        private SIPDomainDataLayer _sipDomainDataLayer;
        private Dictionary<string, SIPDomain> m_domains = new Dictionary<string, SIPDomain>();  // Records the domains that are being maintained.

        public SIPDomainManager(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _sipDomainDataLayer = new SIPDomainDataLayer(dbContextFactory);
        }

        /// <summary>
        /// Loads the SIP domains from the databsae into an in-memory dictionary for fast lookups.
        /// </summary>
        public async Task Load()
        {
            var domains = await _sipDomainDataLayer.GetListAsync();

            if (domains == null || domains.Count() == 0)
            {
                throw new ApplicationException("No SIP domains could be loaded from the database. There needs to be at least one domain.");
            }
            else
            {
                m_domains.Clear();

                foreach (SIPDomain sipDomain in domains)
                {
                    AddDomain(sipDomain);
                }
            }
        }

        private void AddDomain(SIPDomain sipDomain)
        {
            if (sipDomain == null)
            {
                Logger.LogWarning("SIPDomainManager cannot add a null SIPDomain object, ignoring.");
            }
            else
            {
                if (!m_domains.ContainsKey(sipDomain.Domain.ToLower()))
                {
                    Logger.LogDebug($"SIPDomainManager adding domain: {sipDomain.Domain} with alias list {sipDomain.AliasList}.");

                    m_domains.Add(sipDomain.Domain.ToLower(), sipDomain);
                }
                else
                {
                    Logger.LogWarning($"SIPDomainManager ignoring duplicate domain entry for {sipDomain.Domain.ToLower()}.");
                }
            }
        }

        /// <summary>
        /// Checks whether there the supplied hostname represents a serviced domain or alias.
        /// </summary>
        /// <param name="host">The hostname to check for a serviced domain for.</param>
        /// <returns>The canconical domain name for the host if found or null if not.</returns>
        public string GetCanonicalDomain(string host)
        {
            SIPDomain domain = GetSIPDomain(host);
            return (domain != null) ? domain.Domain.ToLower() : null;
        }

        private SIPDomain GetSIPDomain(string host)
        {
            //logger.Debug("SIPDomainManager GetDomain for " + host + ".");

            if (host == null)
            {
                return null;
            }
            else
            {
                if (m_domains.ContainsKey(host.ToLower()))
                {
                    return m_domains[host.ToLower()];
                }
                else
                {
                    foreach (SIPDomain SIPDomain in m_domains.Values)
                    {
                        if (SIPDomain.Aliases != null)
                        {
                            foreach (string alias in SIPDomain.Aliases)
                            {
                                if (alias.ToLower() == host.ToLower())
                                {
                                    return SIPDomain;
                                }
                            }
                        }
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// Checks whether a host name is in the list of supported domains and aliases.
        /// </summary>
        /// <param name="host"></param>
        /// <returns>True if the host is present as a domain or an alias, false otherwise.</returns>
        public bool HasDomain(string host)
        {
            return GetSIPDomain(host) != null;
        }
    }
}
