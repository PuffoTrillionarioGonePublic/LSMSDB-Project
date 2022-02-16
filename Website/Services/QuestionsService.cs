using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using Website.Models.Discussions;
using Website.Services;
using Website.Models.Users;
using Website.Models.Statistics;
using System.Threading;

namespace Website.Services
{
    public class QuestionsService : MongoSessionBase
    {
        private readonly IMongoCollection<Question> _questions;
        private readonly IMongoCollection<User> _users;
        private readonly IMongoCollection<Models.Discussions.Tag> _tags;
        private readonly Neo4jService neo4j;
        private readonly UsersService usersService;

        public QuestionsService(MongoService mongoService, Neo4jService neo4jService, UsersService usersService)
        : base(mongoService)
        {
            this._questions = mongoService.QuestionsCollection;
            this._users = mongoService.UsersCollection;
            this._tags = mongoService.TagsCollection;
            this.neo4j = neo4jService;
            this.usersService = usersService;
        }

        public async Task<IEnumerable<Question>> GetQuestions(int skip = 0, int take = 0)
        {
            // Removed questions which should not be shown
            var list = await this._questions.Find(q => q.Removed == null)
                .Sort(new SortDefinitionBuilder<Question>().Descending(q => q.Created))
                .Project(new ProjectionDefinitionBuilder<Question>().Exclude(q => q.Comments).Exclude(q => q.Answers).Exclude(q => q.InterestedUsers))
                .As<Question>()
                .Skip(skip > 0 ? skip : null)
                .Limit(take > 0 ? take : null)
                .ToListAsync();
            list.ForEach(q => q.AfterDeserialisation());
            return list;
        }

        public async Task<IEnumerable<Question>> GetAllQuestions()
        {
            // Removed questions which should not be shown
            var list = await this._questions.Find(q => q.Removed == null).ToListAsync();
            list.ForEach(q => q.AfterDeserialisation());
            return list;
        }

        /**
         * Get all the questions a user contributed to but
         * which where not asked by him
         */
        public async Task<IEnumerable<Question>> GetQuestionsUserContributedTo(string userId)
        {
            var questions = await this.neo4j.FindDiscussionsUserContributedTo(userId);
            return questions;
        }

        /**
         * How many questions has the user asked?
         */
        public async Task<long> CountAskedQuestions(string userId) =>
            await this.neo4j.CountAskedQuestions(userId);

        /**
         * Get questions asked by the user asked?
         */
        public async Task<IEnumerable<Question>> GetAskedQuestions(string userId, int? limit = null, int? skip = null)
        {
            var list = await this.neo4j.GetAskedQuestions(userId, limit, skip);
            return list;
        }

        public async Task<Question> Get(string id)
        {
            var q = await this._questions.Find(q => q.Id == id).FirstOrDefaultAsync();
            q?.AfterDeserialisation();
            return q;
        }

