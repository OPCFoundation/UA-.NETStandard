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
using System.Reflection;
using System.Collections.ObjectModel;
using System.Collections.Generic;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class StatusCodes
    {
        /// <summary>
        /// Returns the browse name for the attribute.
        /// </summary>
        public static string GetBrowseName(uint identifier)
        {
            if (BrowseNames.Value.TryGetValue(identifier, out var browseName))
            {
                return browseName;
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns the UTF-8 browse name for the attribute.
        /// </summary>
        public static byte[] GetUtf8BrowseName(uint identifier)
        {
            if (Utf8BrowseNames.Value.TryGetValue(identifier, out var utf8BrowseName))
            {
                return utf8BrowseName;
            }

            return null;
        }

        /// <summary>
        /// Returns the browse names for all attributes.
        /// </summary>
        public static IReadOnlyCollection<string> GetBrowseNames()
        {
            return BrowseNames.Value.Values;
        }

        /// <summary>
        /// Returns the id for the attribute with the specified browse name.
        /// </summary>
        public static uint GetIdentifier(string browseName)
        {
            foreach (var field in BrowseNames.Value)
            {
                if (field.Value == browseName)
                {
                    return field.Key;
                }
            }

            return 0;
        }

        private static ReadOnlyDictionary<uint, string> CreateBrowseNamesDictionary()
        {
            FieldInfo[] fields = typeof(StatusCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

            var keyValuePairs = new Dictionary<uint, string>();
            foreach (FieldInfo field in fields)
            {
                keyValuePairs.Add((uint)field.GetValue(typeof(StatusCodes)), field.Name);
            }
#if NET8_0_OR_GREATER
            return keyValuePairs.ToFrozenDictionary().AsReadOnly();
#else
            return new ReadOnlyDictionary<uint, string>(keyValuePairs);
#endif
        }

        private static ReadOnlyDictionary<uint, byte[]> CreateUtf8BrowseNamesDictionary()
        {
            FieldInfo[] fields = typeof(StatusCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

            var keyValuePairs = new Dictionary<uint, byte[]>();
            foreach (FieldInfo field in fields)
            {
                keyValuePairs.Add((uint)field.GetValue(typeof(StatusCodes)),
                    System.Text.Encoding.UTF8.GetBytes(field.Name));
            }

#if NET8_0_OR_GREATER
            return keyValuePairs.ToFrozenDictionary().AsReadOnly();
#else
            return new ReadOnlyDictionary<uint, byte[]>(keyValuePairs);
#endif
        }

        /// <summary>
        /// Creates a dictionary of browse names for the status codes.
        /// </summary>
        private static readonly Lazy<ReadOnlyDictionary<uint, string>> BrowseNames =
            new(CreateBrowseNamesDictionary);

        /// <summary>
        /// Creates a dictionary of Utf8 browse names for the status codes.
        /// </summary>
        private static readonly Lazy<ReadOnlyDictionary<uint, byte[]>> Utf8BrowseNames =
            new(CreateUtf8BrowseNamesDictionary);
    }
}
