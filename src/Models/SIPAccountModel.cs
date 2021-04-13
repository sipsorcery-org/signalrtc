using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SIPSorcery.SIP.App;

namespace signalrtc.Models
{
    public class SIPAccountModel : ISIPAccount
    {
        public Guid ID { get; set; }
        public string SIPUsername { get; set; }
        public string SIPPassword { get; set; }
        public string HA1Digest { get; set; }
        public string SIPDomain { get; set; }
        public bool IsDisabled { get; set; }
    }
}
