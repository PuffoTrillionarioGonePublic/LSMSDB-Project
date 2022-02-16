using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Statistics
{
    /**
     * This class is used to hold statistics
     * about the votes received by a user
     */
    public class UserVoteStats
    {
        // Votes received on answers
        public int ALike { get; set; }
        public int ADislike { get; set; }
        // Votes received on comments
        public int CLike { get; set; }
        public int CDislike { get; set; }
    }
}
