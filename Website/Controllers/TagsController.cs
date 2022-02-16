using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Website.Services;


namespace Website.Controllers
{
    /**
     * This controller is used to handle a few group of pages listing
     * used tags and (a subset of) question using them
     */
    public class TagsController : Controller
    {
        private readonly QuestionsService _questions;
        private readonly TagsService _tags;
        private readonly UsersService usersService;
        public TagsController(QuestionsService questions, TagsService tags, UsersService usersService)
        {
            this._questions = questions;
            this.usersService = usersService;
            this._tags = tags;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Tags";
        }

        /**
         * Show a list of available tags
         */
        public async Task<IActionResult> Index(string tagSearch, int page = 0, int resultsPerPage = 72)
        {
            tagSearch = string.IsNullOrWhiteSpace(tagSearch) ? null : tagSearch.Trim().ToUpper();
            page = (page < 0) ? 0 : page;
            resultsPerPage = (resultsPerPage < 6) ? 6 : (resultsPerPage > 144) ? 144 : resultsPerPage;
            // IDictionary<string, int>
            var tags = await this._questions.GetTags(tagSearch, resultsPerPage, page*resultsPerPage);

            if (!tags.Any() && page > 0)
            {
                return RedirectToAction(nameof(Index));
            }

            ViewData["tags"] = tags;
            ViewData[nameof(tagSearch)] = tagSearch;
            ViewData[nameof(page)] = page;
            ViewData[nameof(resultsPerPage)] = resultsPerPage;

            return View();
        }

        public async Task<IActionResult> Tag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return BadRequest();
            }
            tag = tag.Trim().ToUpper();
            var tagObj = await this._tags.GetTag(tag);
            if (tagObj is null)
            {
                return NotFound();
            }
            var questions = await this._questions.GetQuestionsByTag(tag);
            if (User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
                var user = await this.usersService.GetUserById(userId);
                ViewData["user"] = user;
            }
            ViewData["questions"] = questions;
            ViewData["tag"] = tagObj;
            return View();
        }

        /**
         * Add current user to tag followers
         */
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FollowTag(string tag)
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._tags.FollowTag(userId, tag);
            return RedirectToAction(nameof(Tag), "Tags", new { Tag = tag });
        }

        /**
         * Remove current user from tag followers
         */
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnfollowTag(string tag)
        {
            var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._tags.UnfollowTag(userId, tag);
            return RedirectToAction(nameof(Tag), "Tags", new { Tag = tag });
        }


        public async Task<IActionResult> GetQuestionsByTags(string tags)
        {
            var taglist = tags?.Split(",").Select(s => s.Trim().ToUpper());
            if (taglist is null)
            {
                return BadRequest();
            }
            var questions = await this._questions.GetQuestionsByTags(taglist);
            ViewData["taglist"] = taglist;
            ViewData["questions"] = questions;
            return View();
        }
    }
}
