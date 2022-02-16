using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;

namespace Website.Services
{
    /**
     * This special service is intended to handle data resyncronisation
     * between the main db of the application (mongo) and all secondary
     * databases
     */
    public class SynchronizerService
    {
        private readonly MongoService mongo;
        private readonly Neo4jService neo4j;
        public SynchronizerService(MongoService mongo, Neo4jService neo4j)
        {
            this.mongo = mongo;
            this.neo4j = neo4j;
        }

        public async Task ClearNeo4j()
        {
            await this.neo4j.ClearDatabase();
        }

        public async Task SynchronizeUsers()
        {
            var cursor = await this.mongo.UsersCollection.Find(u => true).ToCursorAsync();
            await cursor.ForEachAsync(async u => await this.neo4j.SyncUser(u));
        }

        public async Task SynchronizeQuestions()
        {
            var cursor = await this.mongo.QuestionsCollection.Find(q => true).ToCursorAsync();
            await cursor.ForEachAsync(async q => await this.neo4j.SyncQuestion(q));
        }

        public async Task SynchronizeTags()
        {
            var cursor = await this.mongo.TagsCollection.Find(t => true).ToCursorAsync();
            await cursor.ForEachAsync(async t => await this.neo4j.SyncTag(t));
        }
    }
}
