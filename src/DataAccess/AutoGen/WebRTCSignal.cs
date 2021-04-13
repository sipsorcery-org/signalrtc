using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class WebRTCSignal
    {
        public string ID { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string SignalType { get; set; }
        public string Signal { get; set; }
        public string Inserted { get; set; }
        public string DeliveredAt { get; set; }
    }
}
