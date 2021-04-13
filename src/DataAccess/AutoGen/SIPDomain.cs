using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPDomain
    {
        public SIPDomain()
        {
            SIPAccounts = new HashSet<SIPAccount>();
        }

        public string ID { get; set; }
        public string Domain { get; set; }
        public string AliasList { get; set; }
        public string Inserted { get; set; }

        public virtual ICollection<SIPAccount> SIPAccounts { get; set; }
    }
}
