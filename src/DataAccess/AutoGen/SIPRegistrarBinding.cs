using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPRegistrarBinding
    {
        public string ID { get; set; }
        public string SIPAccountID { get; set; }
        public string UserAgent { get; set; }
        public string ContactURI { get; set; }
        public long Expiry { get; set; }
        public string RemoteSIPSocket { get; set; }
        public string ProxySIPSocket { get; set; }
        public string RegistrarSIPSocket { get; set; }
        public string LastUpdate { get; set; }
        public string ExpiryTime { get; set; }

        public virtual SIPAccount SIPAccount { get; set; }
    }
}
