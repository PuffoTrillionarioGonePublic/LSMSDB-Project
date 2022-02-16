using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Settings
{
    public class MongoDatabaseSettings : IMongoDatabaseSettings
    {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string UsersCollection { get; set; }
        public string QuestionsCollection { get; set; }
        public string PostsCollection { get; set; }
        public string TagsCollection { get; set; }
    }

    public interface IMongoDatabaseSettings
    {
        string ConnectionString { get; set; }
        string DatabaseName { get; set; }
        string UsersCollection { get; set; }
        string QuestionsCollection { get; set; }
        string PostsCollection { get; set; }
        string TagsCollection { get; set; }
    }
}
