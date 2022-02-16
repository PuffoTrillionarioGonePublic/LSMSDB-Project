using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Statistics
{
    public class TagStatics
    {
        public class OtherTag
        {
            public string TagName { get; set; }
            public long CommonUsages { get; set; }
        }
        public string TagName { get; set; }
        public long TotalUsages { get; set; }

        public IEnumerable<OtherTag> OtherTags { get; set; }
    }
}
