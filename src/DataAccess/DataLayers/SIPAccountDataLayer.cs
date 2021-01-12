//-----------------------------------------------------------------------------
// Filename: SIPAccountDataLayer.cs
//
// Description: Data access methods for the SIPAccount entity. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace devcall.DataAccess
{
    public class SIPAccountDataLayer
    {
        private readonly IDbContextFactory<SIPAssetsDbContext> _dbContextFactory;

        public SIPAccountDataLayer(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<SIPAccount> GetSIPAccount(string username, string domain, bool includeBindings = false)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentNullException(nameof(username), "The username parameter must be specified for GetSIPAccount.");
            }
            else if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentNullException(nameof(domain), "The domain parameter must be specified for GetSIPAccount.");
            }

            using (var db = _dbContextFactory.CreateDbContext())
            {
                var query = db.SIPAccounts.Include(x => x.Domain).AsQueryable();
                if(includeBindings)
                {
                    query = query.Include(y => y.SIPRegistrarBindings);
                }

                SIPAccount sipAccount = await query.Where(x => x.SIPUsername.ToLower() == username.ToLower() &&
                                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefaultAsync();
                //if (sipAccount == null)
                //{
                //    // A full lookup failed. Now try a partial lookup if the incoming username is in a dotted domain name format.
                //    if (username.Contains("."))
                //    {
                //        string usernameSuffix = username.Substring(username.LastIndexOf(".") + 1);
                //        sipAccount = db.SIPAccounts.Include(x => x.Domain).Where(x => x.SIPUsername.ToLower() == usernameSuffix.ToLower() &&
                //                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefault();
                //    }
                //}

                return sipAccount;
            }
        }

        /// <summary>
        /// Checks whether a SIP account for a specific username exists.
        /// </summary>
        public async Task<bool> Exists(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentNullException(nameof(username), "The username parameter must be specified for Exists.");
            }

            using (var db = _dbContextFactory.CreateDbContext())
            {
                return await db.SIPAccounts.AnyAsync(x => x.SIPUsername.ToLower() == username.ToLower());
            }
        }

        public async Task<SIPAccount> Create(string username, string domain, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(username), "The username, domain and password parameters must be specified when creating a SIP Account.");
            }

            SIPAccount sipAccount = new SIPAccount
            {
                SIPUsername = username,
                SIPPassword = password
            };

            using (var db = _dbContextFactory.CreateDbContext())
            {
                SIPDomain sipDomain = await db.SIPDomains.Where(x => x.Domain.ToLower() == domain.ToLower()).SingleOrDefaultAsync();

                if (sipDomain == null)
                {
                    throw new ApplicationException($"SIP Domain not found for {domain} when creating SIP Account.");
                }
                else
                {
                    sipAccount.ID = Guid.NewGuid();
                    sipAccount.Inserted = DateTime.UtcNow;
                    sipAccount.DomainID = sipDomain.ID;
                    await db.SIPAccounts.AddAsync(sipAccount);
                    await db.SaveChangesAsync();
                }
            }

            return sipAccount;
        }

        public async Task UpdatePassword(string username, string domain, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(username), "The username, domain and password parameters must be specified when updating a SIP Account password.");
            }

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(domain))
            {
                using (var db = _dbContextFactory.CreateDbContext())
                {
                    SIPAccount sipAccount = await db.SIPAccounts.Where(x => x.SIPUsername.ToLower() == username.ToLower() &&
                                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefaultAsync();

                    if (sipAccount == null)
                    {
                        throw new ApplicationException($"The SIP account for {username} and {domain} could not be found when attempting a password update.");
                    }
                    else
                    {
                        sipAccount.SIPPassword = password;
                        await db.SaveChangesAsync();
                    }
                }
            }
        }

        public async Task Delete(string username, string domain)
        {
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(domain))
            {
                using (var db = _dbContextFactory.CreateDbContext())
                {
                    SIPAccount sipAccount = await db.SIPAccounts.Where(x => x.SIPUsername.ToLower() == username.ToLower() &&
                                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefaultAsync();

                    if(sipAccount != null)
                    {
                        db.SIPAccounts.Remove(sipAccount);
                        await db.SaveChangesAsync();
                    }
                }
            }
        }
    }
}
