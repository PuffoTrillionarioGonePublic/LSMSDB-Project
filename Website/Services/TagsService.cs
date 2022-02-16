using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using Website.Models.Discussions;
using Website.Models.Users;
using Website.Models.Statistics;
using System.Threading;

namespace Website.Services
{
    public class TagsService : MongoSessionBase
    {
        private readonly IMongoCollection<User> _users;
        private readonly IMongoCollection<Models.Discussions.Tag> _tags;
        private readonly Neo4jService neo4j;
        private readonly UsersService usersService;

        public TagsService(MongoService mongoService, Neo4jService neo4jService, UsersService usersService)
        : base(mongoService)
        {
            this._users = mongoService.UsersCollection;
            this._tags = mongoService.TagsCollection;
            this.neo4j = neo4jService;
            this.usersService = usersService;
        }

        public async Task<Models.Discussions.Tag> GetTag(string tag) =>
            await this._tags.Find(t => t.Id == tag).FirstOrDefaultAsync();

        public async Task<IEnumerable<string>> GetAllTagIds()
        {
            var tags = await this._tags.Find(t => true)
                .Project(new ProjectionDefinitionBuilder<Models.Discussions.Tag>().Include(t => t.Id))
                .As<Models.Discussions.Tag>()
                .ToListAsync();
            var ans = new string[tags.Count];
            var i = 0;
            foreach (var t in tags)
            {
                ans[i++] = t.Id;
            }
            return ans;
        }

        public async Task FollowTag(string userId, string tag)
        {
            tag = tag.Trim().ToUpper();
            var session = await this.GetSessionHandle();

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) =>
            {
                // Tag has one more follower
                var upDefT = new UpdateDefinitionBuilder<Models.Discussions.Tag>().Inc(t => t.CountFollowers, 1);
                // so can update Users collection
                var upDefU = new UpdateDefinitionBuilder<User>().AddToSet(t => t.FollowedTags, tag);
                var queryU = new ExpressionFilterDefinition<User>(u => u.Id == userId && !u.FollowedTags.Contains(tag));
                var queryT = new ExpressionFilterDefinition<Models.Discussions.Tag>(t => t.Id == tag);
                // session is not null
                var user = session is not null
                    ? await this._users.FindOneAndUpdateAsync(session, queryU, upDefU, null, ct)
                    : await this._users.FindOneAndUpdateAsync(queryU, upDefU, null, ct);
                if (user is null)
                    return false;
                // try to increment counter
                var t = await (session is not null
                    ? this._tags.FindOneAndUpdateAsync(session, queryT, upDefT, null, ct)
                    : this._tags.FindOneAndUpdateAsync(queryT, upDefT, null, ct));
                // if tag wan noexistent
                if (t is null)
                {
                    await session.AbortTransactionAsync(ct);
                    return false;
                }
                return true;
            };

            if (session is not null
                ? await session.WithTransactionAsync(callbackAsync)
                : await callbackAsync(null, CancellationToken.None))
                await this.neo4j.FollowTag(userId, tag);
        }

        public async Task UnfollowTag(string userId, string tag)
        {
            tag = tag.Trim().ToUpper();
            var session = await this.GetSessionHandle();

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) =>
            {
                var upDefU = new UpdateDefinitionBuilder<User>().Pull(t => t.FollowedTags, tag);
                var upDefT = new UpdateDefinitionBuilder<Models.Discussions.Tag>().Inc(t => t.CountFollowers, -1);
                var queryU = new ExpressionFilterDefinition<User>(u => u.Id == userId && u.FollowedTags.Contains(tag));
                var queryT = new ExpressionFilterDefinition<Models.Discussions.Tag>(t => t.Id == tag);
                // remove the follower
                var res = await (session is not null
                    ? this._users.UpdateOneAsync(session, queryU, upDefU, null, ct)
                    : this._users.UpdateOneAsync(queryU, upDefU, null, ct));
                // if the user was effectively following the tag
                if (!res.IsModifiedCountAvailable || res.ModifiedCount == 0)
                    return false;
                // decrement the counter
                await (session is not null
                    ? this._tags.FindOneAndUpdateAsync(session, queryT, upDefT, null, ct)
                    : this._tags.FindOneAndUpdateAsync(queryT, upDefT, null, ct));
                return true;
            };

