//-----------------------------------------------------------------------------
// Filename: JanusEchoController.cs
//
// Description: A web API controller to set up an Echo Test with a Janus
// WebRTC server.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace signalrtc.Controllers.api
{
    [EnableCors(Startup.CORS_POLICY_NAME)]
    [Route("janus/echo")]
    [ApiController]
    public class JanuEchoController : ControllerBase
    {
        public const int MAX_JANUS_ECHO_DURATION = 180;

        private readonly IConfiguration _config;
        private readonly ILogger<JanuEchoController> _logger;

        /// <summary>
        /// Base URL for the Janus REST server.
        /// </summary>
        private readonly string _janusUrl;

        public JanuEchoController(
            IConfiguration config,
            ILogger<JanuEchoController> logger)
        {
            _config = config;
            _logger = logger;

            _janusUrl = _config[ConfigKeys.JANUS_URL];
        }

        /// <summary>
        /// Sets up a WebRTC Echo Test with the Janus WebRTC server.
        /// </summary>
        /// <param name="sdp">The SDP offer from a WebRTC peer that will connect to the Janus Echo Test plugin.</param>
        /// <param name="duration">The maximum duration in seconds for the </param>
        /// <returns>The SDP answer from Janus.</returns>
        /// <remarks>
        /// Sanity test:
        /// curl -X POST https://localhost:5001/api/webrtcsignal/janus?duration=10 -H "Content-Type: application/json" -v -d "1234"
        /// </remarks>
        [HttpPost("offer")]
        public async Task<IActionResult> JanusEcho([FromBody] RTCSessionDescriptionInit offer, int duration = MAX_JANUS_ECHO_DURATION)
        {
            if (offer == null)
            {
                _logger.LogWarning($"Janus echo controller was supplied an empty SDP offer.");
                return BadRequest("SDP offer missing");
            }
            else if(duration > MAX_JANUS_ECHO_DURATION)
            {
                duration = MAX_JANUS_ECHO_DURATION;
            }

            _logger.LogDebug($"Creating Janus echo test with duration {duration}s.");

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(duration * 1000);
            var janusClient = new JanusRestClient(_janusUrl, _logger, cts.Token);
            
            var startSessionResult = await janusClient.StartSession();

            if (!startSessionResult.Success)
            {
                return Problem("Failed to create Janus session.");
            }
            else
            {
                var waitForAnswer = new TaskCompletionSource<string>();

                janusClient.OnJanusEvent += (resp) =>
                {
                    if (resp.jsep != null)
                    {
                        _logger.LogDebug($"janus get event jsep={resp.jsep.type}.");
                        _logger.LogDebug($"janus SDP Answer: {resp.jsep.sdp}");

                        waitForAnswer.SetResult(resp.jsep.sdp);
                    }
                };

                var pluginResult = await janusClient.StartEcho(offer.sdp);
                if (!pluginResult.Success)
                {
                    await janusClient.DestroySession();
                    waitForAnswer.SetCanceled();
                    cts.Cancel();
                    return Problem("Failed to create Janus Echo Test Plugin instance.");
                }
                else
                {
                    using (cts.Token.Register(() =>
                    {
                        // This callback will be executed if the token is cancelled.
                        waitForAnswer.TrySetCanceled();
                    }))
                    {
                        // At this point we're waiting for a response on the Janus long poll thread that
                        // contains the SDP answer to send back tot he client.
                        try
                        {
                           var sdpAnswer = await waitForAnswer.Task;
                            _logger.LogDebug("SDP answer ready, sending to client.");
                            return Ok(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdpAnswer });
                        }
                        catch (TaskCanceledException)
                        {
                            await janusClient.DestroySession();
                            return Problem("Janus operation timed out waiting for Echo Test plugin SDP answer.");
                        }
                    }
                }
            }
        }
    }
}
