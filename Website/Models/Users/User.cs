using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Users
{
    /**
     * This class is used to keep data about
     * user banned by an admin in the admin
     * object.
     */
    [BsonIgnoreExtraElements]
    public class BannedUser
    {
        // Id if the ban
        [BsonRepresentation(BsonType.ObjectId)]
        public string BanId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public DateTime Registered { get; set; }
        // The user will be unable to interact with
        // the website untill the ban will be expired.
        // If BanEnd is null the user will be
        // considered as definitely banned and will
        // not be able to interact with the website
        // anymore
        public DateTime BanStart { get; set; }
        public DateTime? BanEnd { get; set; }

        // Why was the user banned?
        public string Reason { get; set; }
    }

    /**
     * This class is used to store info about bans
     * in the banned user's object
     */
    [BsonIgnoreExtraElements]
    public class BanInfo
    {
        // Id identifying the ban action
        [BsonRepresentation(BsonType.ObjectId)]
        public string BanId { get; set; }
        // Id of the admin who banned the user
        [BsonRepresentation(BsonType.ObjectId)]
        public string BannerAdmin { get; set; }

        // Same of before
        public DateTime BanStart { get; set; }
        public DateTime? BanEnd { get; set; }
        
        // Why was the user banned?
        public string Reason { get; set; }
    }

    /**
     * This class represents the unread updates
     * on an interesting question.
     *
     * Maybe we could save only the counter and
     * retrieve the other information by the
     * graph db.
     */
    [BsonIgnoreExtraElements]
    public class QuestionUpdates
    {
        // Info about the question
        // Storing the Question.Id is unnecessary
        // because it is stored as the property
        // name in the containing object
        [BsonIgnore]
        public string QuestionId { get; set; }
        public string Title { get; set; }
        public DateTime Created { get; set; }

        // Info abouth the Author
        [BsonRepresentation(BsonType.ObjectId)]
        public string AuthorId { get; set; }
        public string AuthorName { get; set; }
        public IEnumerable<string> Tags { get; set; }

        // Number of unread updates
        // Null value are ignored to avoid
        // race condidions
        [BsonIgnoreIfNull] 
        public int? CountUpdates { get; set; }
        // The "Solved" update is special, it
        // is better to not to store it in the
        // counter
        [BsonIgnoreIfNull]
        public bool? Solved { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public DateTime Registered { get; set; } = DateTime.UtcNow;
        public string Email { get; set; }
        // In the first version of the project
        // passwords will be saved in plai text
        // this will be canged in the future
        public string Password { get; set; }
        public string Username { get; set; }
        
        // The following two dictionaries are used
        // to mantain statistics about tags created
        // and followed by the user
        [BsonIgnoreIfNull]
        public IEnumerable<string> CreatedTags { get; set; }
        [BsonIgnoreIfNull]
        public IEnumerable<string> FollowedTags { get; set; }

        // Being an admin is a special status and, theoretically,
        // imply user owns all capabilities (not implemented for
        // now)
        //
        // ADMINS CANNOT BE BANNED!
        public bool IsAdmin { get; set; }

        // List of ban gotten by the user
        // NOT AVAILABLE FOR ADMINS!
        [BsonIgnoreIfNull]
        public IEnumerable<BanInfo> BanList { get; set; }

        // List of users banned by the current user (which must be an admin)
        // ONLY AVAILABLE FOR ADMINS!
        [BsonIgnoreIfNull]
        public IEnumerable<BannedUser> BannedUsers { get; set; }

        // Check if the user now is banned and, consequently,
        // cannot log in
        public bool IsCurrentlyBanned()
        {
            var now = DateTime.UtcNow;
            return this.BanList?.Any(bi => bi.BanStart <= now && bi.BanEnd is null || now <= bi.BanEnd) ?? false;
        }

        public BanInfo GetCurrentBan()
        {
            var now = DateTime.UtcNow;
            return this.BanList?.FirstOrDefault(bi => bi.BanStart <= now && bi.BanEnd is null || now <= bi.BanEnd);
        }

        ICollection<string> Capabilities { get; set; }
        [BsonIgnoreIfNull]
        public IDictionary<string, QuestionUpdates> Updates { get; set; }

        public bool IsTagCreator(string tag)
        {
            return this.CreatedTags?.Contains(tag) ?? false;
        }

        public bool IsTagFollower(string tag)
        {
            return this.FollowedTags?.Contains(tag) ?? false;
        }

        public int CreatedTagsCount()
        {
            return this.CreatedTags?.Count() ?? 0;
        }

        public int FollowedTagsCount()
        {
            return this.FollowedTags?.Count() ?? 0;
        }
    }
}
