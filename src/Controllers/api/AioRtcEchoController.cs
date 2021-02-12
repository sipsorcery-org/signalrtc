//-----------------------------------------------------------------------------
// Filename: AioRtcEchoController.cs
//
// Description: An API controller that passes through requests to an aiortc 
// WebRTC echo test daemon. The reason to use this controller is to allow the 
// echo test service to operate on a well known end point.
//
// The aiortc project is a Python WebRTC library, see
// https://github.com/aiortc/aiortc.
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

using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace signalrtc.Controllers
{
    [EnableCors(Startup.CORS_POLICY_NAME)]
    [Route("aiortc/echo")]
    [ApiController]
    public class AioRtcEchoController : ControllerBase
    {
        private const string REST_CONTENT_TYPE = "application/json";

        private ILogger<SIPSorceryEchoController> _logger;

        private readonly string _aioRtcEchoTestRestUrl;

        public AioRtcEchoController(IConfiguration config,
            ILogger<SIPSorceryEchoController> logger)
        {
            _logger = logger;
            _aioRtcEchoTestRestUrl = config[ConfigKeys.AIORTC_ECHOTEST_REST_URL];
        }

        /// <summary>
        /// The client peer needs to supply its SDP offer which  will be forwarded to the echo 
        /// test daemon. The SDP answer from the daemon will be returned to the client.
        /// </summary>
        /// <param name="offer">The JSON encoded SDP offer from the WebRTC client peer.</param>
        /// <remarks>
        /// Sanity Test:
        /// curl -X POST https://localhost:5001/aiortc/echo/offer -H "Content-Type: application/json" -v -d "1234"
        /// </remarks>
        [HttpPost("offer")]
        public async Task<ActionResult<string>> Offer([FromBody] RTCSessionDescriptionInit offer)
        {
            _logger.LogDebug($"Echo controller posting offer to {_aioRtcEchoTestRestUrl}.");
            _logger.LogDebug($"Offer={offer}");

            HttpClient client = new HttpClient();
            var postResp = await client.PostAsync($"{_aioRtcEchoTestRestUrl}", new StringContent(offer.toJSON(), Encoding.UTF8, REST_CONTENT_TYPE));

            _logger.LogDebug($"Echo controller post offer response {postResp.StatusCode}:{postResp.ReasonPhrase}.");

            if (postResp.IsSuccessStatusCode)
            {
                var answer = await postResp.Content.ReadAsStringAsync();
                _logger.LogDebug($"Answer: {answer}.");
                return answer;
            }
            else
            {
                return BadRequest(postResp.ReasonPhrase);
            }
        }
    }
}
