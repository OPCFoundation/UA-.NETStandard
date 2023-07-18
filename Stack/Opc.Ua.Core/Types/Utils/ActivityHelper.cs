using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Defines Activity Source
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// Activity Source
        /// </summary>
        public static ActivitySource ActivitySrc { get; } = new ActivitySource("Opc.Ua.Client-ActivitySource");
    }
}