            // then can update Neo4j db
            if (session is not null
                ? await session.WithTransactionAsync(callbackAsync)
                : await callbackAsync(null, CancellationToken.None))
                await this.neo4j.UnfollowTag(userId, tag);
        }

        public async Task<bool> IsTagFollower(string userId, string tag)
        {
            var user = await this.usersService.GetUserById(userId);
            if (user is null)
            {
                return false;
            }
            return user.IsTagFollower(tag);
        }

        public async Task<bool> IsTagCreator(string userId, string tag)
        {
            var user = await this.usersService.GetUserById(userId);
            if (user is null)
            {
                return false;
            }
            return user.IsTagCreator(tag);
        }

        public async Task<long> GetTagUsages(string tag)
        {
            var t = await this.GetTag(tag);
            return t?.CountQuestions ?? 0;
        }

        public async Task<IDictionary<string, long>> GetTagsUsages(IEnumerable<string> tags)
        {
            var ans = new Dictionary<string, long>();
            foreach (var tag in tags)
            {
                ans[tag] = await this.GetTagUsages(tag);
            }

            return ans;
        }

        public async Task<IEnumerable<TagStatics>> GetTagStatistics(IEnumerable<string> taglist)
        {
            var tagset = taglist.ToHashSet();
            var tagStatics = new List<TagStatics>();
            foreach (var tag in taglist)
            {
                var tagCoUsages = await this.neo4j.GetTagCousages(tag);
                var stats = new TagStatics
                {
                    TagName = tag,
                    OtherTags = tagCoUsages.Select(ot => {
                        return new TagStatics.OtherTag
                        {
                            TagName = ot.Key,
                            CommonUsages = ot.Value,
                        };
                    }).OrderByDescending(ot => ot.CommonUsages),
                };
                tagStatics.Add(stats);
            }
            // Get total usages for each tags
            var totals = await this.GetTagsUsages(tagset);
            // set totals
            tagStatics.ForEach(ts =>
            {
                ts.TotalUsages = totals[ts.TagName];
            });
            return tagStatics;
        }

        /**
         * 
         */
        public async Task<UserTagStats> FollowedTagsStats()
        {
            var ans = new UserTagStats
            {
                TagsPerUser = new SortedDictionary<int, int>(),
                UsersPerTag = new SortedDictionary<int, int>(),
            };
            // Histograms of distribution of # of
            // tags followed by each user
            //
            // Mongo pipeline
            //  [{$project:
            //  {
            //  CountFollowers:
            //  {$ifNull:[
            //          '$CountFollowers',
            //          0
            //          ]
            //  }
            //  }
            //  },{$group:
            //      {
            //      _id: "$CountFollowers",
            //      count: { $count: { } }
            //      }
            //  }]
            //
            // C# format
            var tagsPerUserPipeline = new BsonDocument[]
            {
                new BsonDocument("$project", new BsonDocument(
                    "CountFollowers", new BsonDocument("$ifNull", new BsonArray
                    {
                        "$CountFollowers",
                        0
                    }))
                ),
                new BsonDocument("$group", new BsonDocument {
                            { "_id", "$CountFollowers" },
                            { "count", new BsonDocument("$count", new BsonDocument()) },
                })
            };
            // Histograms of distribution of # of
            // users following each tag
            //
            // mongo pipeline
            //  [{$project:
            //  {
            //  followedTagsCount:
            //  {$cond: {
            //      if: {$isArray:[ "$FollowedTags" ]},
            //      then: {$size: "$FollowedTags"},
            //      else: 0 }
            //  }
            //  }
            //  }, {$group:
            //      {
            //      _id: "$followedTagsCount",
            //      count: { $count: { } }
            //      }
            //  }]
            //
            // C#
            var usersPerTagPipeline = new BsonDocument[]
            {
                new BsonDocument("$project",
                    new BsonDocument("followedTagsCount",
                        new BsonDocument("$cond", new BsonDocument {
                            { "if", new BsonDocument("$isArray", new BsonArray { "$FollowedTags" }) },
                            { "then", new BsonDocument("$size", "$FollowedTags") },
                            { "else", 0 }
                        }))),
                    new BsonDocument("$group", new BsonDocument {
                            { "_id", "$followedTagsCount" },
                            { "count", new BsonDocument("$count", new BsonDocument()) }
                        })
            };

            var tagsPerUserTask = this._tags.Aggregate<BsonDocument>(tagsPerUserPipeline).ToListAsync();
            var usersPerTagHisto = await this._users.Aggregate<BsonDocument>(usersPerTagPipeline).ToListAsync();
            var tagsPerUserHisto = await tagsPerUserTask;

            foreach (var doc in usersPerTagHisto)
            {
                ans.UsersPerTag[doc["_id"].ToInt32()] = doc["count"].ToInt32();
            }
            foreach (var doc in tagsPerUserHisto)
            {
                ans.TagsPerUser[doc["_id"].ToInt32()] = doc["count"].ToInt32();
            }

            return ans;
        }
    }
}
