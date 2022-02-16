using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Discussions
{
    [BsonIgnoreExtraElements]
    public class Vote
    {
        // User Id will be stored as parent field name, no need for memory waste in db
        [BsonIgnore]
        public String AuthorId { get; set; }
        [BsonIgnore]
        public string AuthorName { get; set; }
        public bool IsUseful { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class Answer
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string QuestionId { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        [BsonIgnoreIfNull]
        public DateTime? LastEdited { get; set; }
        [BsonRepresentation(BsonType.String)]
        public string AuthorId { get; set; }
        [BsonIgnore]
        public string AuthorName { get; set; }
        public string Text { get; set; }

        // Used to mark answers hidden from admins
        [BsonIgnoreIfNull]
        public RemovedPostInfo Removed { get; set; }

        [BsonIgnoreIfNull]
        public IDictionary<string, Vote> Votes { get; set; }
        [BsonIgnoreIfNull]
        public ICollection<Comment> Comments { get; set; }

        /**
         * This property is used only by the application
         * to recognize the answer which the asker designed
         * as fundamental to solve the problem
         */
        [BsonIgnore]
        public bool ContainsSolution { get; set; }

        // Calculate the score of and answer
        // Useful is +1, !Useful is -1
        public int CalculateAnsewrScore()
        {
            return Votes is null ? 0 : 2 * Votes.Values.Count(v => v.IsUseful) - Votes.Count();
        }

        public void AfterDeserialisation(IDictionary<string, string> Users)
        {
            this.AuthorName = Users[this.AuthorId];
            if (this.Comments is not null) foreach (var C in this.Comments)
            {
                C.AfterDeserialisation(Users);
            }
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
            if (this.Comments is not null) foreach (var C in this.Comments)
            {
                C.BeforeSerialisation(Users);
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
