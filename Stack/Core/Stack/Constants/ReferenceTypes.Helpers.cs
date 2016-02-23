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
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class ReferenceTypes
    {
        #region Static Helper Functions
        /// <summary>
        /// Returns the browse name for the attribute.
        /// </summary>
        public static string GetBrowseName(uint identifier)
        {
            FieldInfo[] fields = typeof(ReferenceTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (identifier == (uint)field.GetValue(typeof(ReferenceTypes)))
                {
                    return field.Name;
                }
            }

            return System.String.Empty;
        }

        /// <summary>
        /// Returns the browse names for all attributes.
        /// </summary>
        public static string[] GetBrowseNames()
        {
            FieldInfo[] fields = typeof(ReferenceTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

            int ii = 0;

            string[] names = new string[fields.Length];

            foreach (FieldInfo field in fields)
            {
                names[ii++] = field.Name;
            }

            return names;
        }

        /// <summary>
        /// Returns the id for the attribute with the specified browse name.
        /// </summary>
        public static uint GetIdentifier(string browseName)
        {
            FieldInfo[] fields = typeof(ReferenceTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (field.Name == browseName)
                {
                    return (uint)field.GetValue(typeof(ReferenceTypes));
                }
            }

            return 0;
        }
        #endregion
    }

}
