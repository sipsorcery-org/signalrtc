using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPCall
    {
        public string ID { get; set; }
        public string CDRID { get; set; }
        public string LocalTag { get; set; }
        public string RemoteTag { get; set; }
        public string CallID { get; set; }
        public long CSeq { get; set; }
        public string BridgeID { get; set; }
        public string RemoteTarget { get; set; }
        public string LocalUserField { get; set; }
        public string RemoteUserField { get; set; }
        public string ProxySendFrom { get; set; }
        public string RouteSet { get; set; }
        public long? CallDurationLimit { get; set; }
        public string Direction { get; set; }
        public string Inserted { get; set; }
        public string RemoteSocket { get; set; }

        public virtual CDR CDR { get; set; }
    }
}
