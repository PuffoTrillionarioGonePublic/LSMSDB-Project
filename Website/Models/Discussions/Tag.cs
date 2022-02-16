using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Website.Models.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Discussions
{
    [BsonIgnoreExtraElements]
    public class Tag
    {
        // Just the tag Id, that is the NAME itself
        [BsonId]
        public string Id { get; set; }
        public string Description { get; set; }
        // How many Questions use it?
        public int CountQuestions { get; set; }
        public int CountFollowers { get; set; }
        // ////////////////////////////////////////////
        // The following properties should be initialised
        // only after tags creation
        // ////////////////////////////////////////////
        // Identifiers of the first user using the tag
        public string AuthorId { get; set; }
        public string AuthorName { get; set; }
        // When does the tag has been defined?
        public DateTime Defined { get; set; }
    }
}
