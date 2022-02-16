using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;
using Website.Models.Users;
using MongoDB.Driver;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Website.Controllers
{
    /**
     * This controller is to be used to handle
     * login, logout, sign up and other users
     * related actions
     */
    public class UsersController : Controller
    {
        private readonly UsersService _users;
        private readonly QuestionsService _questions;
        public UsersController(UsersService users, QuestionsService questions)
        {
            this._users = users;
            this._questions = questions;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Users";
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult LoginPage(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                ViewData["error"] = error;
            }
            return View();
        }

        // Registration page
        public IActionResult SignUpPage(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                ViewData["error"] = error;
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignUpAsync(string email, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                return RedirectToAction(nameof(SignUpPage), "Users", new { Error = "Credenziali invalide" });
            }

            var newUser = new User() { Email = email, Username = username, Password = password, Registered = DateTime.UtcNow };

#warning ONLY FOR TESTING PURPOSES
            if (username == "X")
            {
                newUser.IsAdmin = true;
            }

            try
            {
                await this._users.AddNewUser(newUser);
            }
            catch (MongoWriteException ME)
            {
                WriteError WE = ME.WriteError;
                if (WE is not null)
                {
                    switch (WE.Category)
                    {
#warning TODO: is email or username already used?
                        case ServerErrorCategory.DuplicateKey:
                            return RedirectToAction(nameof(SignUpPage), "Users", new { Error = "Credenziali già in uso, sceglierne di differenti!" });
                    }
                }
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            return RedirectToAction(nameof(LoginPage), "Users");
        }

        private static bool CheckPassword(User user, string pwd)
        {
            // it will be necessary to use a pwd hash or sth similar
            // USeful reference:
            //  https://docs.microsoft.com/en-us/troubleshoot/dotnet/csharp/compute-hash-values
            return user.Password == pwd;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string returnUrl)
        {
            // check arguments
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return BadRequest();
            }
            var user = await this._users.GetUserByEmail(email);
            // it will be necessary to check also user pwd
            if (user is null || !CheckPassword(user, password))
            {
                // unauthorised
                return RedirectToAction(nameof(LoginPage), "Users", new { Error = "Credenziali invalide" });
            }
            // Is the user banned?
            if (user.IsCurrentlyBanned())
            {
                var ban = user.GetCurrentBan();
                string error;
                if (ban.BanEnd is null)
                {
                    error = "You have been permanently banned, you will not be able to login anymore.";
                }
                else
                {
                    error = "You have been temporarily banned, you will not be able to login untill " + ban.BanEnd?.ToLocalTime() + ".";
                }
                    
                return RedirectToAction(nameof(LoginPage), "Users", new { Error = error });
            }
            // OK! User authenticated!

            // Per fare login con i claim:
            //  https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-5.0#create-an-authentication-cookie
            var claims = new List<Claim> {
                // Available ClaimTypes:
                //  MS Doc  https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes?view=net-6.0
                new Claim(ClaimTypes.SerialNumber, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
            };

            if (user.IsAdmin)
            {
                // How to give roles to users:
                //  https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-5.0#create-an-authentication-cookie
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            // Doc ClaimsIdentity:
            //  https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity?view=net-5.0
            var claimsIdentity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // Doc ClaimsPrincipal:
            //  https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal?view=net-5.0
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            // https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.authenticationhttpcontextextensions.signinasync?view=aspnetcore-5.0
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal);

            // to main page or to previous page
            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> LogoutAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        /**
         * Show current user page with some info about itsel and his questions
         */
        public async Task<IActionResult> Me()
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            // User object
            var user = await this._users.GetUserById(userId);
            // How many questions has the user asked?
            var askedQuestionsCount = await this._questions.CountAskedQuestions(userId);
            // Get the questions the user is interested in for updates
            var interestingQuestion = await this._questions.GetInterestingQuestions(userId);
            
            ViewData["user"] = user;
            ViewData["askedQuestionsCount"] = askedQuestionsCount;
            ViewData["interestingQuestion"] = interestingQuestion;
            return View();
        }

        public async Task<IActionResult> MyQuestions(int page = 0, int resultsPerPage = 20)
        {
            int limit = (resultsPerPage <= 0) ? 20 : (resultsPerPage > 50) ? 50 : resultsPerPage;
            // lower page is 0
            int skip = (page < 0) ? 0 : page*limit;

            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            // How many questions has the user asked?
            var askedQuestionsCount = await this._questions.CountAskedQuestions(userId);
            // Get the hasked questions
            var askedQuestions = await this._questions.GetAskedQuestions(userId, limit, skip);
            // no more results in this page
            if (!askedQuestions.Any() && page > 0)
            {
                // page 0 is chosen by default
                return RedirectToAction(nameof(MyQuestions), new { resultsPerPage = limit });
            }

            ViewData["askedQuestionsCount"] = askedQuestionsCount;
            ViewData["askedQuestions"] = askedQuestions;
            ViewData["resultsPerPage"] = resultsPerPage;
            ViewData["page"] = page;

            return View();
        }

        [Authorize]
        /**
         * Show current user page with some info about itsel and his questions
         */
        public async Task<IActionResult> ContributedQuestions()
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            var questions = await this._questions.GetQuestionsUserContributedTo(userId);
            ViewData["questions"] = questions;
            return View();
        }

        /**
         * Show statistics about the current user
         */
        [Authorize]
        public async Task<IActionResult> MyStatistics()
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            // User object
            var user = await this._users.GetUserById(userId);
            // How many questions has the user asked?
            var askedQuestionsCount = await this._questions.CountAskedQuestions(userId);
            // Get stats stats about the votes received by the user
            var userVoteStats = await this._users.CountVotesReceivedByUser(userId);

            ViewData["user"] = user;
            ViewData["askedQuestionsCount"] = askedQuestionsCount;
            ViewData["userVoteStats"] = userVoteStats;
            return View();
        }
    }
}
