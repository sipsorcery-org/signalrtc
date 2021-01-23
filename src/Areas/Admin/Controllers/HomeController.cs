//-----------------------------------------------------------------------------
// Filename: HomeController.cs
//
// Description: Administrator controller to edit the system dialplan.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using devcall.DataAccess;

namespace devcall.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles ="Admin")]
    public class HomeController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ILogger<HomeController> _logger;

        private readonly SIPDialPlanManager _sipDialPlanManager;

        public HomeController(
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            IConfiguration config,
            ILogger<HomeController> logger,
            SIPDialPlanManager sipDialPlanManager)
        {
            _config = config;
            _logger = logger;

            _sipDialPlanManager = sipDialPlanManager;
        }

        public async Task<IActionResult> Index()
        {
            var dialplan = await _sipDialPlanManager.LoadDialPlan();

            return View(nameof(Index), dialplan.DialPlanScript);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDialPlan(string dialPlanScript)
        {
            _logger.LogDebug("Attempting to compile updated dial plan script...");

            var script = dialPlanScript?.Trim();

            DateTime lastUpdated = DateTime.UtcNow;
            string errMessage = _sipDialPlanManager.CompileDialPlan(script, lastUpdated);

            if (errMessage == null)
            {
                _logger.LogDebug("Dial plan compiled successfully, attempting to update database...");

                await _sipDialPlanManager.UpdateDialPlanScript(script, lastUpdated);

                TempData["Success"] = "Dial plan script successfully updated.";

                return RedirectToAction(nameof(Index));
            }
            else
            {
                _logger.LogWarning($"Dial plan compilation failed. {errMessage}");

                TempData["Error"] = errMessage;

                return View(nameof(Index), script);
            }
        }
    }
}
