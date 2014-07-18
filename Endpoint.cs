using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LocalHost
{
    public struct Endpoint
    {
        public string Name { set; get; }
        public string Address { set; get; }
        public string Binding { set; get; }
        public string Contract { set; get; }
    }
}
