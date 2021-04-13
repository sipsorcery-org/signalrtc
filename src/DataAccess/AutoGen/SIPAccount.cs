using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPAccount
    {
        public SIPAccount()
        {
            SIPRegistrarBindings = new HashSet<SIPRegistrarBinding>();
        }

        public string ID { get; set; }
        public string DomainID { get; set; }
        public string SIPDialPlanID { get; set; }
        public string SIPUsername { get; set; }
        public string HA1Digest { get; set; }
        public long IsDisabled { get; set; }
        public string Inserted { get; set; }

        public virtual SIPDomain Domain { get; set; }
        public virtual SIPDialPlan SIPDialPlan { get; set; }
        public virtual ICollection<SIPRegistrarBinding> SIPRegistrarBindings { get; set; }
    }
}