        public async Task AddQuestion(Question question)
        {
            if (question.Created == new DateTime())
            {
                question.Created = DateTime.UtcNow;
            }
            // Just in case...
            question.Tags = question.Tags.Select(t => t.Trim().ToUpper());
            question.BeforeSerialisation();

            var update = new QuestionUpdates
            {
                QuestionId = question.Id,
                Title = question.Title,
                Created = question.Created,
                AuthorId = question.AuthorId,
                AuthorName = question.Users[question.AuthorId],
                Tags = question.Tags,
                CountUpdates = null,
                Solved = null,
            };
            var session = await this.GetSessionHandle();
            var createdTags = new List<string>();

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) => {
                UpdateDefinition<User> udbu; UpdateDefinition<Models.Discussions.Tag> udbt;
                createdTags.Clear();
                if (session is not null)
                    await this._questions.InsertOneAsync(session, question, cancellationToken: ct);
                else
                    await this._questions.InsertOneAsync(question, cancellationToken: ct);
                // Now upsert tags in mongo
                // Maybe could be possible use an aggregation pipeline
                foreach (var tag in question.Tags)
                {
                    var upsert = new UpdateOptions { IsUpsert = true };
                    udbt = new UpdateDefinitionBuilder<Models.Discussions.Tag>().Inc(t => t.CountQuestions, 1)
                               .SetOnInsert(t => t.AuthorId, question.AuthorId).SetOnInsert(t => t.AuthorName, question.AuthorName)
                               .SetOnInsert(t => t.Defined, question.Created);
                    var res = (session is not null)
                        ? await this._tags.UpdateOneAsync(session, t => t.Id == tag, udbt, upsert, ct)
                        : await this._tags.UpdateOneAsync(t => t.Id == tag, udbt, upsert, ct);
                    if (res.IsAcknowledged && res.UpsertedId is not null)
                    {
                        udbu = new UpdateDefinitionBuilder<User>().AddToSet(u => u.CreatedTags, tag);
                        if (session is not null)
                            await this._users.UpdateOneAsync(session, u => u.Id == question.AuthorId, udbu, null, ct);
                        else
                            await this._users.UpdateOneAsync(u => u.Id == question.AuthorId, udbu, null, ct);
                        createdTags.Add(tag);
                    }
                }
                // creator by default follow a question
                udbu = new UpdateDefinitionBuilder<User>().Set(u => u.Updates[question.Id], update);
                if (session is not null)
                    await this._users.UpdateOneAsync(session, u => u.Id == question.AuthorId, udbu, null, ct);
                else
                    await this._users.UpdateOneAsync(u => u.Id == question.AuthorId, udbu, null, ct);
                return true; // just as placeholder
            };
            if (session is not null)
                await session.WithTransactionAsync(callbackAsync);
            else
                await callbackAsync(null, CancellationToken.None);

            await Task.WhenAll(createdTags
                .Select(t => this.neo4j.CreateTag(question.AuthorId, t))
                .Append(this.neo4j.AddQuestion(question)));
            await this.neo4j.SubscribeForQuestionUpdates(question.Id, new InterestedUser
            {
                UserId = question.AuthorId,
                DateTime = question.Created,
                UserName = question.Users[question.AuthorId],
            });
        }

        public async Task<string> AddAnswer(string questionId, Answer answer)
        {
            answer.Id = ObjectId.GenerateNewId().ToString();
            if (answer.Created == new DateTime())
            {
                answer.Created = DateTime.UtcNow;
            }
            // Update question and retrieve interesteds' list
            var question = await this._questions.FindOneAndUpdateAsync<Question>(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    // add username in dictionary
                    .Set(q => q.Users[answer.AuthorId], answer.AuthorName)
                    // add Question
                    .AddToSet(nameof(Question.Answers), answer),
                new FindOneAndUpdateOptions<Question>()
                {
                    Projection = new ProjectionDefinitionBuilder<Question>().Include(q => q.InterestedUsers)
                }
                );
            // check if anything has been updated?
            if (question is null)
            {
                return null;
            }
            // Notify interested user
            await this.Notify(question.Id, question.InterestedUsers?.Keys, answer.AuthorId);
            // Update Neo4j DB
            await this.neo4j.AddAnswer(questionId, answer);
            return answer.Id;
        }

        public async Task<string> CommentQuestion(string questionId, Comment comment)
        {
            comment.Id = ObjectId.GenerateNewId().ToString();
            if (comment.Created == new DateTime())
            {
                comment.Created = DateTime.UtcNow;
            }
            var question = await this._questions.FindOneAndUpdateAsync<Question>(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    // add username in dictionary
                    .Set(q => q.Users[comment.AuthorId], comment.AuthorName)
                    // add comment
                    .AddToSet(nameof(Question.Comments), comment),
                new FindOneAndUpdateOptions<Question>()
                {
                    Projection = new ProjectionDefinitionBuilder<Question>().Include(q => q.InterestedUsers)
                }
                );
            // check if anything has been updated
            if (question is null)
            {
                return null;
            }
            // Notify interested user
            await this.Notify(questionId, question.InterestedUsers?.Keys, comment.AuthorId);
            // Update Neo4j DB
            await this.neo4j.AddCommentToQuestion(comment, questionId);
            return comment.Id;
        }

