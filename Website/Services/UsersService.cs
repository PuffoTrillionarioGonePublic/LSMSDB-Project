using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Services;
using Website.Models.Users;
using MongoDB.Driver;
using MongoDB.Bson;
using Website.Models.Statistics;
using System.Threading;

namespace Website.Services
{
    // This service will be used to
    // find user related data in the db
    public class UsersService : MongoSessionBase
    {
        private readonly IMongoCollection<User> users;
        private readonly Neo4jService neo4j;
        public UsersService(MongoService mongo, Neo4jService neo4j) : base(mongo)
        {
            this.users = mongo.UsersCollection;
            this.neo4j = neo4j;
        }

        public async Task<User> GetUserByEmail(string email) =>
            await this.users.Find(u => u.Email == email).FirstOrDefaultAsync();

        public async Task<User> GetUserByName(string name) =>
            await this.users.Find(u => u.Username == name).FirstOrDefaultAsync();

        public async Task<User> GetUserById(string id) =>
            await this.users.Find(u => u.Id == id).FirstOrDefaultAsync();

        public async Task<IEnumerable<User>> FindUsersByName(string username) =>
            await this.users.Find(u => u.Username.StartsWith(username)).Limit(20).ToListAsync();

        // get all user ids - only for debugging purposes
        public async Task<IEnumerable<string>> GetAllUserIds()
        {
            var users = await this.users.Find(u => true)
                .Project(new ProjectionDefinitionBuilder<User>().Include(u => u.Id))
                .As<User>()
                .ToListAsync();
            var ans = new string[users.Count];
            var i = 0;
            foreach (var u in users)
            {
                ans[i++] = u.Id;
            }
            return ans;
        }


        // To add new users to the db
        public async Task AddNewUser(User user)
        {
            if (user.Registered == new DateTime())
            {
                user.Registered = DateTime.UtcNow;
            }
            await this.users.InsertOneAsync(user);
            await this.neo4j.AddUser(user);
        }

        public async Task<UserVoteStats> CountVotesReceivedByUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }
            return await this.neo4j.CountVotesReceivedByUser(userId);
        }

        /**
         * Ban user
         */
        public async Task BanUser(string victimId, string adminId, TimeSpan? timeSpan, string reason)
        {
            var banId = ObjectId.GenerateNewId().ToString();
            var now = DateTime.UtcNow;
            var info = new BanInfo
            {
                BanId = banId,
                BannerAdmin = adminId,
                BanStart = now,
                BanEnd = timeSpan is null ? null : now + timeSpan,
                Reason = reason,
            };
            var session = await this.GetSessionHandle();

            Func<IClientSessionHandle, CancellationToken, Task<bool>> callbackAsync = async (IClientSessionHandle session, CancellationToken ct) =>
            {
                // common user
                var queryU = new ExpressionFilterDefinition<User>(u => u.Id == victimId && !u.IsAdmin);
                var upDefU = new UpdateDefinitionBuilder<User>().Push(u => u.BanList, info);
                var victim = await (session is not null
                    ? this.users.FindOneAndUpdateAsync(session, queryU, upDefU, null, ct)
                    : this.users.FindOneAndUpdateAsync(queryU, upDefU, null, ct));
                if (victim is null)
                    return false;
                var bannedUser = new BannedUser
                {
                    BanId = banId,
                    UserId = victimId,
                    Username = victim.Username,
                    Email = victim.Email,
                    Registered = victim.Registered,
                    BanStart = info.BanStart,
                    BanEnd = info.BanEnd,
                    Reason = reason,
                };
                // admin
                var queryA = new ExpressionFilterDefinition<User>(u => u.Id == adminId);
                var upDefA = new UpdateDefinitionBuilder<User>().Push(u => u.BannedUsers, bannedUser);
                await (session is not null
                    ? this.users.UpdateOneAsync(session, queryA, upDefA, null, ct)
                    : this.users.UpdateOneAsync(queryA, upDefA, null, ct));
                return true;
            };
            await (session is not null
                ? session.WithTransactionAsync(callbackAsync)
                : callbackAsync(null, CancellationToken.None));
        }

        /**
         * This methor perform an aggregation that count the number
         * of user who created an account grouped by registration day
         */
        public async Task<IDictionary<string, int>> GetSignInStats()
        {
            // Why it works:
            //  Doc:    https://docs.mongodb.com/manual/reference/operator/aggregation/group/
            // [{$group:
            //     {
            //     _id: { $dateToString: { format: "%Y-%m-%d", date: "$Registered" } },
            //     count: { $sum: 1 }
            //     }
            // }]
            var pipeline = new BsonDocument[]
            {
                new BsonDocument("$group",
                new BsonDocument
                    {
                        { "_id",
                new BsonDocument("$dateToString",
                new BsonDocument
                            {
                                { "format", "%Y-%m-%d" },
                                { "date", "$Registered" }
                            }) },
                        { "count",
                new BsonDocument("$sum", 1) }
                    })
            };
            var res = await this.users.Aggregate<BsonDocument>(pipeline).ToListAsync();
            var ans = new Dictionary<string, int>();
            foreach (var doc in res)
            {
                ans[doc["_id"].ToString()] = doc["count"].ToInt32();
            }

            return ans;
        }
    }
}
