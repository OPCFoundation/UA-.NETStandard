/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Legacy content filter extensions
    /// </summary>
    public partial class ContentFilter
    {
        /// <summary>
        /// Set the default StringComparison to use when evaluating the Equals operator.
        /// This property is meant to be set as a config setting and not set / reset on
        /// a per context basis, to ensure consistency
        /// </summary>
        public static StringComparison EqualsOperatorDefaultStringComparison { get; set; }
            = StringComparison.Ordinal;
    }
}
