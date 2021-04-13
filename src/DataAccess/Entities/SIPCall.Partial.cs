// ============================================================================
// FileName: SIPCall.Partial.cs
//
// Description:
// Represents the SIPCall entity. This partial class is used to apply 
// additional properties or metadata to the auto generated SIPCall class.
//
// A SIPCall corresponds to the establishment of a SIP Dialogue between 
// two SIP user agents.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using SIPSorcery.SIP;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPCall
    {
        public SIPCall()
        { }

        /// <summary>
        /// This constructor translates the SIP layer dialogue to a data access
        /// layer entity.
        /// </summary>
        /// <param name="dialogue">The SIP layer dialogue to translate.</param>
        public SIPCall(SIPDialogue dialogue)
        {
            ID = dialogue.Id.ToString();
            CDRID = dialogue.CDRId != Guid.Empty ? dialogue.CDRId.ToString() : null;
            LocalTag = dialogue.LocalTag;
            RemoteTag = dialogue.RemoteTag;
            CallID = dialogue.CallId;
            CSeq = dialogue.CSeq;
            BridgeID = dialogue.BridgeId.ToString();
            RemoteTarget = dialogue.RemoteTarget.ToString();
            LocalUserField = dialogue.LocalUserField.ToString();
            RemoteUserField = dialogue.RemoteUserField.ToString();
            ProxySendFrom = dialogue.ProxySendFrom;
            RouteSet = dialogue.RouteSet?.ToString();
            CallDurationLimit = dialogue.CallDurationLimit;
            Direction = dialogue.Direction.ToString();
            Inserted = dialogue.Inserted.ToString("o");
            RemoteSocket = dialogue.RemoteSIPEndPoint?.ToString();
        }

        /// <summary>
        /// Translates a data access layer SIPDialog entity to a SIP layer SIPDialogue.
        /// </summary>
        public SIPDialogue ToSIPDialogue()
        {
            SIPDialogue dialogue = new SIPDialogue();

            dialogue.Id = !string.IsNullOrEmpty(ID) ? new Guid(ID) : Guid.Empty;
            dialogue.CDRId = !string.IsNullOrEmpty(CDRID) ? new Guid(CDRID) : Guid.Empty;
            dialogue.LocalTag= LocalTag;
            dialogue.RemoteTag = RemoteTag;
            dialogue.CallId = CallID;
            dialogue.CSeq = (int)CSeq;
            dialogue.BridgeId = !string.IsNullOrEmpty(BridgeID) ? new Guid(BridgeID) : Guid.Empty;
            dialogue.RemoteTarget = SIPURI.ParseSIPURIRelaxed(RemoteTarget);
            dialogue.LocalUserField = SIPUserField.ParseSIPUserField(LocalUserField);
            dialogue.RemoteUserField = SIPUserField.ParseSIPUserField(RemoteUserField);
            dialogue.ProxySendFrom = ProxySendFrom;
            dialogue.RouteSet = string.IsNullOrWhiteSpace(RouteSet) ? null : SIPRouteSet.ParseSIPRouteSet(RouteSet);
            dialogue.CallDurationLimit = (int)CallDurationLimit.GetValueOrDefault();
            dialogue.Direction = Enum.Parse<SIPCallDirection>(Direction, true);
            dialogue.Inserted = DateTime.Parse(Inserted);
            dialogue.RemoteSIPEndPoint = (RemoteSocket != null) ? SIPEndPoint.ParseSIPEndPoint(RemoteSocket) : null;

            return dialogue;
        }
    }
}
