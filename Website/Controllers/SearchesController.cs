using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;

namespace Website.Controllers
{
    /**
     * This controller handle a search section
     * in the website.
     *
     * Question will be surely searchable and, maybe
     * in the future, users will be
     *
     * Question could be searched by tags, question
     * title and question description.
     *
     * Maybe only one view will be used. Data will be
     * shown only if available.
     */
    public class SearchesController : Controller
    {
        private readonly QuestionsService _questions;
        public SearchesController(QuestionsService questions)
        {
            this._questions = questions;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Search";
        }

        /**
         * Simply show a page where inserting search
         * parameters
         */
        [HttpGet]
        public async Task<IActionResult> Index(string keywords, string tags, int page = 0, int resultsPerPage = 20)
        {
            if (resultsPerPage <= 0)
            {
                resultsPerPage = 20;
            }
            else if (resultsPerPage > 50)
            {
                // no more than 50 results per page
                resultsPerPage = 50;
            }
            ViewData[nameof(page)] = page;
            ViewData[nameof(resultsPerPage)] = resultsPerPage;
            if (string.IsNullOrWhiteSpace(keywords))
            {
                return View(nameof(Index));
            }
            IEnumerable<string> taglist = string.IsNullOrWhiteSpace(tags) ? null : tags.Split(",").Select(s => s.Trim().ToUpper());
            var questions = await this._questions.SearchQuestions(keywords, taglist, page*resultsPerPage, resultsPerPage);
            if (!questions.Any() && page != 0)
            {
                return RedirectToAction(nameof(Index), new
                {
                    keywords,
                    tags,
                    page = 0,
                    resultsPerPage,
                });
            }
            ViewData["keywords"] = keywords;
            ViewData["tags"] = tags;
            ViewData["taglist"] = taglist;
            ViewData["questions"] = questions;
            return View(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Query(string complexQuery)
        {
            if (string.IsNullOrWhiteSpace(complexQuery))
            {
                return View(nameof(Index));
            }
            var tmp = complexQuery.Split(" ").Select(s => s.Trim());
            var tags = string.Join(",", tmp.Where(s => s.StartsWith("#")).Select(s => s.Substring(1).ToUpper()));
            string keywords = string.Join(" ", tmp.Where(s => !s.StartsWith("#")));
            if (string.IsNullOrWhiteSpace(keywords) && !string.IsNullOrWhiteSpace(tags))
            {
                return RedirectToAction(nameof(TagsController.GetQuestionsByTags), "Tags", new {
                    Tags = tags,
                });
            }
            return await Index(keywords, tags);
        }
    }
}
