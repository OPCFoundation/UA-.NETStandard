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
using System.Collections.Generic;

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
    public static partial class StatusCodes
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        public const uint Good = 0x00000000;

        /// <summary>
        /// The operation completed however its outputs may not be usable.
        /// </summary>
        public const uint Uncertain = 0x40000000;

        /// <summary>
        /// The operation failed.
        /// </summary>
        public const uint Bad = 0x80000000;

        /// <summary>
        /// Returns the browse names for all attributes
        /// </summary>
        public static IEnumerable<string> BrowseNames => s_symbolToStatusCode.Value.Keys;

        /// <summary>
        /// Returns the browse name for the attribute.
        /// </summary>
        public static string GetBrowseName(uint identifier)
        {
            return s_statusCodeToSymbol.Value.TryGetValue(identifier & 0xFFFF0000, out string name)
                ? name : string.Empty;
        }

        /// <summary>
        /// Same as GetBrowseName
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns></returns>
        public static string LookupSymbolicId(uint identifier)
        {
            return GetBrowseName(identifier);
        }

        /// <summary>
        /// Returns the UTF-8 browse name for the attribute.
        /// </summary>
        public static byte[] GetUtf8BrowseName(uint identifier)
        {
            return s_utf8BrowseNames.Value.TryGetValue(identifier & 0xFFFF0000, out byte[] name)
                ? name : null;
        }

        /// <summary>
        /// Returns the browse names for all attributes.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }

        /// <summary>
        /// Returns the id for the attribute with the specified browse name.
        /// </summary>
        public static uint GetIdentifier(string browseName)
        {
            return s_symbolToStatusCode.Value.TryGetValue(browseName, out uint id)
                ? id : 0;
        }

        /// <summary>
        /// Creates a dictionary of browse names to status codes
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, uint>> s_symbolToStatusCode =
            new(() =>
            {
#if NET8_0_OR_GREATER
                return s_statusCodeToSymbol.Value.ToFrozenDictionary(k => k.Value, k => k.Key);
#else
                return new ReadOnlyDictionary<string, uint>(
                    s_statusCodeToSymbol.Value.ToDictionary(k => k.Value, k => k.Key));
#endif
            });

        /// <summary>
        /// Creates a dictionary of browse names for the status codes.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<uint, string>> s_statusCodeToSymbol =
            new(() =>
            {
                FieldInfo[] fields = typeof(StatusCodes).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<uint, string>();
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType == typeof(uint))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(typeof(StatusCodes)),
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

        /// <summary>
        /// Creates a dictionary of Utf8 browse names for the status codes.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<uint, byte[]>> s_utf8BrowseNames =
            new(() =>
            {
                FieldInfo[] fields = typeof(StatusCodes).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<uint, byte[]>();
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType == typeof(uint))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(typeof(StatusCodes)),
                            System.Globalization.CultureInfo.InvariantCulture);
                        keyValuePairs.Add(value,
                            System.Text.Encoding.UTF8.GetBytes(field.Name));
                    }
                }

#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<uint, byte[]>(keyValuePairs);
#endif
            });
    }
}