        public async Task<string> CommentAnswer(string questionId, string answerId, Comment comment)
        {
            comment.Id = ObjectId.GenerateNewId().ToString();
            if (comment.Created == new DateTime())
            {
                comment.Created = DateTime.UtcNow;
            }
            var question = await this._questions.FindOneAndUpdateAsync<Question>(q => q.Id == questionId && q.Answers.Any(a => a.Id == answerId),
                new UpdateDefinitionBuilder<Question>()
                    // add username
                    .Set(q => q.Users[comment.AuthorId], comment.AuthorName)
                    // add comment
                    .Push(q => q.Answers.ElementAt(-1).Comments, comment),
                new FindOneAndUpdateOptions<Question>()
                {
                    Projection = new ProjectionDefinitionBuilder<Question>().Include(q => q.InterestedUsers)
                }
                );
            // check if anything has been updated
            if (question is null)
            {
                return null;
            }
            // Notify interested user
            await this.Notify(questionId, question.InterestedUsers?.Keys, comment.AuthorId);
            // Update Neo4j DB
            await this.neo4j.AddCommentToAnswer(comment, answerId);
            return comment.Id;
        }

        public async Task SubscribeForQuestionUpdates(string questionId, InterestedUser iu)
        {
            var session = await this.GetSessionHandle();
            // add username + add interested user
            var ud = new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.Users[iu.UserId], iu.UserName)
                    .Set(q => q.InterestedUsers[iu.UserId], iu);
            var options = new FindOneAndUpdateOptions<Question>
            {
                Projection = new ProjectionDefinitionBuilder<Question>()
                    .Include(q => q.Id).Include(q => q.AuthorId).Include(q => q.Title)
                    .Include(q => q.Users).Include(q => q.Tags).Include(q => q.Created)
            };
            if (iu.DateTime == new DateTime())
            {
                iu.DateTime = DateTime.UtcNow;
            }

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) =>
            {
                // Effectively update the list
                var query = new ExpressionFilterDefinition<Question>(q => q.Id == questionId && !q.InterestedUsers.ContainsKey(iu.UserId));
                var question = await (session is not null
                    ? this._questions.FindOneAndUpdateAsync<Question>(session, query, ud, options, ct)
                    : this._questions.FindOneAndUpdateAsync<Question>(query, ud, options, ct));
                // Was anything found?
                if (question is null)
                    return false;
                var update = new QuestionUpdates
                {
                    QuestionId = question.Id,
                    Title = question.Title,
                    Created = question.Created,
                    AuthorId = question.AuthorId,
                    AuthorName = question.Users[question.AuthorId],
                    Tags = question.Tags,
                    // They must not be update now to avoid data races
                    // set to their default values only for clarity
                    CountUpdates = null,
                    Solved = null,
                };
                var upDef = new UpdateDefinitionBuilder<User>().Set(u => u.Updates[questionId], update);
                // Save info about question in User collection
                await (session is not null
                    ? this._users.FindOneAndUpdateAsync(session, u => u.Id == iu.UserId, upDef, null, ct)
                    : this._users.FindOneAndUpdateAsync(u => u.Id == iu.UserId, upDef, null, ct));
                return true;
            };
            // Update Neo4j DB
            if (session is not null
                ? await session.WithTransactionAsync(callbackAsync)
                : await callbackAsync(null, CancellationToken.None))
                await this.neo4j.SubscribeForQuestionUpdates(questionId, iu);
        }

        public async Task UnsubscribeForQuestionUpdates(string questionId, string userId)
        {
            var session = await this.GetSessionHandle();

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) =>
            {
                var upDefQ = new UpdateDefinitionBuilder<Question>().Unset(q => q.InterestedUsers[userId]);
                var queryQ = new ExpressionFilterDefinition<Question>(q => q.Id == questionId && q.InterestedUsers.ContainsKey(userId));
                // Remove the user from the list of the interesteds
                var question = session is not null
                    ? await this._questions.FindOneAndUpdateAsync(session, queryQ, upDefQ, null, ct)
                    : await this._questions.FindOneAndUpdateAsync(queryQ, upDefQ, null, ct);
                if (question is not null)
                    return false;

                var queryU = new ExpressionFilterDefinition<User>(u => u.Id == userId);
                var upDefU = new UpdateDefinitionBuilder<User>().Unset(u => u.Updates[questionId]);
                // Remove field for updates in the User objects
                if (session is not null)
                    await this._users.FindOneAndUpdateAsync(session, queryU, upDefU, null, ct);
                else
                    await this._users.FindOneAndUpdateAsync(queryU, upDefU, null, ct);
                return true;
            };
            // Update Neo4j DB
            if (session is not null
                ? await session.WithTransactionAsync(callbackAsync)
                : await callbackAsync(null, CancellationToken.None))
                await this.neo4j.UnsubscribeForQuestionUpdates(questionId, userId);
        }

        public async Task<IEnumerable<Question>> GetInterestingQuestions(string userId)
        {
            var user = await this.usersService.GetUserById(userId);
            if (user is null)
            {
                return null;
            }
            var questions = new List<Question>();
            if (user.Updates is not null)
            {
                foreach (var item in user.Updates)
                {
                    var Q = new Question() {
                        Id = item.Key,
                        Title = item.Value.Title,
                        AuthorId = item.Value.AuthorId,
                        AuthorName = item.Value.AuthorName,
                        Created = item.Value.Created,
                        Tags = item.Value.Tags,
                        UnreadUpdates = item.Value.CountUpdates,
                    };
                    questions.Add(Q);
                }
            }

            return questions;
        }

        public async Task VoteAnswer(string questionId, string answerId, Vote vote)
        {
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId && q.Answers.Any(a => a.Id == answerId),
                new UpdateDefinitionBuilder<Question>()
                    // add username
                    .Set(q => q.Users[vote.AuthorId], vote.AuthorName)
                    // add vote
                    .Set(q => q.Answers.ElementAt(-1).Votes[vote.AuthorId], vote)
                );
            // Modified?
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            // Sync Neo4j
            await this.neo4j.VoteAnswer(answerId, vote);
        }

        public async Task RemoveAnswerVote(string questionId, string answerId, string voterId)
        {
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId && q.Answers.Any(a => a.Id == answerId),
                new UpdateDefinitionBuilder<Question>()
                    .Unset(q => q.Answers.ElementAt(-1).Votes[voterId])
                );
            // Modified?
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            // Sync Neo4j
            await this.neo4j.RemoveVoteToAnswer(answerId, voterId);
        }

        public async Task VoteCommentToQuestion(string questionId, string commentId, Vote vote)
        {
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId && q.Comments.Any(c => c.Id == commentId),
                new UpdateDefinitionBuilder<Question>()
                    // add username
                    .Set(q => q.Users[vote.AuthorId], vote.AuthorName)
                    // add vote
                    .Set(q => q.Comments.ElementAt(-1).Votes[vote.AuthorId], vote)
                );
            // Updated?
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.VoteComment(commentId, vote);
        }

        public async Task RemoveVoteToCommentToQuestion(string questionId, string commentId, string voterId)
        {
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId && q.Comments.Any(c => c.Id == commentId),
                new UpdateDefinitionBuilder<Question>()
                    .Unset(q => q.Comments.ElementAt(-1).Votes[voterId])
                );
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.RemoveVoteToComment(commentId, voterId);
        }

        /**
         * See here for the original hint:
         *  https://stackoverflow.com/questions/51621667/how-to-update-item-from-array-nested-within-array/51622030#51622030
         * MongoDB Doc:
         *  https://docs.mongodb.com/manual/reference/operator/update/positional-filtered/#update-all-documents-that-match-arrayfilters-in-an-array
         */
        public async Task VoteCommentToAnswer(string questionId, string answerId, string commentId, Vote vote)
        {
            var votedComment = $"{nameof(Question.Answers)}.$[a].{nameof(Answer.Comments)}.$[c].{nameof(Comment.Votes)}.{vote.AuthorId}";
            // q.Answers.Any(a => a.Id == answerId && a.Comments.Any(c => c.Id == commentId))
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    // add username
                    .Set(q => q.Users[vote.AuthorId], vote.AuthorName)
                    //.Set(q => q.Answers.First(a => a.Id == answerId).Comments.First(c => c.Id == commentId).Votes[vote.AuthorId], vote),
                    .Set(votedComment, vote),
                new UpdateOptions()
                {
                    ArrayFilters = new ArrayFilterDefinition[] {
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("a._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(answerId))))),
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("c._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(commentId))))),
                    // Old attempt to not use "magics"
                       // new BsonDocumentArrayFilterDefinition<Question>(
                       //     // Used Constructor:
                       //     //  https://mongodb.github.io/mongo-csharp-driver/2.14/apidocs/html/M_MongoDB_Bson_BsonDocument__ctor_11.htm
                       //     new MongoDB.Bson.BsonDocument(
                       //         Builders<Answer>.Filter.Eq(a => a.Id, answerId).ToBsonDocument()
                       //         )
                       //     )
                    }
                }
                );
            // Updated?
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.VoteComment(commentId, vote);
        }

        public async Task RemoveVoteToCommentToAnswer(string questionId, string answerId, string commentId, string voterId)
        {
            var votedComment = $"{nameof(Question.Answers)}.$[a].{nameof(Answer.Comments)}.$[c].{nameof(Comment.Votes)}.{voterId}";
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    .Unset(votedComment),
                new UpdateOptions()
                {
                    ArrayFilters = new ArrayFilterDefinition[] {
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("a._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(answerId))))),
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("c._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(commentId))))),
                    }
                }
                );
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.RemoveVoteToComment(commentId, voterId);
        }

        public async Task SetSolvedStatus(string questionId, bool solved)
        {
            var question = await this._questions.FindOneAndUpdateAsync<Question>(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.Solved, solved),
                new FindOneAndUpdateOptions<Question>()
                {
                    Projection = new ProjectionDefinitionBuilder<Question>().Include(q => q.InterestedUsers)
                }
                );
            // Check if anything has been updated
            if (question is null)
            {
                return;
            }
            // Sync Neo4j DB
            await this.neo4j.SetQuestionSolvedStatus(questionId, solved);
            // Notify interested user
            var InterestedUsers = question.InterestedUsers?.Keys;
            if (InterestedUsers is null)
            {
                return;
            }
            await this._users.UpdateManyAsync(u => InterestedUsers.Contains(u.Id),
                new UpdateDefinitionBuilder<User>()
                    .Set(u => u.Updates[questionId].Solved, solved)
                );
        }

        /**
         * Allow an asking user to safely choose the "Solution"
         */
        public async Task MarkAnswerAsSolution(string questionId, string answerId, string authorId) =>
            await this._questions.FindOneAndUpdateAsync(q => q.Id == questionId && q.AuthorId == authorId && q.Answers.Any(a => a.Id == answerId),
                new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.SolutionAnswerId, answerId)
                );

        /**
         * Allow the askeing user to unset the question choosen as "Solution"
         */
        public async Task UnmarkAnswerAsSolution(string questionId, string answerId, string authorId) =>
            await this._questions.FindOneAndUpdateAsync(q => q.Id == questionId && q.AuthorId == authorId && q.SolutionAnswerId == answerId,
                new UpdateDefinitionBuilder<Question>()
                    .Unset(q => q.SolutionAnswerId)
                );

        /**
         * Following methods are used to mark questions deleted by admins
         */
        public async Task RemoveQuestion(string question, string moderator, string reason)
        {
            var info = new RemovedPostInfo
            {
                ModeratorId = moderator,
                DateTime = DateTime.UtcNow,
                Reason = reason,
            };

            var res = await this._questions.UpdateOneAsync(q => q.Id == question && q.Removed == null,
                new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.Removed, info)
                );
            
            // Check if anythinf has been modified
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.HideQuestion(question, moderator);
        }

        public async Task RemoveAnswer(string question, string answer, string moderator, string reason)
        {
            var info = new RemovedPostInfo
            {
                ModeratorId = moderator,
                DateTime = DateTime.UtcNow,
                Reason = reason,
            };

            var res = await this._questions.UpdateOneAsync(q => q.Id == question && q.Answers.Any(a => a.Id == answer && a.Removed == null) && q.Removed == null,
                new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.Answers.ElementAt(-1).Removed, info)
                );

            // Check if anythinf has been modified
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.HideAnswer(answer, moderator);
        }

        public async Task RemoveCommentToQuestion(string question, string comment, string moderator, string reason)
        {
            var info = new RemovedPostInfo
            {
                ModeratorId = moderator,
                DateTime = DateTime.UtcNow,
                Reason = reason,
            };

            var res = await this._questions.UpdateOneAsync(q => q.Id == question && q.Comments.Any(c => c.Id == comment && c.Removed == null),
                new UpdateDefinitionBuilder<Question>()
                    .Set(q => q.Comments.ElementAt(-1).Removed, info)
                );

            // Check if anythinf has been modified
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.HideComment(comment, moderator);
        }

        public async Task RemoveCommentToAnswer(string questionId, string answerId, string commentId, string moderator, string reason)
        {
            var info = new RemovedPostInfo
            {
                ModeratorId = moderator,
                DateTime = DateTime.UtcNow,
                Reason = reason,
            };

            var votedComment = nameof(Question.Answers) + ".$[a]." + nameof(Answer.Comments) + ".$[c]." + nameof(Comment.Removed);
            var res = await this._questions.UpdateOneAsync(q => q.Id == questionId,
                new UpdateDefinitionBuilder<Question>()
                    .Set(votedComment, info),
                new UpdateOptions()
                {
                    ArrayFilters = new ArrayFilterDefinition[] {
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("a._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(answerId))))),
                        new BsonDocumentArrayFilterDefinition<Question>(
                            new BsonDocument("c._id", new BsonDocument("$eq", new BsonObjectId(new ObjectId(commentId))))),
                    }
                }
                );
            
            // Check if anythinf has been modified
            if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
            {
                return;
            }
            await this.neo4j.HideComment(commentId, moderator);
        }

        /**
         * Notify interested users
         */
        private async Task Notify(string questionId, IEnumerable<string> interestedUsers, string except = null)
        {
            // If empty nothing to do
            if (interestedUsers is null)
                return;
            // Any user not to notify?
            if (except is not null)
                interestedUsers = interestedUsers.Where(u => u != except);
            // if the list is empty stop
            if (!interestedUsers.Any())
                return;
            await this._users.UpdateManyAsync(u => interestedUsers.Contains(u.Id),
                new UpdateDefinitionBuilder<User>()
                    .Inc(u => u.Updates[questionId].CountUpdates, 1)
                );
        }

        public async Task<User> ConsumeNotification(string userId, string questionId)
        {
            return await this._users.FindOneAndUpdateAsync(u => u.Id == userId && u.Updates.ContainsKey(questionId),
                new UpdateDefinitionBuilder<User>()
                    .Set(u => u.Updates[questionId].CountUpdates, 0)
                );
        }

        // ******************************************************************
        // The followings are TAGS related functions!!!
        // ******************************************************************
        /**
         * Return the number of existing TAGS.
         *  It is the number of different elements in Question.Tags[] array
         */
        public async Task<IEnumerable<Models.Discussions.Tag>> GetTags(string tagSearch, int? limit = null, int? skip = null)
        {
            var tags = await (tagSearch == null ? this._tags.Find(x => true) : this._tags.Find(x => x.Id.StartsWith(tagSearch)))
                .Project<Models.Discussions.Tag>(new ProjectionDefinitionBuilder<Models.Discussions.Tag>().Include(t => t.Id).Include(t => t.CountQuestions))
                .Sort(new SortDefinitionBuilder<Models.Discussions.Tag>().Ascending(t => t.Id))
                .Skip(skip)
                .Limit(limit)
                .ToListAsync();

            return tags;
        }

        /**
         * Perform a full text search on the collection to find relevant questions
         */
        public async Task<IEnumerable<Question>> SearchQuestions(string keywords, IEnumerable<String> tags = null, int? skip = null, int? take = null)
        {
            var query = new FilterDefinitionBuilder<Question>().Text(keywords);
            // ignore removed questions
            query &= new ExpressionFilterDefinition<Question>(q => q.Removed == null);
            if (tags is not null)
            {
                query &= new FilterDefinitionBuilder<Question>().All<string>(q => q.Tags, tags);
            }
            // See here for meta:
            // mongodb doc:
            //  https://docs.mongodb.com/manual/core/link-text-indexes/
            // How to use in C#:
            //  https://stackoverflow.com/questions/32194379/mongodb-text-search-with-sorting-in-c-sharp
            const string text_score_field = "score"; // never use magics
            var ans = await this._questions
                .Find(query)
                .Project(new ProjectionDefinitionBuilder<Question>().MetaTextScore(text_score_field).Exclude(q => q.Comments).Exclude(q => q.Answers).Exclude(q => q.InterestedUsers))
                .Sort(new SortDefinitionBuilder<Question>().MetaTextScore(text_score_field))
                .As<Question>()
                .Skip(skip)
                .Limit(take)
                .ToListAsync();
            ans.ForEach(q => q.AfterDeserialisation());
            return ans;
        }

        /**
         * Find questions with the specified tag
         */
        public async Task<IEnumerable<Question>> GetQuestionsByTag(string tag, int? skip = null, int? take = null) =>
            await this._questions.Find(q => q.Tags.Contains(tag))
                .Sort(new SortDefinitionBuilder<Question>().Descending(q => q.Created)).Skip(skip).Limit(take).ToListAsync();

        public async Task<IEnumerable<Question>> GetQuestionsByTags(IEnumerable<String> tags) =>
            await this._questions.Find(new FilterDefinitionBuilder<Question>().All<string>(q => q.Tags, tags))
                .Sort(new SortDefinitionBuilder<Question>().Descending(q => q.Created)).ToListAsync();

        /************************************************
         * AGGREGATION PIPELINES
         ************************************************
         */
        public async Task<QuestionStats> GetQuestionStats()
        {
            // Mongo syntax:
            // [{$project: {
            //  Title: 1,
            //  Users: {
            //   $size: {
            //    $objectToArray: '$Users'
            //   }
            //  },
            //  Comments: {
            //   $size: {
            //    $ifNull: [
            //     '$Comments',
            //     []
            //    ]
            //   }
            //  },
            //  Answers: {
            //   $ifNull: [
            //    '$Answers',
            //    []
            //   ]
            //  }
            // }}, {$addFields: {
            //  CommentsToAnswers: {
            //   $sum: {
            //    $map: {
            //     input: '$Answers',
            //     'in': {
            //      $size: {
            //       $ifNull: [
            //        '$$this.Comments',
            //        []
            //       ]
            //      }
            //     }
            //    }
            //   }
            //  },
            //  Answers: {
            //   $size: '$Answers'
            //  }
            // }}, {$group: {
            //  _id: null,
            //  AvgUsers: {
            //   $avg: '$Users'
            //  },
            //  StdDevUsers: {
            //   $stdDevPop: '$Users'
            //  },
            //  AvgAnswers: {
            //   $avg: '$Answers'
            //  },
            //  StdDevAnswers: {
            //   $stdDevPop: '$Answers'
            //  },
            //  AvgComments: {
            //   $avg: '$Comments'
            //  },
            //  StdDevComments: {
            //   $stdDevPop: '$Comments'
            //  },
            //  AvgCommentsToAnswers: {
            //   $avg: '$CommentsToAnswers'
            //  },
            //  StdDevCommentsToAnswers: {
            //   $stdDevPop: '$CommentsToAnswers'
            //  }
            // }}]
            // Doc:
            //  http://mongodb.github.io/mongo-csharp-driver/2.7/reference/driver/crud/linq/
            var pipeline = new BsonDocument[]
            {
                new BsonDocument("$project", new BsonDocument
                    {
                        { "Title", 1 },
                        { "Users", new BsonDocument("$size", new BsonDocument("$objectToArray", "$Users")) },
                        { "Comments", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$Comments", new BsonArray() })) },
                        { "Answers", new BsonDocument("$ifNull", new BsonArray { "$Answers", new BsonArray() }) }
                    }),
                new BsonDocument("$addFields", new BsonDocument
                    { 
                        { "CommentsToAnswers", new BsonDocument(
                            "$sum", new BsonDocument(
                                "$map", new BsonDocument {
                                    { "input", "$Answers" },
                                    { "in", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$$this.Comments", new BsonArray() })) } })) },
                        { "Answers", new BsonDocument("$size", "$Answers") }
                    }),
                new BsonDocument("$group", new BsonDocument
                    {
                        { "_id", BsonNull.Value },
                        { nameof(QuestionStats.AvgUsers), new BsonDocument("$avg", "$Users") },
                        { nameof(QuestionStats.StdDevUsers), new BsonDocument("$stdDevPop", "$Users") },
                        { nameof(QuestionStats.AvgAnswers), new BsonDocument("$avg", "$Answers") },
                        { nameof(QuestionStats.StdDevAnswers), new BsonDocument("$stdDevPop", "$Answers") },
                        { nameof(QuestionStats.AvgComments), new BsonDocument("$avg", "$Comments") },
                        { nameof(QuestionStats.StdDevComments), new BsonDocument("$stdDevPop", "$Comments") },
                        { nameof(QuestionStats.AvgCommentsToAnswers), new BsonDocument("$avg", "$CommentsToAnswers") },
                        { nameof(QuestionStats.StdDevCommentsToAnswers), new BsonDocument("$stdDevPop", "$CommentsToAnswers") }
                    })
            };
            // only one result expected
            var res = await this._questions.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
            return new QuestionStats
            {
                AvgUsers = res?[nameof(QuestionStats.AvgUsers)].ToDouble() ?? 0.0,
                StdDevUsers = res?[nameof(QuestionStats.StdDevUsers)].ToDouble() ?? 0.0,
                AvgAnswers = res?[nameof(QuestionStats.AvgAnswers)].ToDouble() ?? 0.0,
                StdDevAnswers = res?[nameof(QuestionStats.StdDevAnswers)].ToDouble() ?? 0.0,
                AvgComments = res?[nameof(QuestionStats.AvgComments)].ToDouble() ?? 0.0,
                StdDevComments = res?[nameof(QuestionStats.StdDevComments)].ToDouble() ?? 0.0,
                AvgCommentsToAnswers = res?[nameof(QuestionStats.AvgCommentsToAnswers)].ToDouble() ?? 0.0,
                StdDevCommentsToAnswers = res?[nameof(QuestionStats.StdDevCommentsToAnswers)].ToDouble() ?? 0.0,
            };
        }
    }
}
