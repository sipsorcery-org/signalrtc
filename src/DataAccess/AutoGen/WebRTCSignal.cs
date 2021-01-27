using System;
using System.Collections.Generic;

#nullable disable

namespace devcall.DataAccess
{
    public partial class WebRTCSignal
    {
        public Guid ID { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string SignalType { get; set; }
        public string Signal { get; set; }
        public DateTime Inserted { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }
}
