using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using Website.Services;
using Website.Models.Discussions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Website.Controllers
{
    public class QuestionsController : Controller
    {
        private readonly QuestionsService _questions;

        public QuestionsController(QuestionsService service)
        {
            this._questions = service;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["Title"] = "Questions";
        }

        public async Task<IActionResult> Index(int page = 0, int take = 20)
        {
            // To easily display results per page
            int skip = page * take;
            // limit the number of items to retrieve to 100
            if (take > 100)
            {
                take = 100;
            }
            else if (take <= 0)
            {
                take = 20;
            }
            var questions = await this._questions.GetQuestions(skip, take);
            if (!questions.Any() && skip != 0)
            {
                // No results in this page
                return RedirectToAction(nameof(Index), new { Take = take });
            }
            ViewData["questions"] = questions;
            ViewData["page"] = page;
            ViewData["take"] = take;
            return View();
        }

        public async Task<IActionResult> Read(string id)
        {
            var question = await this._questions.Get(id);
            if (question is null)
            {
                return NotFound();
            }
            // Is user is authenticated remove pending update
            // if the current user is interested in this question
            if (User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirst(ClaimTypes.SerialNumber).Value;
                // Was the user interested in this question?
                if (question.InterestedUsers is not null && question.InterestedUsers.ContainsKey(userId))
                {
                    await this._questions.ConsumeNotification(userId, id);
                }
            }
            ViewData["question"] = question;
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(string question, string response)
        {
            Answer answer = new() {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                Text = response.Trim(),
            };
            await this._questions.AddAnswer(question, answer);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [Authorize]
        public IActionResult NewQuestion(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                ViewData["error"] = error;
            }
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubscribeQuestion(string question)
        {
            var iu = new InterestedUser() {
                UserId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                UserName = User.FindFirst(ClaimTypes.Name).Value,
            };
            await this._questions.SubscribeForQuestionUpdates(question, iu);
            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnsubscribeQuestion(string question)
        {
            await this._questions.UnsubscribeForQuestionUpdates(question, User.FindFirst(ClaimTypes.SerialNumber).Value);
            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestion(
            [Bind("Title,Text")] Question question, string tags)
        {
            if (string.IsNullOrWhiteSpace(question.Title) || string.IsNullOrWhiteSpace(question.Text))
            {
                return RedirectToAction(nameof(NewQuestion), "Questions", new { Error = "Title and question cannot be empty!" });
            }
            question.AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            question.AuthorName = User.FindFirst(ClaimTypes.Name).Value;
            if (tags is not null)
            {
                var tg = new SortedSet<String>();
                foreach (var tag in tags.Split(",").AsEnumerable().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpper()))
                {
                    tg.Add(tag);
                }
                question.Tags = tg;
            }
            // enforce 1-5 tags
            if (tags is null || !(question.Tags.Any() && question.Tags.Count() <= 5))
            {
                return RedirectToAction(nameof(NewQuestion), "Questions", new { Error = "From 1 to 5 Tags required" });
            }
            await this._questions.AddQuestion(question);
            return RedirectToAction(nameof(Index), "Questions");
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommentQuestion(string question, string comment)
        {
            Comment comm = new()
            {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                Text = comment.Trim(),
            };
            await this._questions.CommentQuestion(question, comm);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommentAnswer(string question, string answer, string comment)
        {
            Comment comm = new()
            {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                Text = comment.Trim(),
            };
            await this._questions.CommentAnswer(question, answer, comm);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoteAnswer(string question, string answer, bool useful)
        {
            Vote comm = new()
            {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                IsUseful = useful,
            };
            await this._questions.VoteAnswer(question, answer, comm);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAnswerVote(string question, string answer)
        {
            var UserId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveAnswerVote(question, answer, UserId);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoteCommentToQuestion(string question, string comment, bool useful)
        {
            Vote comm = new()
            {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                IsUseful = useful,
            };
            await this._questions.VoteCommentToQuestion(question, comment, comm);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveVoteToCommentToQuestion(string question, string comment)
        {
            var UserId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveVoteToCommentToQuestion(question, comment, UserId);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoteCommentToAnswer(string question, string answer, string comment, bool useful)
        {
            Vote comm = new()
            {
                AuthorId = User.FindFirst(ClaimTypes.SerialNumber).Value,
                AuthorName = User.FindFirst(ClaimTypes.Name).Value,
                IsUseful = useful,
            };
            await this._questions.VoteCommentToAnswer(question, answer, comment, comm);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveVoteToCommentToAnswer(string question, string answer, string comment)
        {
            var UserId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveVoteToCommentToAnswer(question, answer, comment, UserId);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetQuestionSolvedStatus(string question, bool solved)
        {
            await this._questions.SetSolvedStatus(question, solved);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAnswerAsSolution(string question, string answer)
        {
            var UserId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.MarkAnswerAsSolution(question, answer, UserId);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnmarkAnswerAsSolution(string question, string answer)
        {
            var UserId = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.UnmarkAnswerAsSolution(question, answer, UserId);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        /**
         * The following methods are for admins only and are
         * used to mark Q&A&C removed by admins.
         */
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveQuestion(string question, string reason)
        {
            string moderator = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveQuestion(question, moderator, reason);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAnswer(string question, string answer, string reason)
        {
            string moderator = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveAnswer(question, answer, moderator, reason);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCommentToQuestion(string question, string comment, string reason)
        {
            string moderator = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveCommentToQuestion(question, comment, moderator, reason);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCommentToAnswer(string question, string answer, string comment, string reason)
        {
            string moderator = User.FindFirst(ClaimTypes.SerialNumber).Value;
            await this._questions.RemoveCommentToAnswer(question, answer, comment, moderator, reason);

            return RedirectToAction(nameof(Read), "Questions", new { Id = question });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ModerateQuestion(string questionId, string answerId, string commentId)
        {
            var question = await this._questions.Get(questionId);
            if (question is null)
            {
                return NotFound();
            }

            ViewData[nameof(question)] = question;
            ViewData[nameof(questionId)] = questionId;
            ViewData[nameof(answerId)] = answerId;
            ViewData[nameof(commentId)] = commentId;

            return View();
        }
    }
}
