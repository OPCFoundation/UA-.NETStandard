using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Activity Helper Class.
    /// </summary>
    public static class ActivityHelper
    {
        /// <summary>
        /// Activity Source
        /// </summary>
        public static ActivitySource ActivitySrc { get; } = new ActivitySource("Opc.Ua");
    }
}
