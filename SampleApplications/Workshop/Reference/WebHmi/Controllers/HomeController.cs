using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebHmi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebHmi.Controllers
{
    public class HomeController : Controller
    {
        private const bool ShowWarning = false;

        private const string OAuthServer = "wptest.opcfoundation.org";
        //private const string OAuthServer = "newwebsite.opcfoundation.org";

        private const string AuthorizationCodeUrl = "https://" + OAuthServer + "/oauth/authorize/";
        private const string AccessTokenUrl = "https://" + OAuthServer + "/oauth/token/";
        private const string UserIdentityUrl = "https://" + OAuthServer + "/oauth/me/";
        private const string ClientId = "eG3ccss3l2rWdTslcPFuXekKsBtj0Zme8X7pWuUw";
        private const string ClientSecret = "mj3livDCckHXquRJHRiFwoXVXKXqQ90lq0OhZNr9";
        //private const string RedirectUri = "https://localhost:44324/Home/GetAccessToken/";
        private const string RedirectUri = "https://prototyping.opcfoundation.org/Home/GetAccessToken/";
        private const string UserNameKey = "UserName";
        private const string UserEmailKey = "UserEmail";
        private const string UserAccessToken = "AccessToken";
        private const string UserRefreshToken = "RefreshToken";
        private const string UserContextKey = "UserContext";
        
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            LookupUserIdentity();
            ViewData["ShowWarning"] = ShowWarning;
            return View();
        }

        public IActionResult Read()
        {
            LookupUserIdentity("Read");
            ViewData["ShowWarning"] = ShowWarning;
            return View();
        }


        public IActionResult Write()
        {
            LookupUserIdentity("Write");
            ViewData["ShowWarning"] = ShowWarning;
            return View();
        }

        public IActionResult Call()
        {
            LookupUserIdentity("Call");
            ViewData["ShowWarning"] = ShowWarning;
            return View();
        }

        public IActionResult Subscribe()
        {
            LookupUserIdentity("Subscribe");
            ViewData["ShowWarning"] = ShowWarning;
            return View();
        }

        public IActionResult Login()
        {
            SetUserIdentity(null, null, null, null);

            var url = AuthorizationCodeUrl;
            url += $"?client_id={ClientId}";
            url += $"&response_type=code";
            url += $"&redirect_uri={RedirectUri}";

            return Redirect(url);
        }

        public IActionResult Logout()
        {
            SetUserIdentity(null, null, null, null);

            byte[] utf8 = null;

            if (HttpContext.Session.TryGetValue(UserContextKey, out utf8))
            {
                return RedirectToAction(Encoding.UTF8.GetString(utf8));
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> GetAccessToken(string code, string state, string error, string error_description)
        {
            if (!String.IsNullOrEmpty(error))
            {
                return View("Error", new ErrorViewModel
                {
                    ErrorCode = $"Authorization Error: {error}",
                    ErrorMessage = error_description
                });
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters["grant_type"] = "authorization_code";
            parameters["code"] = code;
            parameters["client_id"] = ClientId;
            parameters["client_secret"] = ClientSecret;
            parameters["redirect_uri"] = RedirectUri;

            HttpClient client = new HttpClient();
            FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);
            client.DefaultRequestHeaders.Add("Authorization", $"Basic {ClientId}:{ClientSecret}");
            client.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0");

            var response = await client.PostAsync(AccessTokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return View("Error", new ErrorViewModel 
                { 
                    ErrorCode = $"HTTP Code: {(int)response.StatusCode}", 
                    ErrorMessage = response.ReasonPhrase 
                });
            }

            var json = response.Content.ReadAsStringAsync().Result;
            var body = JObject.Parse(json);

            string errorText = (string)body["error"];
            string accessToken = null;
            string refreshToken = null;

            if (errorText != null)
            {
                return View("Error", new ErrorViewModel
                {
                    ErrorCode = $"OAuth2 Error",
                    ErrorMessage = errorText
                });
            }
            else
            {
                accessToken = (string)body["access_token"];
                refreshToken = (string)body["refresh_token"];
            }

            HttpClient client2 = new HttpClient();
            client2.DefaultRequestHeaders.Add("Bearer", accessToken);
            client2.DefaultRequestHeaders.Add("cache-control", $"no-cache");
            client2.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0");

            var response2 = await client2.GetAsync($"{UserIdentityUrl}/?access_token={accessToken}");

            json = response2.Content.ReadAsStringAsync().Result;
            body = JObject.Parse(json);

            errorText = (string)body["error"];

            if (errorText != null)
            {
                return View("Error", new ErrorViewModel
                {
                    ErrorCode = $"OAuth2 Error",
                    ErrorMessage = errorText
                });
            }

            SetUserIdentity((string)body["user_email"], (string)body["display_name"], accessToken, refreshToken);

            byte[] utf8 = null;

            if (HttpContext.Session.TryGetValue(UserContextKey, out utf8))
            {
                return RedirectToAction(Encoding.UTF8.GetString(utf8));
            }

            return RedirectToAction("Index");
        }

        private void SetUserIdentity(string email, string displayName, string accessToken, string refreshToken)
        {
            if (email == null)
            {
                HttpContext.Session.Remove(UserEmailKey);
                HttpContext.Session.Remove(UserNameKey);
                HttpContext.Session.Remove(UserAccessToken);
                HttpContext.Session.Remove(UserRefreshToken);
            }
            else
            {
                HttpContext.Session.Set(UserEmailKey, Encoding.UTF8.GetBytes(email));

                if (String.IsNullOrEmpty(displayName))
                {
                    displayName = email;
                }

                HttpContext.Session.Set(UserNameKey, Encoding.UTF8.GetBytes(displayName));

                if (accessToken != null)
                {
                    HttpContext.Session.Set(UserAccessToken, Encoding.UTF8.GetBytes(accessToken));
                }

                if (refreshToken != null)
                {
                    HttpContext.Session.Set(UserRefreshToken, Encoding.UTF8.GetBytes(refreshToken));
                }
            }
        }

        private void LookupUserIdentity(string context = null)
        {
            byte[] utf8 = null;

            if (HttpContext.Session.TryGetValue(UserEmailKey, out utf8))
            {
                ViewData[UserEmailKey] = Encoding.UTF8.GetString(utf8);

                if (HttpContext.Session.TryGetValue(UserNameKey, out utf8))
                {
                    ViewData[UserNameKey] = Encoding.UTF8.GetString(utf8);
                }

                if (HttpContext.Session.TryGetValue(UserAccessToken, out utf8))
                {
                    ViewData[UserAccessToken] = Encoding.UTF8.GetString(utf8);
                }
            }

            if (context != null)
            {
                HttpContext.Session.Set(UserContextKey, Encoding.UTF8.GetBytes(context));
            }
            else
            {
                HttpContext.Session.Remove(UserContextKey);
            }
        }

        [HttpPost]
        public async Task<ActionResult> Invoke(string secret)
        {
            try
            {
                string input = await new StreamReader(Request.Body).ReadToEndAsync();

                StringContent content = new StringContent(input);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpClient client = new HttpClient();

                if (HttpContext.Request.Headers.ContainsKey("Authorization"))
                {
                    client.DefaultRequestHeaders.Add("Authorization", HttpContext.Request.Headers["Authorization"].ToArray<string>());
                }

                // var response = await client.PostAsync($"https://{System.Net.Dns.GetHostName()}:62540", content);
                var response = await client.PostAsync("https://prototyping.opcfoundation.org:62540", content);

                if (!response.IsSuccessStatusCode)
                {
                    return new StatusCodeResult((int)response.StatusCode);
                }

                var output = await response.Content.ReadAsStringAsync();
                return Content(output, "application/json");
            }
            catch (Exception e)
            {
                return Content($"{{\"ProxyError\":\"{e}\"}}", "application/json");
            }
        }
        
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            LookupUserIdentity();
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
