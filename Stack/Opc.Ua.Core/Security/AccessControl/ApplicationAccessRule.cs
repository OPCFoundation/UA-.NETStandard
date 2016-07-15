/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// An access rule for an application.
    /// </summary>
    public partial class ApplicationAccessRule
    {
        /// Gets the application access rules implied by the access rights to the file.
        /// </summary>
        public static IList<ApplicationAccessRule> GetAccessRules(String filePath)
        {
            // combine the access rules into a set of abstract application rules.
            List<ApplicationAccessRule> accessRules = new List<ApplicationAccessRule>();

            return accessRules;
        }

        /// <summary>
        /// Gets the application access rules implied by the access rights to the file.
        /// </summary>
        public static void SetAccessRules(String filePath, IList<ApplicationAccessRule> accessRules, bool replaceExisting)
        {
        }
    }
}
