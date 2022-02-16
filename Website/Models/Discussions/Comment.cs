using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Discussions
{
    /**
     * Comments should be used only to criticize the form of a post,
     * not to give answers.
     */
    [BsonIgnoreExtraElements]
    public class Comment
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        // Property added to simplify debug API
        [BsonIgnore]
        public string QuestionId { get; set; }
        // Property added to simplify debug API
        [BsonIgnore]
        public string AnswerId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string AuthorId { get; set; }
        [BsonIgnore]
        public string AuthorName { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        [BsonIgnoreIfNull]
        public DateTime? LastEdited { get; set; }
        public string Text { get; set; }

        // Used to mark answers hidden from admins
        [BsonIgnoreIfNull]
        public RemovedPostInfo Removed { get; set; }

        [BsonIgnoreIfNull]
        public IDictionary<string, Vote> Votes { get; set; }
        public void AfterDeserialisation(IDictionary<string, string> Users)
        {
            this.AuthorName = Users[this.AuthorId];
            if (this.Votes is not null) foreach (var V in this.Votes)
            {
                V.Value.AuthorName = Users[V.Key];
            }
        }

        public void BeforeSerialisation(IDictionary<string, string> Users)
        {
            if (!string.IsNullOrWhiteSpace(this.AuthorName))
            {
                Users[this.AuthorId] = this.AuthorName;
            }
            if (this.Votes is not null) foreach (var V in this.Votes)
            {
                if (!string.IsNullOrWhiteSpace(V.Value.AuthorName))
                {
                    Users[V.Key] = V.Value.AuthorName;
                }
            }
        }
    }
}
