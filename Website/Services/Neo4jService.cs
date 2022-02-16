using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Models.Settings;
using Website.Models.Discussions;
using Website.Models.Users;
using Neo4jClient;
using Neo4jClient.Cypher;
using Neo4jClient.Extensions;
using Neo4jClient.Execution;
using Neo4jClient.Transactions;
using Neo4jClient.Serialization;
using Neo4j.Driver;
using Website.Models.Statistics;

namespace Website.Services
{
    public class Neo4jService
    {
        private readonly IGraphClient graphClient;
        private bool ignoreConnectionFault;
        public Neo4jService(INeo4jSettings settings)
        {
            // About the 4.x version:
            //  https://xclave.co.uk/2020/09/28/neo4jclient-4-0/
            var client = new GraphClient(new Uri(settings.ConnectionString), settings.Username, settings.Password);
            InitConnection(client).Wait();
            this.graphClient = client;
            this.ignoreConnectionFault = true;
        }

        private async Task InitConnection(IGraphClient client)
        {
            // https://stackoverflow.com/questions/65138622/how-to-handle-the-new-connectasync-api-c-neo4jclient
            await client.ConnectAsync();
            // https://neo4j.com/docs/developer-manual/current/cypher/schema/constraints/
            // warning: CreateUniqueConstraint() does not allow to specify if not exist
            await client.Cypher
                .Create($"CONSTRAINT IF NOT EXISTS FOR (u:{nameof(User)}) REQUIRE u.{nameof(User.Id)} IS UNIQUE")
                //.CreateUniqueConstraint($"u:{nameof(User)}", $"u.{nameof(User.Id)}")
                .ExecuteWithoutResultsAsync();
            await client.Cypher
                .Create($"CONSTRAINT IF NOT EXISTS FOR (q:{nameof(Question)}) REQUIRE q.{nameof(Question.Id)} IS UNIQUE")
                .ExecuteWithoutResultsAsync();
            await client.Cypher
                .Create($"CONSTRAINT IF NOT EXISTS FOR (a:{nameof(Answer)}) REQUIRE a.{nameof(Answer.Id)} IS UNIQUE")
                .ExecuteWithoutResultsAsync();
            await client.Cypher
                .Create($"CONSTRAINT IF NOT EXISTS FOR (c:{nameof(Comment)}) REQUIRE c.{nameof(Comment.Id)} IS UNIQUE")
                .ExecuteWithoutResultsAsync();
            await client.Cypher
                .Create($"CONSTRAINT IF NOT EXISTS FOR (t:{nameof(Models.Discussions.Tag)}) REQUIRE t.{nameof(Models.Discussions.Tag.Id)} IS UNIQUE")
                .ExecuteWithoutResultsAsync();
        }

