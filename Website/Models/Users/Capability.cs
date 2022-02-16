using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Website.Models.Users
{
    public class Capability
    {
        public string Id { get; set; }
        // When does the capability has been defined?
        public DateTime Defined { get; set; }
        // What does it means?
        public string Description { get; set; }
    }
}
