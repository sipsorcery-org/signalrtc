using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class CDR
    {
        public CDR()
        {
            SIPCalls = new HashSet<SIPCall>();
        }

        public string ID { get; set; }
        public string Inserted { get; set; }
        public string Direction { get; set; }
        public string Created { get; set; }
        public string DstUser { get; set; }
        public string DstHost { get; set; }
        public string DstUri { get; set; }
        public string FromUser { get; set; }
        public string FromName { get; set; }
        public string FromHeader { get; set; }
        public string CallID { get; set; }
        public string LocalSocket { get; set; }
        public string RemoteSocket { get; set; }
        public string BridgeID { get; set; }
        public string InProgressAt { get; set; }
        public long? InProgressStatus { get; set; }
        public string InProgressReason { get; set; }
        public long? RingDuration { get; set; }
        public string AnsweredAt { get; set; }
        public long? AnsweredStatus { get; set; }
        public string AnsweredReason { get; set; }
        public long? Duration { get; set; }
        public string HungupAt { get; set; }
        public string HungupReason { get; set; }

        public virtual ICollection<SIPCall> SIPCalls { get; set; }
    }
}
