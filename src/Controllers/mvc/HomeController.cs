using System;
using System.Collections.Generic;
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
using devcall;
using devcall.DataAccess;

namespace demo.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _sipDefaultDomain;

        private readonly SIPAccountDataLayer _sipAccountDataLayer;
        private readonly SIPRegistrarBindingDataLayer _sipRegBindingsDataLayer;
        private readonly IConfiguration _config;
        private readonly ILogger<HomeController> _logger;

        private readonly string _githubAppName;
        private readonly string _githubClientID;
        private readonly string _githubClientSecret;

        public HomeController(
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory,
            IConfiguration config,
            ILogger<HomeController> logger)
        {
            _config = config;
            _logger = logger;
            _sipAccountDataLayer = new SIPAccountDataLayer(dbContextFactory);
            _sipRegBindingsDataLayer = new SIPRegistrarBindingDataLayer(dbContextFactory);

            _sipDefaultDomain = config[ConfigKeys.SIP_DOMAIN];
            _githubAppName = config[ConfigKeys.GITHUB_OAUTH_APPNAME];
            _githubClientID = config[ConfigKeys.GITHUB_OAUTH_CLIENTID];
            _githubClientSecret = config[ConfigKeys.GITHUB_OAUTH_CLIENTSECRET];
        }

        public IActionResult Index()
        {
            if (HttpContext.User?.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("List");
            }
            else
            {
                return View();
            }
        }

        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        public IActionResult Login()
        {
            //string csrf = Membership.GeneratePassword(24, 1);
            //Session["CSRF:State"] = csrf;

            string callbackUrl = this.Url.ActionLink("Authorize", "Home");
            _logger.LogDebug($"GitHub OAuth callback URL: {callbackUrl}.");

            var client = new GitHubClient(new ProductHeaderValue(_githubAppName));
            var request = new OauthLoginRequest(_githubClientID)
            {
                RedirectUri = new Uri(callbackUrl)
            };

            var oauthLoginUrl = client.Oauth.GetGitHubLoginUrl(request);

            _logger.LogDebug($"Redirecting to {oauthLoginUrl}.");

            return Redirect(oauthLoginUrl.ToString());
        }

        public async Task<ActionResult> Authorize(string code, string state)
        {
            if (String.IsNullOrEmpty(code))
            {
                return RedirectToAction("Login");
            }
            else
            {
                //var expectedState = HttpContext.Session.GetString("CSRF:State");
                //if (state != expectedState)
                //{
                //    _logger.LogWarning($"Authentication failure, callback state did not match exepcted value.");

                //    return RedirectToAction("Login");
                //}
                //else
                //{
                //    HttpContext.Session.SetString("CSRF:State", null);

                var client = new GitHubClient(new ProductHeaderValue(_githubAppName));
                var request = new OauthTokenRequest(_githubClientID, _githubClientSecret, code);
                var token = await client.Oauth.CreateAccessToken(request);

                client.Credentials = new Credentials(token.AccessToken);

                var user = await client.User.Current();

                _logger.LogDebug($"GitHub authenticated user ID: {user.Id}.");

                var claims = new List<Claim>
                {
                    new Claim("user", user.Id.ToString()),
                    new Claim("role", "Member")
                };

                await HttpContext.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", "user", "role")));

                return RedirectToAction("List");
            }
        }

        [Authorize]
        public async Task<IActionResult> List()
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
                return RedirectToAction(nameof(List));
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
                var newAccount = await _sipAccountDataLayer.Create(User.Identity.Name, _sipDefaultDomain, "password");
                TempData["Success"] = $"SIP account successfully created for username {newAccount.AOR}.";
                return RedirectToAction(nameof(List));
            }
            else
            {
                var presetSIPAccount = new SIPAccount
                {
                    Domain = new SIPDomain { Domain = _sipDefaultDomain },
                    SIPUsername = User.Identity.Name,
                    SIPPassword = sipAccount.SIPPassword
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
                await _sipAccountDataLayer.UpdatePassword(User.Identity.Name, _sipDefaultDomain, sipAccount.SIPPassword);
                return RedirectToAction(nameof(List));
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
            return RedirectToAction(nameof(List));
        }
    }
}
