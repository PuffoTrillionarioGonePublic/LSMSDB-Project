using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Settings
{
    public class Neo4jSettings : INeo4jSettings
    {
        public string ConnectionString { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public interface INeo4jSettings
    {
        string ConnectionString { get; set; }
        string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
