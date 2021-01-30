//-----------------------------------------------------------------------------
// Filename: WebRTCSignalController.cs
//
// Description: A web API controller to serve as a simple WebRTC signalling
// implementation.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using signalrtc.DataAccess;
using SIPSorcery.Net;

namespace signalrtc.Controllers.api
{
    [EnableCors(Startup.CORS_POLICY_NAME)]
    [Route("api/[controller]")]
    [ApiController]
    public class WebRTCSignalController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<WebRTCSignalController> _logger;

        public WebRTCSignalController(
            SIPAssetsDbContext context,
            IConfiguration config,
            ILogger<WebRTCSignalController> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Gets a list of the "callers" that have one or more current WebRTC signals pending 
        /// for the "to" identity.
        /// </summary>
        /// <param name="to">The "callee" to get the list of callers for.</param>
        /// <returns>A list of callers that have one or more pending signals for the callee.</returns>
        //[HttpGet]
        //public async Task<ActionResult<IEnumerable<string>>> GetCallers(string to)
        //{
        //    if (string.IsNullOrEmpty(to))
        //    {
        //        return BadRequest();
        //    }

        //    return await _context.WebRTCSignals.Where(x => x.To.ToLower() == to.ToLower())
        //            .OrderBy(x => x.From)
        //            .Select(x => x.From)
        //            .Distinct()
        //            .ToListAsync();
        //}

        /// <summary>
        /// Gets a list of the WebRTC signals for a single call. The signals are for the "to" 
        /// identity and supplied by the "from" identity.
        /// </summary>
        /// <param name="to">The identity to get the WebRTC signals for.</param>
        /// <param name="from">The identity to get the WebRTC signals from.</param>
        /// <param name="type">Optional. A string to filter the types of signals to return.</param>
        /// <returns>A list of </returns>
        /// <example>
        /// $.ajax({
        ///     url: 'https://localhost:5001/us/them`,
        ///     type: 'GET',
        ///     success: onSuccess,
        ///     error: onError
        /// });
        /// </example>
        [HttpGet("{to}/{from}/{type=any}")]
        public async Task<ActionResult<string>> GetSignalsForCaller(string to, string from, WebRTCSignalTypesEnum type = WebRTCSignalTypesEnum.any)
        {
            if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(from))
            {
                return BadRequest();
            }

            var query = _context.WebRTCSignals.Where(x =>
                x.To.ToLower() == to.ToLower() &&
                x.From.ToLower() == from.ToLower() &&
                x.DeliveredAt == null);

            if(type != WebRTCSignalTypesEnum.any)
            {
                query = query.Where(x => x.SignalType == type.ToString());
            }

            var nextSignal = await query
                .OrderBy(x => x.Inserted)
                .FirstOrDefaultAsync();

            if(nextSignal != null)
            {
                nextSignal.DeliveredAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return nextSignal.Signal;
            }
            else
            {
                return NoContent();
            }
        }

        /// <summary>
        /// Inserts a new WebRTC signal message.
        /// </summary>
        /// <param name="from">The source identity for the signal message.</param>
        /// <param name="to">The destination identity for the signal message.</param>
        /// <param name="sdp">The JSON formatted SDP offer or answer message.</param>
        /// <example>
        /// pc = new RTCPeerConnection();
        /// sdpOfferInit = await pc.createOffer();
        /// $.ajax({
        ///        url: 'https://localhost:5001/sdp/us/them',
        ///        type: 'PUT',
        ///        contentType: 'application/json',
        ///        data: JSON.stringify(pc.localDescription),
        ///        success: onSuccess,
        ///        error: onError
        ///   });
        /// </example>
        [HttpPut("sdp/{from}/{to}")]
        public async Task<IActionResult> Put(string from, string to, [FromBody] RTCSessionDescriptionInit sdp)
        {
            if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(from) || sdp == null || sdp.sdp == null)
            {
                _logger.LogWarning($"WebRTC signal controller PUT sdp request had invalid parameters.");
                return BadRequest();
            }

            if (sdp.type == RTCSdpType.offer)
            {
                await ExpireExisting(from, to);
            }

            WebRTCSignal sdpSignal = new WebRTCSignal
            {
                ID = Guid.NewGuid(),
                To = to,
                From = from,
                SignalType = WebRTCSignalTypesEnum.sdp.ToString(),
                Signal = sdp.toJSON(),
                Inserted = DateTime.UtcNow
            };

            _context.WebRTCSignals.Add(sdpSignal);

             await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Inserts a new WebRTC signal message.
        /// </summary>
        /// <param name="from">The source identity for the signal message.</param>
        /// <param name="to">The destination identity for the signal message.</param>
        /// <param name="ice">The JSON formatted ICE candidate.</param>
        /// <example>
        /// pc = new RTCPeerConnection();
        /// pc.onicecandidate = evt => {
        ///   evt.candidate && 
        ///   $.ajax({
        ///        url: 'https://localhost:5001/ice/us/them',
        ///        type: 'PUT',
        ///        contentType: 'application/json',
        ///        data: JSON.stringify(evt.candidate),
        ///        success: onSuccess,
        ///        error: onError
        ///   });
        /// };
        /// </example>
        [HttpPut("ice/{from}/{to}")]
        public async Task<IActionResult> PutIce(string from, string to, [FromBody] RTCIceCandidateInit ice)
        {
            if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(from) || ice == null || ice.candidate == null)
            {
                _logger.LogWarning($"WebRTC signal controller PUT ice candidate request had invalid parameters.");
                return BadRequest();
            }

            WebRTCSignal iceSignal = new WebRTCSignal
            {
                ID = Guid.NewGuid(),
                To = to,
                From = from,
                SignalType = WebRTCSignalTypesEnum.ice.ToString(),
                Signal = ice.toJSON(),
                Inserted = DateTime.UtcNow
            };

            _context.WebRTCSignals.Add(iceSignal);

            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Removes any pending WebRTC signal messages for a WebRTC source and destination. The idea is that
        /// a new SDP offer invalidates any previous SDP and ICE messages.
        /// </summary>
        /// <param name="from">The identity of the peer that set the SDP offer or answer.</param>
        /// <param name="to">>The identity of the destination peer for the SDP offer or answer.</param>
        private async Task ExpireExisting(string from, string to)
        {
            var existing = await _context.WebRTCSignals.Where(x => 
                (from.ToLower() == x.From.ToLower() && to.ToLower() == x.To.ToLower()) ||
                 (to.ToLower() == x.From.ToLower() && from.ToLower() == x.To.ToLower()))
               .ToArrayAsync();

            if (existing?.Length > 0)
            {
                _context.RemoveRange(existing);
                await _context.SaveChangesAsync();
            }
        }
    }
}
