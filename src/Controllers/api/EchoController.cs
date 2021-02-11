//-----------------------------------------------------------------------------
// Filename: EchoController.cs
//
// Description: An API controller that passes through requests to a WebRTC 
// echo test daemon. The reason to use this controller is to allow the 
// echo test service to operate on a well knownend point.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
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
    [Route("echo")]
    [ApiController]
    public class EchoController : ControllerBase
    {
        private const string REST_CONTENT_TYPE = "application/json";

        private ILogger<EchoController> _logger;

        private readonly string _echoTestRestUrl;

        public EchoController(IConfiguration config,
            ILogger<EchoController> logger)
        {
            _logger = logger;
            _echoTestRestUrl = config[ConfigKeys.WEBRTC_ECHOTEST_REST_URL];
        }

        /// <summary>
        /// The first step in the echo test. A request for an SDP offer is forwarded to the echo test
        /// daemon which will return a JSON encoded offer.
        /// </summary>
        /// <param name="id">A unique ID for this WebRTC session. The format does not matter but 
        /// if the ID is already in use an error response will be returned.</param>
        /// <returns>A JSON encoded SDP offer.</returns>
        /// <remarks>
        /// Sanity Test:
        /// curl http://localhost:5000/echo/offer/1
        /// </remarks>
        [HttpGet("offer/{id}")]
        public async Task<ActionResult<string>> Offer(string id)
        {
            _logger.LogDebug($"Echo controller requesting offer from {_echoTestRestUrl}/offer/{id}.");
            HttpClient client = new HttpClient();
            var resp = await client.GetAsync($"{_echoTestRestUrl}/offer/{id}");
            var offer = await resp.Content.ReadAsStringAsync();
            //_logger.LogDebug($"Offer for {id}: {offer}.");
            return offer;
        }

        /// <summary>
        /// The second step in the echo test. The client peer needs to supply its SDP answer which 
        /// will be forwarded to the echo test daemon.
        /// </summary>
        /// <param name="id">The unique ID for the WebRTC session. This must match the value that
        /// was used in the first get offer step.</param>
        /// <param name="answer">The JSON encoded SDP answer from the WebRTC client peer.</param>
        /// <remarks>
        /// Sanity Test:
        /// curl -X POST https://localhost:5001/echo/answer/1234 -H "Content-Type: application/json" -v -d "1234"
        /// </remarks>
        [HttpPost("answer/{id}")]
        public async Task<ActionResult> Answer(string id, [FromBody] RTCSessionDescriptionInit answer)
        {
            _logger.LogDebug($"Echo controller posting answer to {_echoTestRestUrl}/answer/{id}.");
            //_logger.LogDebug($"Answer={answer}");

            HttpClient client = new HttpClient();
            var postResp = await client.PostAsync($"{_echoTestRestUrl}/answer/{id}", new StringContent(answer.toJSON(), Encoding.UTF8, REST_CONTENT_TYPE));

            _logger.LogDebug($"Echo controller post answer response {postResp.StatusCode}:{postResp.ReasonPhrase}.");

            return postResp.IsSuccessStatusCode ? Ok() : BadRequest(postResp.ReasonPhrase);
        }

        /// <summary>
        /// This action allows the webrtc client peer to supply its ICE candidates. They will be forwarded
        /// to the echo test daemon. If the daemon is operating on a public IP address the client's ICE
        /// candidates will typically not be required.
        /// </summary>
        /// <param name="id">The unique ID for the WebRTC session. This must match the value that
        /// was used in the first get offer step.</param>
        /// <param name="candidate">The JSON encoded ICE candidate from the WebRTC client peer.</param>
        [HttpPost("ice/{id}")]
        public async Task<ActionResult> Ice(string id, [FromBody] RTCIceCandidateInit candidate)
        {
            _logger.LogDebug($"Echo controller posting ice candidate to {_echoTestRestUrl}/ice/{id}.");
            _logger.LogDebug($"Candidate={candidate.candidate}");

            HttpClient client = new HttpClient();
            var postResp = await client.PostAsync($"{_echoTestRestUrl}/ice/{id}", new StringContent(candidate.toJSON(), Encoding.UTF8, REST_CONTENT_TYPE));

            _logger.LogDebug($"Echo controller post ice response {postResp.StatusCode}:{postResp.ReasonPhrase}.");

            return postResp.IsSuccessStatusCode ? Ok() : BadRequest(postResp.ReasonPhrase);
        }
    }
}
