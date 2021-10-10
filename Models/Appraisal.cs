using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Autopraisal.Models
{
    class Appraisal
    {
        public string market = "jita";
        public string raw_textarea = "";

        public Appraisal(string market, string raw_textarea)
        {
            this.market = market;
            this.raw_textarea = raw_textarea;
        }
    }
}
