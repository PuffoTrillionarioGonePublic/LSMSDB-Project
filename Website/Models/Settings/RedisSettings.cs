using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Settings
{
    public class RedisSettings : IRedisSettings
    {
        public string InstanceName { get; set; }
        public string ConnectionString { get; set; }
    }

    public interface IRedisSettings
    {
        string InstanceName { get; set; }
        string ConnectionString { get; set; }
    }
}
