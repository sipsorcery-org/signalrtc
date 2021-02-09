// ============================================================================
// FileName: webrtc-rest-signaling.cs
//
// Description:
// A simple WebRTC signaling implementation that relies on a HTTP REST server
// to relay messages between two WebRTC peers.
//
// The four REST methods used are:
//
// PUT server/sdp/{localID}/{remoteID}
// PUT server/sdp/{localID}/{remoteID}
// GET server/sdp/{localID}/{remoteID}
// GET server/ice/{localID}/{remoteID}
//
// For two instances of the signaler to communicate they must use opposite 
// local and remote IDs. For example:
//
// Peer 1: localID=alice-123, remoteID=bob-456
// Peer 2: localID=bob-456,   remoteID=alice-123
//
// The format of the IDs does not matter but as there is no guranatee of a
// collision detection mechanism on the server pseudo random values are 
// recommended.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 09 Feb 2021	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

/*
 * This prototype implements a simple signaling mechansim to allow two WebRTC
 * peer connections to communicate and exchange SDP and ICE messages. The signaler
 * relies on the same HTTP REST server being accessible by both peers.
 */
function WebRTCRestSignaler(signalingBaseUrl, localID, remoteID, peerConnection, waitForOffer = false) {

    const SIGNALING_CONNECTED_POLL_PERIOD = 500;
    const SIGNAL_MESSAGE_TYPE_SDP = "sdp";
    const SIGNAL_MESSAGE_TYPE_ICE = "ice";

    var pollTimer;
    var isStopped = false;
    var hasReceivedSdp = false;
    var PC = peerConnection;
    var SignalServerUrl = signalingBaseUrl;
    var WaitForOffer = waitForOffer;

    this.LocalID = localID;
    this.RemoteID = remoteID;

    var sigInstance = this;

    /*
     * Catch peer connection ICE candidates so they can be sent to the signaling server
     * and become available for the remote peer.
     */
    PC.onicecandidate = evt => {
        evt.candidate && console.log(`${sigInstance.LocalID}: local ice candidate ${evt.candidate.candidate}`);
        evt.candidate && sigInstance.sendSignal(JSON.stringify(evt.candidate), SIGNAL_MESSAGE_TYPE_ICE);
    }

    /*
    * Once the ICE connection has been established the signaling server checks can be stopped.
    */
    PC.oniceconnectionstatechange = (evt) => {
        console.log(`${sigInstance.LocalID}: oniceconnectionstatechange: ${PC.iceConnectionState}.`);
        PC.iceConnectionState == "connected" && sigInstance.stop();
    }

    /*
     * Starts the signaling by creating a timer that periodically sends a GET request to
     * the REST server.
     */
    this.start = function () {
        pollTimer = setInterval(sigInstance.pollSignalingServer, SIGNALING_CONNECTED_POLL_PERIOD);
        sigInstance.pollSignalingServer();
    }

    /*
     * Stops the signaling.
     */
    this.stop = function () {
        isStopped = true;
        clearInterval(pollTimer);
        pollTimer = null;
    }

    /*
     * Sends a signaling message to the REST server.
     */
    this.sendSignal = function (msg, messageType) {
        //console.log(`Send signal ${signalingUrl}/${messageType}/${signalingOurID}/${signalingTheirID}`);

        $.ajax({
            url: `${SignalServerUrl}/${messageType}/${sigInstance.LocalID}/${sigInstance.RemoteID}`,
            type: 'PUT',
            contentType: 'application/json',
            data: msg,
            success: sigInstance.onSuccess,
            error: (xhr, status, error) => console.log(`${sigInstance.LocalID}: Signaling send error: ${status}: ${error}`)
        });
    }

    /*
     * Polls the signaling REST serve for any messages that are waiting for this peer.
     * This method should be called periodically until the ICE connection is established.
     */
    this.pollSignalingServer = async function () {

        if (isStopped) {
            clearInterval(pollTimer);
            return;
        }

        let signalType = hasReceivedSdp ? SIGNAL_MESSAGE_TYPE_ICE : SIGNAL_MESSAGE_TYPE_SDP;
        console.log(`${sigInstance.LocalID}: checking signal server ${SignalServerUrl}/${sigInstance.LocalID}/${sigInstance.RemoteID}/${signalType}`);

        $.ajax({
            url: `${SignalServerUrl}/${sigInstance.LocalID}/${sigInstance.RemoteID}/${signalType}`,
            type: 'GET',
            success: sigInstance.onSuccess,
            error: (xhr, status, error) => console.log(`${sigInstance.LocalID}: signaling receive error: ${status}: ${error}`)
        });
    }

    /*
     * Handler for processing JSEP messages retrieved from the signaling server.
     */
    this.onSuccess = async function (data, status) {
        //console.log(`${status}: ${data}`);

        if (status == 'nocontent') {
            if (PC?.remoteDescription == null && PC?.localDescription == null) {
                if (!WaitForOffer) {
                    // There is no offer waiting for us so we'll send the initial offer.
                    console.log(`${sigInstance.LocalID}: Creating offer and sending to signaling server...`);
                    await PC.createOffer()
                        .then(offer => PC.setLocalDescription(offer))
                        .then(() => sigInstance.sendSignal(JSON.stringify(PC.localDescription), SIGNAL_MESSAGE_TYPE_SDP));
                }
            }
        }
        else if (data) {
            var obj = JSON.parse(data);

            if (obj?.candidate) {
                console.log(`${sigInstance.LocalID}: got remote ice candidate ${obj.candidate}`);
                await PC.addIceCandidate(obj);
            }
            else if (obj?.sdp) {
                console.log(`${sigInstance.LocalID}: got remote SDP.`);
                hasReceivedSdp = true;
                await PC.setRemoteDescription(new RTCSessionDescription(obj));
                if (PC.localDescription == null) {
                    await PC.createAnswer()
                        .then(answer => PC.setLocalDescription(answer))
                        .then(() => sigInstance.sendSignal(JSON.stringify(PC.localDescription), SIGNAL_MESSAGE_TYPE_SDP));
                }
            }

            // Poll straight away to get any additional pending messages.
            sigInstance.pollSignalingServer();
        }
    }
}
