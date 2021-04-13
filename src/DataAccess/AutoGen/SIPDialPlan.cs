using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPDialPlan
    {
        public SIPDialPlan()
        {
            SIPAccounts = new HashSet<SIPAccount>();
        }

        public string ID { get; set; }
        public string DialPlanName { get; set; }
        public string DialPlanScript { get; set; }
        public string Inserted { get; set; }
        public string LastUpdate { get; set; }
        public long AcceptNonInvite { get; set; }

        public virtual ICollection<SIPAccount> SIPAccounts { get; set; }
    }
}
