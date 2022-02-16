using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;
using Website.Models.Users;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Website.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UsersService users;
        private readonly SynchronizerService synchronizer;
        public AdminController(UsersService users, SynchronizerService synchronizer)
        {
            this.users = users;
            this.synchronizer = synchronizer;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Admin";
        }

        public IActionResult Index()
        {
            return View();
        }

        /**
         * Here admins
         */
        public async Task<IActionResult> SearchUser(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                var userlist = await this.users.FindUsersByName(query);
                ViewData[nameof(query)] = query;
                ViewData[nameof(userlist)] = userlist;
            }
            return View();
        }

        /**
         * Here admins can see some data
         * about a specific user and ban him
         */
        public async Task<IActionResult> GetUser(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest();
            }
            var user = await this.users.GetUserById(id);
            if (user is null)
            {
                return NotFound();
            }
            ViewData["user"] = user;
            return View();
        }

        /**
         * Show all the users the admin has banned
         */
        public async Task<IActionResult> Banned()
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            var user = await this.users.GetUserById(userId);
            ViewData["bannedUsers"] = user.BannedUsers;
            return View();
        }

        /**
         * This method allow an admin to ban a user for one hour.
         * This is only an example, in a real application the
         * admin should be able to choose how long to ban a user for
         */
        [HttpPost]
        public async Task<IActionResult> BanUser(string id, string reason)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest();
            }
            var adminId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            // Default: BAN FOR ONE HOUR
            var timespan = new TimeSpan(hours: 1, minutes: 0, seconds: 0);
            await this.users.BanUser(id, adminId, timespan, reason);
            return RedirectToAction(nameof(GetUser), new { Id = id });
        }

        public IActionResult Synchronize()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SynchronizeDB()
        {
            await this.synchronizer.ClearNeo4j();
            await Task.WhenAll(new Task[]
            {
                this.synchronizer.SynchronizeUsers(),
                this.synchronizer.SynchronizeQuestions(),
                this.synchronizer.SynchronizeTags(),
            });
            return View();
        }
    }
}