        public async Task AddUser(User user)
        {
            // Good exaple using C# (but old!)
            //  https://github.com/DotNet4Neo4j/Neo4jClient/wiki/cypher-examples#create-a-user
            // Test new version
            //  https://github.com/DotNet4Neo4j/Neo4jClient/blob/master/Neo4jClient.Tests/Cypher/CypherFluentQueryWithParamTests.cs
            // WithParam has been copied from here:
            //  var cfq = bgc.Cypher.Create("MATCH (node:FakeNode) SET node += $param").WithParam("param", obj);
            //  https://github.com/DotNet4Neo4j/Neo4jClient/blob/edd16ff89ca45bdeb0b994f70e7f099cfe496e8c/Neo4jClient.Tests/BoltGraphClientTests/BoltGraphClientTests.cs#L288
            try
            {
                await this.graphClient
                    .Cypher
                    .Create($"(u:{nameof(User)})")
                    .Set("u += $newUser")
                    .WithParam("newUser", new {
                        user.Id,
                        user.Username,
                    })
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task AddQuestion(Question question)
        {
            var qParams = new {
                question.Id,
                question.Title,
                question.Created,
            };
            try
            {
                await this.graphClient
                    .Cypher
                    // find the creator
                    .Match($"(u:{nameof(User)})")
                    .Where("u.Id = $author")
                    // create the question
                    .Create($"(q:{nameof(Question)})")
                    .Set($"q += ${nameof(qParams)}")
                    // add the link to user
                    .Create("(u)-[:ASKED]->(q)")
                    // Merge tags
                    .With("q")
                    .Unwind(question.Tags, "tag")
                    .Merge($"(t:{nameof(Models.Discussions.Tag)} {{Id: tag}})")
                    // add links to tags
                    .Create("(q)-[:ABOUT]->(t)")
                    // set parameters
                    .WithParams(new Dictionary<string, object> {
                        [nameof(qParams)] = qParams,
                        ["author"] = question.AuthorId
                    })
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task SubscribeForQuestionUpdates(string questionId, InterestedUser iu)
        {
            var userId = iu.UserId;
            try
            {
                await this.graphClient
                    .Cypher
                    // find user
                    .Match($"(u:{nameof(User)})")
                    .Where($"u.Id = ${nameof(userId)}")
                    // find question
                    .Match($"(q:{nameof(Question)})")
                    .Where($"q.Id = ${nameof(questionId)}")
                    // add the link
                    .Create("(u)-[:WAIT_FOR_UPDATE]->(q)")
                    // set parameters
                    .WithParams(new Dictionary<string, object> {
                        [nameof(userId)] = userId,
                        [nameof(questionId)] = questionId,
                    })
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task UnsubscribeForQuestionUpdates(string questionId, string userId)
        {
            try
            {
                await this.graphClient
                    .Cypher
                    // find user
                    .Match($"(u:{nameof(User)})")
                    .Where($"u.Id = ${nameof(userId)}")
                    // find question
                    .Match($"(q:{nameof(Question)})")
                    .Where($"q.Id = ${nameof(questionId)}")
                    // find the link
                    .Match("(u)-[r:WAIT_FOR_UPDATE]->(q)")
                    // remove the link
                    .Delete("r")
                    // set parameters
                    .WithParams(new Dictionary<string, object>
                    {
                        [nameof(userId)] = userId,
                        [nameof(questionId)] = questionId,
                    })
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task<IEnumerable<Question>> GetInterestingQuestions(string userId)
        {
            try
            {
                return await this.graphClient
                    .Cypher
                    // w stands for writer
                    .Match($"(u:{nameof(User)})-[:WAIT_FOR_UPDATE]->(q:{nameof(Question)})<-[:ASKED]-(w:{nameof(User)})")
                    .Where($"u.Id = ${nameof(userId)}")
                    .WithParams(new Dictionary<string, object>
                    {
                        [nameof(userId)] = userId,
                    })
                    // See example for result format (it's old but might work)
                    //  https://github.com/DotNet4Neo4j/Neo4jClient/wiki/cypher-examples
                    .Return<Question>((q, w) => new Question {
                        Id = q.As<Question>().Id,
                        Title = q.As<Question>().Title,
                        Created = q.As<Question>().Created,
                        Tags = q.As<Question>().Tags,
                        AuthorId = w.As<User>().Id,
                        AuthorName = w.As<User>().Username,
                    })
                    .ResultsAsync;
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return Enumerable.Empty<Question>();
            }
        }

        public async Task AddAnswer(string questionId, Answer answer)
        {
            var aParams = new {
                answer.Id,
            };
            try
            {
                await this.graphClient
                    .Cypher
                    // find the author
                    .Match($"(u:{nameof(User)})")
                    .Where("u.Id = $author")
                    // find question
                    .Match($"(q:{nameof(Question)})")
                    .Where($"q.Id = ${nameof(questionId)}")
                    // create the answer
                    .Create($"(a:{nameof(Answer)})")
                    .Set($"a += ${nameof(aParams)}")
                    // add link to the question
                    .Create("(a)-[:ANSWERED]->(q)")
                    // add link to the user
                    .Create("(u)-[:WROTE]->(a)")
                    .WithParams(new Dictionary<string, object>
                    {
                        [nameof(aParams)] = aParams,
                        [nameof(questionId)] = questionId,
                        ["author"] = answer.AuthorId,
                    })
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task AddCommentToQuestion(Comment comment, string questionId)
        {
            var cParams = new {
                comment.Id,
            };
            var query = this.graphClient
                .Cypher
                // find the author
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find the referring question
                .Match($"(q:{nameof(Question)})")
                .Where($"q.Id = ${nameof(questionId)}")
                // create the comment
                .Create($"(c:{nameof(Comment)})")
                .Set($"c += ${nameof(cParams)}")
                // add link to the question
                .Create("(c)-[:REFERS_TO]->(q)")
                // add link to the user
                .Create("(u)-[:COMMENTED]->(c)")
                // set params
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(cParams)] = cParams,
                    [nameof(questionId)] = questionId,
                    ["author"] = comment.AuthorId,
                });
            try
            {
                    await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task AddCommentToAnswer(Comment comment, string answerId)
        {
            var cParams = new
            {
                comment.Id,
            };
            var query = this.graphClient
                .Cypher
                // find the author
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find the referring question
                .Match($"(a:{nameof(Answer)})")
                .Where($"a.Id = ${nameof(answerId)}")
                // create the comment
                .Create($"(c:{nameof(Comment)})")
                .Set($"c += ${nameof(cParams)}")
                // add link to the question
                .Create("(c)-[:REFERS_TO]->(a)")
                // add link to the user
                .Create("(u)-[:COMMENTED]->(c)")
                // set params
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(cParams)] = cParams,
                    [nameof(answerId)] = answerId,
                    ["author"] = comment.AuthorId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task VoteComment(string commentId, Vote vote)
        {
            var vParams = new {
                Useful = vote.IsUseful,
            };
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find comment
                .Match($"(c:{nameof(Comment)})")
                .Where($"c.Id = ${nameof(commentId)}")
                // add link representing vote
                .Merge("(u)-[r:VOTED]->(c)")
                // add params
                .Set($"r += ${nameof(vParams)}")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(vParams)] = vParams,
                    [nameof(commentId)] = commentId,
                    ["author"] = vote.AuthorId,
                });

            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task RemoveVoteToComment(string commentId, string voterId)
        {
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find comment
                .Match($"(c:{nameof(Comment)})")
                .Where($"c.Id = ${nameof(commentId)}")
                // find link representing vote
                .Match("(u)-[r:VOTED]->(c)")
                // remove link
                .Delete("r")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(commentId)] = commentId,
                    ["author"] = voterId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task CreateTag(string creatorId, string tag)
        {
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(creatorId)}}})")
                // create tag
                .Create($"(t:{nameof(Models.Discussions.Tag)} {{Id: ${nameof(tag)}}})")
                // add link
                .Create("(u)-[:CREATED]->(t)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(creatorId)] = creatorId,
                    [nameof(tag)] = tag,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task FollowTag(string followerId, string tag)
        {
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(followerId)}}})")
                // find tag
                .Match($"(t:{nameof(Models.Discussions.Tag)} {{Id: ${nameof(tag)}}})")
                // add link
                .Create("(u)-[:FOLLOWS]->(t)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(followerId)] = followerId,
                    [nameof(tag)] = tag,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task UnfollowTag(string followerId, string tag)
        {
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(followerId)}}})")
                // find tag
                .Match($"(t:{nameof(Models.Discussions.Tag)} {{Id: ${nameof(tag)}}})")
                // find link
                .Match("(u)-[r:FOLLOWS]->(t)")
                // delete link
                .Delete("r")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(followerId)] = followerId,
                    [nameof(tag)] = tag,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task VoteAnswer(string answerId, Vote vote)
        {
            var vParams = new
            {
                Useful = vote.IsUseful,
            };
            var query =
                this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find comment
                .Match($"(a:{nameof(Answer)})")
                .Where($"a.Id = ${nameof(answerId)}")
                // add link representing vote
                .Merge("(u)-[r:VOTED]->(a)")
                // add params
                .Set($"r += ${nameof(vParams)}")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(vParams)] = vParams,
                    [nameof(answerId)] = answerId,
                    ["author"] = vote.AuthorId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task RemoveVoteToAnswer(string answerId, string voterId)
        {
            var query = this.graphClient
                .Cypher
                // find user
                .Match($"(u:{nameof(User)})")
                .Where("u.Id = $author")
                // find comment
                .Match($"(a:{nameof(Answer)})")
                .Where($"a.Id = ${nameof(answerId)}")
                // find link representing vote
                .Match("(u)-[r:VOTED]->(a)")
                // remove link
                .Delete("r")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(answerId)] = answerId,
                    ["author"] = voterId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        /**
         * Hide* methods will be used to trak Q&A&C removed
         * (hidden) by admins
         */
        public async Task HideQuestion(string questionId, string adminId)
        {
            var query = this.graphClient
                .Cypher
                // find admin (tahs is a user)
                .Match($"(u:{nameof(User)} {{Id: ${nameof(adminId)}}})")
                // match the question to hide
                .Match($"(q:{nameof(Question)} {{Id: ${nameof(questionId)}}})")
                // hide
                .Merge("(u)-[:HID]->(q)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(questionId)] = questionId,
                    [nameof(adminId)] = adminId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }


        public async Task HideAnswer(string answerId, string adminId)
        {
            var query = this.graphClient
                .Cypher
                // find admin (tahs is a user)
                .Match($"(u:{nameof(User)} {{Id: ${nameof(adminId)}}})")
                // match the answer to hide
                .Match($"(a:{nameof(Answer)} {{Id: ${nameof(answerId)}}})")
                // hide
                .Merge("(u)-[:HID]->(a)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(answerId)] = answerId,
                    [nameof(adminId)] = adminId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task HideComment(string commentId, string adminId)
        {
            var query = this.graphClient
                .Cypher
                // find admin (tahs is a user)
                .Match($"(u:{nameof(User)} {{Id: ${nameof(adminId)}}})")
                // match the answer to hide
                .Match($"(c:{nameof(Comment)} {{Id: ${nameof(commentId)}}})")
                // hide
                .Merge("(u)-[:HID]->(c)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(commentId)] = commentId,
                    [nameof(adminId)] = adminId,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task SetQuestionSolvedStatus(string questionId, bool solved)
        {
            var query = this.graphClient
                .Cypher
                // find question
                .Match($"(q:{nameof(Question)})")
                .Where($"q.Id = ${nameof(questionId)}")
                .Set($"q.Solved = ${nameof(solved)}")
                // set params
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(questionId)] = questionId,
                    [nameof(solved)] = solved ? solved : null,
                });
            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task<long> CountAskedQuestions(string userId)
        {
            try
            {
                return (await this.graphClient
                    .Cypher
                    .Match($"(u:{nameof(User)} {{Id: ${nameof(userId)}}})-[:ASKED]->(q:{nameof(Question)})")
                    .WithParam(nameof(userId), userId)
                    .Return<long>("count(q)")
                    .ResultsAsync).First();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return 0;
            }
        }

        public async Task<IEnumerable<Question>> GetAskedQuestions(string userId, int? limit = null, int? skip = null)
        {
            var query = this.graphClient
                .Cypher
                // Find the user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(userId)}}})")
                // find all question
                .Match($"(q:{nameof(Question)})")
                // not asked by the user
                .Where("(u)-[:ASKED]->(q)")
                .Match($"(q)-[:ABOUT]->(t:{nameof(Tag)})")
                .With("*, t.Id as tag")
                .WithParam(nameof(userId), userId)
                .ReturnDistinct((q, u, tag) => new Question
                {
                    Id = q.As<Question>().Id,
                    Title = q.As<Question>().Title,
                    Created = q.As<Question>().Created,
                    Tags = tag.CollectAsDistinct<string>(),
                    AuthorId = u.As<User>().Id,
                    AuthorName = u.As<User>().Username,
                })
                .OrderByDescending($"q.{nameof(Question.Created)}")
                .Skip(skip)
                .Limit(limit);
            try
            {
                return await query.ResultsAsync;
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return Enumerable.Empty<Question>();
            }
        }

        /**
         * This query find all Question-related dicussions a user contributed to
         * with comments and answers without being the asker
         */
        public async Task<IEnumerable<Question>> FindDiscussionsUserContributedTo(string userId)
        {
            var query = this.graphClient
                .Cypher
                // Find the user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(userId)}}})")
                // find all question
                .Match($"(q:{nameof(Question)})<-[:ASKED]-(author:{nameof(User)})")
                // not asked by the user
                .Where("NOT (u)-[:ASKED]->(q) AND ("
                // but which the user contributed to vith an answer
                + $" (u)-[:WROTE]->(:{nameof(Answer)})-[:ANSWERED]->(q)"
                // or a comment to question
                + $" OR (u)-[:COMMENTED]->(:{nameof(Comment)})-[:REFERS_TO]->(q)"
                // or a comment to answer
                + $" OR (u)-[:COMMENTED]->(:{nameof(Comment)})-[:REFERS_TO]->(:{nameof(Answer)})-[:ANSWERED]->(q)"
                + ")"
                )
                .Match($"(q)-[:ABOUT]->(t:{nameof(Tag)})")
                .With("*, t.Id as tag")
                .WithParam(nameof(userId), userId)
                .ReturnDistinct((q, author, tag) => new Question
                {
                    Id = q.As<Question>().Id,
                    Title = q.As<Question>().Title,
                    Created = q.As<Question>().Created,
                    Tags = tag.CollectAsDistinct<string>(),
                    AuthorId = author.As<User>().Id,
                    AuthorName = author.As<User>().Username,
                })
                .OrderByDescending($"q.{nameof(Question.Created)}");

            try
            {
                return await query.ResultsAsync;
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return Enumerable.Empty<Question>();
            }
        }

        /**
         * Count how many votes the user received
         */
        public async Task<UserVoteStats> CountVotesReceivedByUser(string userId)
        {
            var query = this.graphClient
                .Cypher
                // Find the user
                .Match($"(u:{nameof(User)} {{Id: ${nameof(userId)}}})")
                .With("u")
                // count votes to answers
                .OptionalMatch($"(u)-[:WROTE]->(:{nameof(Answer)})<-[v:VOTED]-(:{nameof(User)})")
                .With("u, count(CASE v.Useful WHEN true THEN 1 ELSE null END) as aLike, count(CASE v.Useful WHEN false THEN 1 ELSE null END) as aDislike")
                .OptionalMatch($"(u)-[:COMMENTED]->(:{nameof(Comment)})<-[v:VOTED]-(:{nameof(User)})")
                .With("u, aLike, aDislike, count(CASE v.Useful WHEN true THEN 1 ELSE null END) as cLike, count(CASE v.Useful WHEN false THEN 1 ELSE null END) as cDislike")
                .WithParam(nameof(userId), userId)
                .Return((aLike, aDislike, cLike, cDislike) => new UserVoteStats
                {
                    ALike = aLike.As<int>(),
                    ADislike = aDislike.As<int>(),
                    CLike = cLike.As<int>(),
                    CDislike = cDislike.As<int>(),
                });

            try
            {
                var ans = await query.ResultsAsync;
                return ans.FirstOrDefault() ?? new UserVoteStats();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return new UserVoteStats();
            }
        }

        /**
         * Return the list of the tags used with the given one
         * and the number of times they are used together
         */
        public async Task<IDictionary<string, long>> GetTagCousages(string tag)
        {
            var ans = new Dictionary<string, long>();
            var query = this.graphClient
                .Cypher
                .Match($"(t1:{nameof(Tag)} {{Id: $tag}})--(q:{nameof(Question)})--(t2:{nameof(Models.Discussions.Tag)})")
                .Where("t1 <> t2")
                .WithParam(nameof(tag), tag)
                .Return((t2, q) => new
                {
                    Name = t2.As<Tag>().Id,
                    Count = q.Count(),
                });
            try
            {
                var res = await query.ResultsAsync;
                foreach (var item in res)
                {
                    ans.Add(item.Name, item.Count);
                }
                return ans;
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
                return ans;
            }
        }

        /**
         * Remove all the nodes from the database
         */
        public async Task ClearDatabase()
        {
            try
            {
                await this.graphClient
                    .Cypher
                    .Match("(n)")
                    .DetachDelete("n")
                    .ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task SyncUser(User user)
        {
            var userId = user.Id;
            int counter = 0;
            // add user
            var query = this.graphClient
                .Cypher
                .Merge($"(u:{nameof(User)} {{ Id: ${nameof(userId)} }})")
                .WithParam(nameof(userId), userId)
                .Set("u += $newUser")
                .WithParam("newUser", new
                {
                    user.Username,
                })
                .With("u");
            // add created tags
            if (user.CreatedTags is not null) foreach (var cTag in user.CreatedTags)
            {
                var param = nameof(cTag) + (counter++).ToString();
                query = query
                    .Merge($"(t:{nameof(Tag)} {{ Id: ${param} }})")
                    .WithParam(param, cTag)
                    .Merge("(u)-[:CREATED]->(t)")
                    .With("u");
            }

            if (user.FollowedTags is not null) foreach (var fTag in user.FollowedTags)
            {
                var param = nameof(fTag) + (counter++).ToString();
                query = query
                    .Merge($"(t:{nameof(Tag)} {{ Id: ${param} }})")
                    .WithParam(param, fTag)
                    .Merge("(u)-[:FOLLOWS]->(t)")
                    .With("u");
            }

            if (user.Updates is not null) foreach (var qU in user.Updates)
            {
                var param = nameof(qU) + (counter++).ToString();
                query = query
                    .Merge($"(q:{nameof(Question)} {{ Id: ${param} }})")
                    .Create("(u)-[:WAIT_FOR_UPDATE]->(q)")
                    .WithParam(param, qU.Key)
                    .With("u");
            }
            query = query.Return<User>("u");

            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task SyncQuestion(Question question)
        {
            var questionId = question.Id;
            var authorId = question.AuthorId;
            int counter = 0;
            var query = this.graphClient
                .Cypher
                // set question
                .Merge($"(q:{nameof(Question)} {{ Id: ${nameof(questionId)} }})")
                .WithParam(nameof(questionId), questionId)
                .Set("q += $newQuestion")
                .WithParam("newQuestion", new {
                    question.Title,
                    question.Created,
                })
                // set creator
                .Merge($"(author:{nameof(User)} {{ Id: ${nameof(authorId)} }})")
                .WithParam(nameof(authorId), authorId)
                .Merge("(author)-[:ASKED]->(q)")
                .With("q")
                // add links to tags
                .Unwind(question.Tags, "tag")
                .Merge($"(t:{nameof(Tag)} {{Id: tag}})")
                .Merge("(q)-[:ABOUT]->(t)")
                .With("q")
                // add list of interested users
                .Unwind(question.InterestedUsers?.Keys, "iu")
                .Merge($"(other:{nameof(User)} {{Id: iu}})")
                .Merge("(other)-[:WAIT_FOR_UPDATE]->(q)")
                .With("q");

            // add comments
            if (question.Comments is not null) foreach (var comment in question.Comments)
            {
                var paramComm = nameof(comment) + (counter++).ToString();
                var paramUser = nameof(comment) + (counter++).ToString();
                query = query
                    // add comment
                    .Merge($"(c:{nameof(Comment)} {{ Id: ${paramComm} }})")
                    .WithParam(paramComm, comment.Id)
                    // add link to question
                    .Merge("(c)-[:REFERS_TO]->(q)")
                    // add link to creator
                    .Merge($"(uC:{nameof(User)} {{ Id: ${paramUser} }})")
                    .WithParam(paramUser, comment.AuthorId)
                    .Merge("(uC)-[:COMMENTED]->(c)")
                    .With("q, c");
                // voti al commento
                if (comment.Votes is not null) foreach (var vote in comment.Votes)
                {
                    var vParams = new {
                        Useful = vote.Value.IsUseful,
                    };
                    paramUser = nameof(comment) + (counter++).ToString();
                    var paramVote = nameof(comment) + (counter++).ToString();
                    query = query
                        .Merge($"(uV:{nameof(User)} {{ Id: ${paramUser} }})")
                        .WithParam(paramUser, comment.AuthorId)
                        .Merge("(uV)-[r:VOTED]->(c)")
                        .Set($"r += ${paramVote}")
                        .WithParam(paramVote, vParams)
                        .With("q, c");
                }
                query = query.With("q");
            }
            query = query.With("q");

            // add answers
            if (question.Answers is not null) foreach (var answer in question.Answers)
            {
                var aParam = nameof(answer) + (counter++).ToString();
                var paramUser = nameof(answer) + (counter++).ToString();
                query = query
                    // add comment
                    .Merge($"(a:{nameof(Answer)} {{ Id: ${aParam} }})")
                    .WithParam(aParam, answer.Id)
                    // add link to question
                    .Merge("(a)-[:ANSWERED]->(q)")
                    // add link to creator
                    .Merge($"(u:{nameof(User)} {{ Id: ${paramUser} }})")
                    .WithParam(paramUser, answer.AuthorId)
                    .Merge("(u)-[:WROTE]->(a)")
                    .With("q, a");

                if (answer.ContainsSolution)
                {
                    query = query
                        .Set($"q.Solved = ${nameof(answer.ContainsSolution)}")
                        .WithParam(nameof(answer.ContainsSolution), true);
                }

                // voti alla risposta
                if (answer.Votes is not null) foreach (var vote in answer.Votes)
                {
                    var vParams = new
                    {
                        Useful = vote.Value.IsUseful,
                    };
                    paramUser = nameof(vote) + (counter++).ToString();
                    var paramVote = nameof(vote) + (counter++).ToString();
                    query = query
                        .Merge($"(uV:{nameof(User)} {{ Id: ${paramUser} }})")
                        .WithParam(paramUser, vote.Key)
                        .Merge("(uV)-[r:VOTED]->(a)")
                        .Set($"r += ${paramVote}")
                        .WithParam(paramVote, vParams)
                        .With("q, a");
                }
                // commenti alla risposta
                if (answer.Comments is not null) foreach (var comment in answer.Comments)
                {
                    var paramComm = nameof(comment) + (counter++).ToString();
                    paramUser = nameof(comment) + (counter++).ToString();
                    query = query
                        // add comment
                        .Merge($"(c:{nameof(Comment)} {{ Id: ${paramComm} }})")
                        .WithParam(paramComm, comment.Id)
                        // add link to question
                        .Merge("(c)-[:REFERS_TO]->(a)")
                        // add link to creator
                        .Merge($"(uC:{nameof(User)} {{ Id: ${paramUser} }})")
                        .WithParam(paramUser, comment.AuthorId)
                        .Merge("(uC)-[:COMMENTED]->(c)")
                        .With("q, a, c");

                    // voti al commento
                    if (comment.Votes is not null) foreach (var vote in comment.Votes)
                    {
                        var vParams = new {
                            Useful = vote.Value.IsUseful,
                        };
                        paramUser = nameof(comment) + (counter++).ToString();
                        var paramVote = nameof(comment) + (counter++).ToString();
                        query = query
                            .Merge($"(uV:{nameof(User)} {{ Id: ${paramUser} }})")
                            .WithParam(paramUser, comment.AuthorId)
                            .Merge("(uV)-[r:VOTED]->(c)")
                            .Set($"r += ${paramVote}")
                            .WithParam(paramVote, vParams)
                            .With("q, a, c");
                    }
                    query = query.With("q, a");
                }
                query = query.With("q");
            }
            query = query.Return<Question>("q");

            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }

        public async Task SyncTag(Tag tag)
        {
            var tagId = tag.Id;
            var creatorId = tag.AuthorId;
            var query = this.graphClient
                .Cypher
                // find user
                .Merge($"(u:{nameof(User)} {{Id: ${nameof(creatorId)}}})")
                // create tag
                .Merge($"(t:{nameof(Tag)} {{Id: ${nameof(tagId)}}})")
                // add link
                .Merge("(u)-[:CREATED]->(t)")
                .WithParams(new Dictionary<string, object>
                {
                    [nameof(creatorId)] = creatorId,
                    [nameof(tagId)] = tagId,
                });

            try
            {
                await query.ExecuteWithoutResultsAsync();
            }
            catch (Exception)
            {
                if (!this.ignoreConnectionFault)
                    throw;
            }
        }
    }
}
