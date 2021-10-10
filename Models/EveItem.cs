using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OreCalc.Models
{
    class EveItem
    {
        public string Name;
        public string Quantity;

        public EveItem(string name, string quantity)
        {
            Name = name;
            Quantity = quantity;
        }
    }
}
