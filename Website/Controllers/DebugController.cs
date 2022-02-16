using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;
using Website.Models.Users;
using Website.Models.Discussions;

namespace Website.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class DebugController : ControllerBase
    {
        private readonly TagsService tagsService;
        private readonly UsersService usersService;
        private readonly QuestionsService questionsService;
        public DebugController(TagsService tagsService, UsersService usersService, QuestionsService questionsService)
        {
            this.tagsService = tagsService;
            this.usersService = usersService;
            this.questionsService = questionsService;
        }

        [HttpGet]
        public ActionResult<string> Hello()
        {
            return "Hello World";
        }

        [HttpGet]
        public async Task<IEnumerable<string>> GetAllUsers()
        {
            var users = await this.usersService.GetAllUserIds();
            return users;
        }

        [HttpGet]
        public async Task<IEnumerable<string>> GetAllTags()
        {
            var tags = await this.tagsService.GetAllTagIds();
            return tags;
        }

        [HttpPost]
        public async Task<ActionResult<User>> CreateUser(User user)
        {
            await this.usersService.AddNewUser(user);
            return user;
        }

        [HttpPost]
        public async Task<ActionResult<Question>> AddQuestion(Question question)
        {
            await this.questionsService.AddQuestion(question);
            return question;
        }

        [HttpPost]
        public async Task<ActionResult<string>> AddAnswer(Answer answer)
        {
            string questionId = answer.QuestionId;
            if (string.IsNullOrWhiteSpace(questionId) || answer is null)
            {
                return BadRequest();
            }
            var answerId = await this.questionsService.AddAnswer(questionId, answer);
            if (string.IsNullOrWhiteSpace(answerId))
            {
                return BadRequest();
            }
            return answerId;
        }

        [HttpPost]
        public async Task<ActionResult<string>> CommentQuestion(Comment comment)
        {
            string questionId = comment.QuestionId;
            if (string.IsNullOrWhiteSpace(questionId) || comment is null)
            {
                return BadRequest();
            }
            var commentId = await this.questionsService.CommentQuestion(questionId, comment);
            if (string.IsNullOrWhiteSpace(commentId))
            {
                return BadRequest();
            }
            return commentId;
        }

        [HttpPost]
        public async Task<ActionResult<string>> CommentAnswer(Comment comment)
        {
            string questionId = comment.QuestionId;
            string answerId = comment.AnswerId;
            if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(answerId) || comment is null)
            {
                return BadRequest();
            }
            var commentId = await this.questionsService.CommentAnswer(questionId, answerId, comment);
            if (string.IsNullOrWhiteSpace(commentId))
            {
                return BadRequest();
            }
            return commentId;
        }

        [HttpPost]
        public async Task VoteAnswer(string questionId, string answerId, bool useful, string userId, string userName)
        {
            await this.questionsService.VoteAnswer(questionId, answerId, new Vote {
                AuthorId = userId,
                AuthorName = userName,
                IsUseful = useful,
            });
        }

        [HttpPost]
        public async Task VoteCommentToQuestion(string questionId, string commentId, bool useful, string userId, string userName)
        {
            await this.questionsService.VoteCommentToQuestion(questionId, commentId, new Vote
            {
                AuthorId = userId,
                AuthorName = userName,
                IsUseful = useful,
            });
        }

        [HttpPost]
        public async Task VoteCommentToAnswer(string questionId, string answerId, string commentId, bool useful, string userId, string userName)
        {
            await this.questionsService.VoteCommentToAnswer(questionId, answerId, commentId, new Vote
            {
                AuthorId = userId,
                AuthorName = userName,
                IsUseful = useful,
            });
        }

        [HttpPost]
        public async Task<IActionResult> FollowTag(string user, string tag)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(tag))
            {
                return BadRequest();
            }
            await this.tagsService.FollowTag(user, tag);
            return Accepted();
        }

        [HttpPost]
        public async Task<IActionResult> UnfollowTag(string user, string tag)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(tag))
            {
                return BadRequest();
            }
            await this.tagsService.UnfollowTag(user, tag);
            return Accepted();
        }

        public async Task<ActionResult<User>> GetUser(string name)
        {
            var user = await this.usersService.GetUserByName(name);
            return user == null ? NotFound() : user;
        }

        public async Task<ActionResult<Question>> GetQuestion(string questionId)
        {
            var question = await this.questionsService.Get(questionId);
            return question;
        }
    }
}
