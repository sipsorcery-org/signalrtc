using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace devcall
{
    public class SIPDialPlanGlobals
    {
        public UASInviteTransaction UasTx { get; set; }
        public ISIPAccount From { get; set; }
    }
}
