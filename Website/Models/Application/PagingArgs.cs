using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Models.Application
{
    /**
     * This class is used 
     */
    public class PagingArgs
    {
        public string Area { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }

        // number of current page
        public int CurrentPage { get; set; }
        // is current page full? if yes next
        // page might exist
        public bool CurrentFull { get; set; }

        // Default name of the parameter used by the
        // action to identify the current page
        public string PageFieldName { get; set; } = "page";

        // arguments passed to the action via the asp-all-route-data
        // asp.net tag helper
        public IDictionary<string,string> Args { get; set; }
    }
}
