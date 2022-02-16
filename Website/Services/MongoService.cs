using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Models;
using Website.Models.Settings;
using Website.Models.Users;
using Website.Models.Discussions;
using MongoDB.Driver;

namespace Website.Services
{
    public class MongoService
    {
        public MongoService(IMongoDatabaseSettings settings)
        {
            var client = new MongoClient(settings.ConnectionString);
            var db = client.GetDatabase(settings.DatabaseName);
            var users = db.GetCollection<User>(settings.UsersCollection);
            var questions = db.GetCollection<Question>(settings.QuestionsCollection);
            var posts = db.GetCollection<Answer>(settings.PostsCollection);
            var tags = db.GetCollection<Models.Discussions.Tag>(settings.TagsCollection);

            this.Client = client;
            this.Database = db;
            this.UsersCollection = users;
            this.QuestionsCollection = questions;
            this.PostsCollection = posts;
            this.TagsCollection = tags;

            this.CheckTransactionSupport();
            this.EnsureIndexes();
        }

        private bool are_transactions_supported;
        private void CheckTransactionSupport()
        {
            // Check if the connection support transactions.
            // Currently mongo supports transactions only if
            // we are connected to a replica set
            this.are_transactions_supported = !string.IsNullOrWhiteSpace(this.Client.Settings.ReplicaSetName);
        }

        /**
         * Get an object to handle mongo session
         */
        public async Task<IClientSessionHandle> GetSessionHandle() =>
            this.are_transactions_supported ? await this.Client.StartSessionAsync() : null;

        private void EnsureIndexes()
        {
            // Ensure uniqueness of username and email address
            this.UsersCollection.Indexes.CreateMany(
                new CreateIndexModel<User>[]
                {
                new CreateIndexModel<User>(new IndexKeysDefinitionBuilder<User>().Ascending(u => u.Email), new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<User>(new IndexKeysDefinitionBuilder<User>().Ascending(u => u.Username), new CreateIndexOptions { Unique = true }),
                }
                );
            // build search indexes on Question collection
            this.QuestionsCollection.Indexes.CreateMany(
                new CreateIndexModel<Question>[]
                {
                // Create index to search questions by tags
                new CreateIndexModel<Question>(new IndexKeysDefinitionBuilder<Question>().Combine(new IndexKeysDefinitionBuilder<Question>().Ascending(u => u.Tags).Descending(u => u.Created))),
                // Create full text index
                new CreateIndexModel<Question>(new IndexKeysDefinitionBuilder<Question>().Text(u => u.Title).Text(u => u.Text)),
                }
                );
        }

        public IMongoClient Client { get; }
        public IMongoDatabase Database { get; }
        public IMongoCollection<User> UsersCollection { get; }
        public IMongoCollection<Question> QuestionsCollection { get; }
        public IMongoCollection<Answer> PostsCollection { get; }
        public IMongoCollection<Models.Discussions.Tag> TagsCollection { get; }
    }
}
