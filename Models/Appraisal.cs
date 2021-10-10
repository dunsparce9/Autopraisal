using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Autopraisal.Models
{
    class Appraisal
    {
        public long created { get; set; }
        public string kind { get; set; }
        public string market_name { get; set; }
        public AppraisalTotals totals { get; set; }
    }

    class AppraisalTotals
    {
        public decimal buy { get; set; }
        public decimal sell { get; set; }
    }

}
