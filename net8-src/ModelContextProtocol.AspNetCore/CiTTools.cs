using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModelContextProtocol.AspNetCore
{
    internal class CiTTools
    {
        internal class One<T>
        {
            public T Value { get; set; }
            public One(T value)
            {
                Value = value;
            }
        }
    }
}
