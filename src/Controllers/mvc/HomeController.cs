//-----------------------------------------------------------------------------
// Filename: HomeController.cs
//
// Description: User controller to login and manage SIP account.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using signalrtc.DataAccess;
using SIPSorcery.SIP;

namespace signalrtc.Controllers
{
    [RequireHttps]
    public class HomeController : Controller
    {
        private const string SESSION_CSRF_KEY = "CSRF:State";

        private readonly string _sipDefaultDomain;

        private readonly SIPAccountDataLayer _sipAccountDataLayer;
        private readonly SIPRegistrarBindingDataLayer _sipRegBindingsDataLayer;
        private readonly SIPDialPlanManager _sipDialPlanManager;
        private readonly IConfiguration _config;
        private readonly ILogger<HomeController> _logger;

        private readonly string _githubAppName;
        private readonly string _githubClientID;
        private readonly string _githubClientSecret;
        private readonly string[] _adminUsers;

        public HomeController(
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            IConfiguration config,
            ILogger<HomeController> logger,
            SIPDialPlanManager sipDialPlanManager)
        {
            _config = config;
            _logger = logger;
            _sipAccountDataLayer = new SIPAccountDataLayer(dbContextFactory);
            _sipRegBindingsDataLayer = new SIPRegistrarBindingDataLayer(dbContextFactory);
            _sipDialPlanManager = sipDialPlanManager;

            _sipDefaultDomain = config[ConfigKeys.SIP_DOMAIN];
            _githubAppName = config[ConfigKeys.GITHUB_OAUTH_APPNAME];
            _githubClientID = config[ConfigKeys.GITHUB_OAUTH_CLIENTID];
            _githubClientSecret = config[ConfigKeys.GITHUB_OAUTH_CLIENTSECRET];

            _adminUsers = _config.GetSection(ConfigKeys.ADMIN_USERNAMES).Get<string[]>();
        }

