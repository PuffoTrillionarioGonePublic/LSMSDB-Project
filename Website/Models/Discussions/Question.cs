using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Discussions
{
    /**
     * Sometimes moderators (admins) need to
     * hide the content of a question, an answer
     * or a comment.
     *
     * This class is used to keep info about the
     * elimination of a content.
     */
    [BsonIgnoreExtraElements]
    public class RemovedPostInfo
    {
        // Who removed the object?
        [BsonRepresentation(BsonType.ObjectId)]
        public string ModeratorId { get; set; }
        // When was it removed?
        public DateTime DateTime { get; set; }
        // Why was is removed?
        public string Reason { get; set; }
    }

    /**
     * This class type mantains data about interested users
     */
    [BsonIgnoreExtraElements]
    public class InterestedUser
    {
        // User Id will be stored as parent field name, no need for memory waste in db
        [BsonIgnore]
        public String UserId { get; set; }

        public String UserName { get; set; }
        public DateTime DateTime { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class Question
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        [BsonIgnoreIfNull]
        public DateTime? LastEdited { get; set; }
        [BsonRepresentation(BsonType.String)]
        public string AuthorId { get; set; }
        [BsonIgnore]
        public string AuthorName { get; set; }

        // Used to mark questions hidden from admins
        [BsonIgnoreIfNull]
        public RemovedPostInfo Removed { get; set; }

        /**
         * This dictionary associates the users' Ids with
         * their names.
         * The key are the ids, the values are the names.
         * Properties like AuthorName will no more be stored
         * in the objects in the arrays but only the Ids will.
         * When the object will be deserialised it will be
         * necessary to retrieve the names from thi dictionary
         * to populate the properties
         */
        [BsonIgnoreIfNull]
        public IDictionary<string, string> Users { get; set; }

        public string Title { get; set; }
        [BsonIgnoreIfNull]
        public IEnumerable<string> Tags { get; set; }
        public string Text { get; set; }
        // Has the initial problem been resolved?
        public bool Solved { get; set; }

        /**
         * Identifier of the answer containing the solution
         * to the problem defined in the question
         */
        [BsonRepresentation(BsonType.ObjectId)]
        public string SolutionAnswerId { get; set; }

        [BsonIgnoreIfNull]
        public ICollection<Comment> Comments { get; set; }

        // Who whant to be notified when updates arrives?
        // Dictionaries allow more options than simple set
        [BsonIgnoreIfNull]
        public IDictionary<string, InterestedUser> InterestedUsers { get; set; }

        // Used only in views to display how many unread updates are
        // available for the current user
        [BsonIgnore]
        public int? UnreadUpdates { get; set; }


        // All Answers will be nested in the same object of the
        // question
        [BsonIgnoreIfNull]
        public ICollection<Answer> Answers { get; set; }

        /**
         * Use Users Dictionary to init *Name properties.
         * Should be used only inside the QuestionsService
         * to mantain the code clean.
         */
        public void AfterDeserialisation()
        {
            this.AuthorName = this.Users[this.AuthorId];
            if (this.Answers is not null) foreach (var A in this.Answers)
            {
                A.AfterDeserialisation(this.Users);
                if (A.Id == this.SolutionAnswerId)
                {
                    A.ContainsSolution = true;
                }
            }
            if (this.Comments is not null) foreach (var C in this.Comments)
            {
                C.AfterDeserialisation(this.Users);
            }
        }

        public void BeforeSerialisation()
        {
            this.Users ??= new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(this.AuthorName))
            {
                this.Users[this.AuthorId] = this.AuthorName;
            }
            if (this.Answers is not null) foreach (var A in this.Answers)
            {
                if (A.ContainsSolution)
                {
                    this.SolutionAnswerId = A.Id;
                }
                A.BeforeSerialisation(this.Users);
            }
            if (this.Comments is not null) foreach (var C in this.Comments)
            {
                C.BeforeSerialisation(this.Users);
            }
        }
    }
}
