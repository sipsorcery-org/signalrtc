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

        //public IActionResult Error()
        //{
        //    return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        //}

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

            //var user = await github.User.Current();

            //_logger.LogDebug($"Current user is {user.Login}.");

            //var sIPAssetsDbContext = _context.SIPAccounts.Include(s => s.Domain).Include(s => s.SIPDialPlan);
            //return View(await sIPAssetsDbContext.ToListAsync());
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

                //Session["OAuthToken"] = token.AccessToken;

                return RedirectToAction("List");
                //}
            }
        }

        [Authorize]
        public async Task<IActionResult> List()
        {
            ViewData["SIPDefaultDomain"] = _sipDefaultDomain;
            var sipAccount = await _sipAccountDataLayer.GetSIPAccountWithBindings(User.Identity.Name, _sipDefaultDomain);
            return View(sipAccount);
        }

        [Authorize]
        public async Task<IActionResult> Create()
        {
            // Check if the SIP account already exists for this user.
            if (await _sipAccountDataLayer.Exists(User.Identity.Name))
            {
                TempData["Error"] = $"SIP account with username {User.Identity.Name} already exists.";
                return RedirectToAction(nameof(List));
            }
            else
            {
                _logger.LogInformation($"Attempting to create new SIP account for {User.Identity.Name}@{_sipDefaultDomain}.");
                await _sipAccountDataLayer.Create(User.Identity.Name, _sipDefaultDomain, "password");
                TempData["Success"] = $"SIP account successfully created for username {User.Identity.Name}@{_sipDefaultDomain}.";
                return RedirectToAction(nameof(List));
            }
        }

        // POST: SIPAccounts/Delete/5
        //[HttpPost, ActionName("Delete")]
        //[ValidateAntiForgeryToken]
        //[Authorize]
        //public async Task<IActionResult> Delete(Guid id)
        //{
        //    var sIPAccount = await _context.SIPAccounts.FindAsync(id);
        //    _context.SIPAccounts.Remove(sIPAccount);
        //    await _context.SaveChangesAsync();
        //    return RedirectToAction(nameof(List));
        //}
    }
}
