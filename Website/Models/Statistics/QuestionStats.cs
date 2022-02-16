using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Statistics
{
    public class QuestionStats
    {
        // Users stats
        public double AvgUsers { get; set; }
        public double StdDevUsers { get; set; }
        // Answers stats
        public double AvgAnswers { get; set; }
        public double StdDevAnswers { get; set; }
        // Comments stats
        public double AvgComments { get; set; }
        public double StdDevComments { get; set; }
        // Comments to answers stats
        public double AvgCommentsToAnswers { get; set; }
        public double StdDevCommentsToAnswers { get; set; }
    }
}
