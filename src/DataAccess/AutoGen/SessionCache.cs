using System;
using System.Collections.Generic;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SessionCache
    {
        public string Id { get; set; }
        public byte[] Value { get; set; }
        public string ExpiresAtTime { get; set; }
        public long? SlidingExpirationInSeconds { get; set; }
        public string AbsoluteExpiration { get; set; }
    }
}
