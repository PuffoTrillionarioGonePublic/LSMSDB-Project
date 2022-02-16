using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Statistics
{
    /**
     * This clas is intended to store some
     * statistics about the distribuition
     * of the number of tags followed by each
     * user and the number of users following
     * each tag.
     * The statistics are hold as histograms
     */
    public class UserTagStats
    {
        public IDictionary<int, int> TagsPerUser { get; set; }
        public IDictionary<int, int> UsersPerTag { get; set; }
    }
}
