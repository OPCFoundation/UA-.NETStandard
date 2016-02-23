/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
