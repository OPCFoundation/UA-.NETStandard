/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


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
        /// Same as get browsename
        /// </summary>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static string GetSymbolicId(this StatusCode statusCode)
        {
            if (!string.IsNullOrEmpty(statusCode.SymbolicId))
            {
                return statusCode.SymbolicId;
            }
            return GetBrowseName(statusCode.CodeBits);
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

        // This will be removed in next version
#pragma warning disable CA2255 // The ModuleInitializer attribute should not be used in libraries
        [ModuleInitializer]
        internal static void InitializeStatusCodes()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            StatusCode.LookupSymbolicIdHook = LookupSymbolicId;
#pragma warning restore CS0618 // Type or member is obsolete
        }
#pragma warning restore CA2255 // The ModuleInitializer attribute should not be used in libraries
    }
}
