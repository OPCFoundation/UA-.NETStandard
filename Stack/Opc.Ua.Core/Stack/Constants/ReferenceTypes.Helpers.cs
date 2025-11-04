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

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
using System.Linq;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class ReferenceTypes
    {
        /// <summary>
        /// Returns the browse name for the attribute.
        /// </summary>
        public static string GetBrowseName(uint identifier)
        {
            return s_referenceTypeIdToName.Value.TryGetValue(identifier, out string name)
                ? name : string.Empty;
        }

        /// <summary>
        /// Creates a dictionary of identifers to browse names for reference types.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<uint, string>> s_referenceTypeIdToName =
            new(() =>
            {
                FieldInfo[] fields = typeof(ReferenceTypes).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<uint, string>();
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType == typeof(uint))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(typeof(ReferenceTypes)),
                            System.Globalization.CultureInfo.InvariantCulture);
                        keyValuePairs.Add(value, field.Name);
                    }
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<uint, string>(keyValuePairs);
#endif
            });
    }
}
