//-----------------------------------------------------------------------------
// Filename: SIPDialPlanDataLayer.cs
//
// Description: Data access methods for the SIPDialPlan entity. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace devcall.DataAccess
{
    public class SIPDialPlanDataLayer
    {
        private readonly IDbContextFactory<SIPAssetsDbContext> _dbContextFactory;

        public SIPDialPlanDataLayer(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public Task<SIPDialPlan> Get(Guid id)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                return db.SIPDialPlans.Where(x => x.ID == id).FirstOrDefaultAsync();
            }
        }

        public async Task<SIPDialPlan> Get(Expression<Func<SIPDialPlan, bool>> where)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                return await db.SIPDialPlans.Where(where).FirstOrDefaultAsync();
            }
        }

        public async Task<DateTime> UpdateDialPlanScript(string dialPlanScript)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var dialplan = await db.SIPDialPlans.FirstOrDefaultAsync();

                dialplan.DialPlanScript = dialPlanScript;
                dialplan.LastUpdate = DateTime.UtcNow;

                await db.SaveChangesAsync();

                return dialplan.LastUpdate;
            }
        }
    }
}
