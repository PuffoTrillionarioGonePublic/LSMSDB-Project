using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;
using Website.Models.Statistics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Website.Controllers
{
    public class StatisticsController : Controller
    {
        private readonly QuestionsService questionsService;
        private readonly UsersService usersService;
        private readonly TagsService tagsService;
        public StatisticsController(TagsService tagsService, UsersService usersService, QuestionsService questionsService)
        {
            this.tagsService = tagsService;
            this.usersService = usersService;
            this.questionsService = questionsService;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Statistics";
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> TagStatistics(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return View();
            }

            var taglist = tags.Split(",").Select(s => s.Trim().ToUpper());
            if (taglist.Any())
            {
                var tagStatics = await this.tagsService.GetTagStatistics(taglist);

                ViewData[nameof(taglist)] = taglist;
                ViewData[nameof(tagStatics)] = tagStatics;
            }
            return View();
        }

        public async Task<IActionResult> QuestionStatistics()
        {
            var stats = await this.questionsService.GetQuestionStats();
            ViewData[nameof(stats)] = stats;
            return View();
        }

        public async Task<IActionResult> UserSignInStatistics()
        {
            var stats = await this.usersService.GetSignInStats();
            ViewData[nameof(stats)] = stats;
            return View();
        }

        public async Task<IActionResult> UserTagStatistics()
        {
            var stats = await this.tagsService.FollowedTagsStats();
            ViewData[nameof(stats)] = stats;
            return View();
        }
    }
}