        public async Task<IActionResult> Index()
        {
            //if (HttpContext.User?.Identity?.IsAuthenticated == true)
            //{
            //    return RedirectToAction(nameof(Account));
            //}
            //else
            //{
            //    return View();
            //}

            var dialplan = await _sipDialPlanManager.LoadDialPlan();

            return View(nameof(Index), dialplan.DialPlanScript);
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Login()
        {
            // Protect the authorize callback against CSRF.
            string csrf = Guid.NewGuid().ToString();
            HttpContext.Session.SetString(SESSION_CSRF_KEY, csrf);

            string callbackUrl = this.Url.ActionLink("Authorize", "Home");
            _logger.LogDebug($"GitHub OAuth callback URL: {callbackUrl}.");

            var client = new GitHubClient(new ProductHeaderValue(_githubAppName));
            var request = new OauthLoginRequest(_githubClientID)
            {
                RedirectUri = new Uri(callbackUrl),
                State = csrf
            };

            var oauthLoginUrl = client.Oauth.GetGitHubLoginUrl(request);

            _logger.LogDebug($"Redirecting to {oauthLoginUrl}.");

            return Redirect(oauthLoginUrl.ToString());
        }

        public async Task<ActionResult> Authorize(string code, string state)
        {
            if (string.IsNullOrEmpty(code))
            {
                return RedirectToAction("Index");
            }
            else
            {
                var csrf = HttpContext.Session.GetString(SESSION_CSRF_KEY);
                if (state != csrf)
                {
                    _logger.LogWarning($"Authentication failure, callback state did not match session CSRF token.");
                    TempData["Error"] = "Authentication failure due to incorrect CSRF token.";
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    HttpContext.Session.Remove(SESSION_CSRF_KEY);

                    var client = new GitHubClient(new ProductHeaderValue(_githubAppName));
                    var request = new OauthTokenRequest(_githubClientID, _githubClientSecret, code);
                    var token = await client.Oauth.CreateAccessToken(request);

                    client.Credentials = new Credentials(token.AccessToken);

                    var user = await client.User.Current();

                    _logger.LogDebug($"GitHub authenticated user ID: {user.Id}.");

                    var claims = new List<Claim>
                    {
                        new Claim("user", user.Id.ToString()),
                        new Claim("role", "Member"),
                    };

                    if(_adminUsers?.Contains(user.Id.ToString()) == true)
                    {
                        _logger.LogDebug($"GitHub user {user.Id} set as admin.");
                        claims.Add(new Claim("role", "Admin"));
                    }

                    await HttpContext.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", "user", "role")));

                    return RedirectToAction(nameof(Account));
                }
            }
        }

        [Authorize]
        public async Task<IActionResult> Account()
        {
            ViewData["SIPDefaultDomain"] = _sipDefaultDomain;
            var sipAccount = await _sipAccountDataLayer.GetSIPAccount(User.Identity.Name, _sipDefaultDomain, true);
            return View(sipAccount);
        }

        [Authorize]
        public async Task<IActionResult> Create()
        {
            // Check if the SIP account already exists for this user.
            if (await _sipAccountDataLayer.Exists(User.Identity.Name))
            {
                TempData["Error"] = $"SIP account sip:{User.Identity.Name}@{_sipDefaultDomain} already exists.";
                return RedirectToAction(nameof(Account));
            }
            else
            {
                var sipAccount = new SIPAccount
                {
                    Domain = new SIPDomain { Domain = _sipDefaultDomain },
                    SIPUsername = User.Identity.Name
                };

                return View(sipAccount);
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("SIPPassword")] SIPAccount sipAccount)
        {
            if (ModelState.IsValid)
            {
                _logger.LogInformation($"Attempting to create new SIP account for {User.Identity.Name}@{_sipDefaultDomain}.");
                var passwordHash = HTTPDigest.DigestCalcHA1(User.Identity.Name, _sipDefaultDomain, sipAccount.SIPPassword);
                var newAccount = await _sipAccountDataLayer.Create(User.Identity.Name, _sipDefaultDomain, passwordHash);
                TempData["Success"] = $"SIP account successfully created for username {newAccount.AOR}.";
                return RedirectToAction(nameof(Account));
            }
            else
            {
                var presetSIPAccount = new SIPAccount
                {
                    Domain = new SIPDomain { Domain = _sipDefaultDomain },
                    SIPUsername = User.Identity.Name
                };
                return View(sipAccount);
            }
        }

        [Authorize]
        public async Task<IActionResult> Edit()
        {
            var sipAccount = await _sipAccountDataLayer.GetSIPAccount(User.Identity.Name, _sipDefaultDomain);

            if (sipAccount == null)
            {
                return NotFound();
            }
            else
            {
                // Don't show existing password (even though it's already hashed).
                var presetSIPAccount = new SIPAccount
                {
                    Domain = new SIPDomain { Domain = _sipDefaultDomain },
                    SIPUsername = User.Identity.Name,
                };

                return View(presetSIPAccount);
            }
        }

        /// <summary>
        /// The only field that can currently be updated is the password.
        /// </summary>
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind("SIPPassword")] SIPAccount sipAccount)
        {
            if (ModelState.IsValid)
            {
                var passwordHash = HTTPDigest.DigestCalcHA1(User.Identity.Name, _sipDefaultDomain, sipAccount.SIPPassword);
                await _sipAccountDataLayer.UpdatePassword(User.Identity.Name, _sipDefaultDomain, passwordHash);
                return RedirectToAction(nameof(Account));
            }
            else
            {
                var presetSIPAccount = await _sipAccountDataLayer.GetSIPAccount(User.Identity.Name, _sipDefaultDomain);
                presetSIPAccount.SIPPassword = sipAccount.SIPPassword;
                return View(presetSIPAccount);
            }
        }

        [Authorize]
        public async Task<IActionResult> Delete()
        {
            await _sipAccountDataLayer.Delete(User.Identity.Name, _sipDefaultDomain);
            return RedirectToAction(nameof(Account));
        }
    }
}
