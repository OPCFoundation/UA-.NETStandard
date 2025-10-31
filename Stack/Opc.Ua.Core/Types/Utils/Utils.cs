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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
#if !NETFRAMEWORK
using Opc.Ua.Security.Certificates;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static partial class Utils
    {

        /// <summary>
        /// Suppresses any exceptions while disposing the object.
        /// </summary>
        /// <remarks>
        /// Writes errors to trace output in DEBUG builds.
        /// </remarks>
        public static void SilentDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
#if DEBUG
            catch (Exception e)
            {
                Debug.WriteLine("Error {0} disposing object: {1}", e, disposable.GetType().Name);
            }
#else
            catch
            {
            }
#endif
        }

        /// <summary>
        /// The earliest time that can be represented on with UA date/time values.
        /// </summary>
        public static DateTime TimeBase { get; } = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Normalize a DateTime to Opc Ua UniversalTime.
        /// </summary>
        public static DateTime ToOpcUaUniversalTime(DateTime value)
        {
            if (value <= DateTime.MinValue)
            {
                return DateTime.MinValue;
            }
            if (value >= DateTime.MaxValue)
            {
                return DateTime.MaxValue;
            }
            if (value.Kind != DateTimeKind.Utc)
            {
                return value.ToUniversalTime();
            }
            return value;
        }

        /// <summary>
        /// Escapes a URI string using the percent encoding.
        /// </summary>
        public static string EscapeUri(string uri)
        {
            if (!string.IsNullOrWhiteSpace(uri))
            {
                // always use back compat: for not well formed Uri, fall back to legacy formatting behavior - see #2793, #2826
                // problem with Uri.TryCreate(uri.Replace(";", "%3b"), UriKind.Absolute, out Uri validUri);
                // -> uppercase letters will later be lowercase (and therefore the uri will later be non-matching)
                var buffer = new StringBuilder();
                foreach (char ch in uri)
                {
                    switch (ch)
                    {
                        case ';':
                        case '%':
                            buffer.AppendFormat(
                                CultureInfo.InvariantCulture,
                                "%{0:X2}",
                                Convert.ToInt16(ch));
                            break;
                        default:
                            buffer.Append(ch);
                            break;
                    }
                }
                return buffer.ToString();
            }
            return string.Empty;
        }

        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(ReadOnlySpan<char> uri)
        {
            if (!uri.IsWhiteSpace())
            {
#if NET9_0_OR_GREATER
                return Uri.UnescapeDataString(uri);
#else
                return Uri.UnescapeDataString(uri.ToString());
#endif
            }
            return string.Empty;
        }

        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(string uri)
        {
            if (!string.IsNullOrWhiteSpace(uri))
            {
                return Uri.UnescapeDataString(uri);
            }

            return string.Empty;
        }

        /// <summary>
        /// Converts a multidimension array to a flat array.
        /// </summary>
        /// <remarks>
        /// The higher rank dimensions are written first.
        /// e.g. a array with dimensions [2,2,2] is written in this order:
        /// [0,0,0], [0,0,1], [0,1,0], [0,1,1], [1,0,0], [1,0,1], [1,1,0], [1,1,1]
        /// </remarks>
        public static Array FlattenArray(Array array)
        {
            var flatArray = Array.CreateInstance(array.GetType().GetElementType(), array.Length);

            int[] indexes = new int[array.Rank];
            int[] dimensions = new int[array.Rank];

            for (int jj = array.Rank - 1; jj >= 0; jj--)
            {
                dimensions[jj] = array.GetLength(array.Rank - jj - 1);
            }

            for (int ii = 0; ii < array.Length; ii++)
            {
                indexes[array.Rank - 1] = ii % dimensions[0];

                for (int jj = 1; jj < array.Rank; jj++)
                {
                    int multiplier = 1;

                    for (int kk = 0; kk < jj; kk++)
                    {
                        multiplier *= dimensions[kk];
                    }

                    indexes[array.Rank - jj - 1] = ii / multiplier % dimensions[jj];
                }

                flatArray.SetValue(array.GetValue(indexes), ii);
            }

            return flatArray;
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }

            return ToHexString(new ReadOnlySpan<byte>(buffer), invertEndian);
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
        public static string ToHexString(ReadOnlySpan<byte> buffer, bool invertEndian = false)
        {
            if (buffer.Length == 0)
            {
                return string.Empty;
            }
#else
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }
#endif

#if NET6_0_OR_GREATER
            if (!invertEndian)
            {
                return Convert.ToHexString(buffer);
            }
            else
#endif
            {
                var builder = new StringBuilder(buffer.Length * 2);

#if !NET6_0_OR_GREATER
                if (!invertEndian)
                {
                    for (int ii = 0; ii < buffer.Length; ii++)
                    {
                        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                    }
                }
                else
#endif
                {
                    for (int ii = buffer.Length - 1; ii >= 0; ii--)
                    {
                        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                    }
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// Formats a message using the invariant locale.
        /// </summary>
        public static string Format(string text, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, text, args);
        }

        /// <summary>
        /// Returns a deep copy of the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T Clone<T>(T value)
            where T : class
        {
            return (T)Clone((object)value);
        }

        /// <summary>
        /// Returns a deep copy of the value.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public static object Clone(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();

            // nothing to do for value types.
            if (type.GetTypeInfo().IsPrimitive)
            {
                return value;
            }

            // strings are special a reference type that does not need to be copied.
            if (type == typeof(string))
            {
                return value;
            }

            // Guid are special a reference type that does not need to be copied.
            if (type == typeof(Guid))
            {
                return value;
            }

            // copy arrays, any dimension.
            if (value is Array array)
            {
                if (array.Rank == 1)
                {
                    var clone = Array.CreateInstance(type.GetElementType(), array.Length);
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        clone.SetValue(Clone(array.GetValue(ii)), ii);
                    }
                    return clone;
                }
                else
                {
                    int[] arrayRanks = new int[array.Rank];
                    int[] arrayIndex = new int[array.Rank];
                    for (int ii = 0; ii < array.Rank; ii++)
                    {
                        arrayRanks[ii] = array.GetLength(ii);
                        arrayIndex[ii] = 0;
                    }
                    var clone = Array.CreateInstance(type.GetElementType(), arrayRanks);
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        clone.SetValue(Clone(array.GetValue(arrayIndex)), arrayIndex);

                        // iterate the index array
                        for (int ix = 0; ix < array.Rank; ix++)
                        {
                            arrayIndex[ix]++;
                            if (arrayIndex[ix] < arrayRanks[ix])
                            {
                                break;
                            }
                            arrayIndex[ix] = 0;
                        }
                    }
                    return clone;
                }
            }

            // use ICloneable if supported
            // must be checked before value type due to some
            // structs implementing ICloneable
            if (value is ICloneable cloneable)
            {
                return cloneable.Clone();
            }

            // nothing to do for other value types.
            if (type.GetTypeInfo().IsValueType)
            {
                return value;
            }

            // copy XmlNode.
            if (value is XmlNode node)
            {
                return node.CloneNode(true);
            }

            //try to find the MemberwiseClone method by reflection.
            MethodInfo memberwiseCloneMethod = type.GetMethod(
                "MemberwiseClone",
                BindingFlags.Public | BindingFlags.Instance);
            if (memberwiseCloneMethod != null)
            {
                object clone = memberwiseCloneMethod.Invoke(value, null);
                if (clone != null)
                {
                    Debug.WriteLine("MemberwiseClone without ICloneable in class '{0}'", type.FullName);
                    return clone;
                }
            }

            //try to find the Clone method by reflection.
            MethodInfo cloneMethod = type.GetMethod(
                "Clone",
                BindingFlags.Public | BindingFlags.Instance);
            if (cloneMethod != null)
            {
                object clone = cloneMethod.Invoke(value, null);
                if (clone != null)
                {
                    Debug.WriteLine("Clone without ICloneable in class '{0}'", type.FullName);
                    return clone;
                }
            }

            // don't know how to clone object.
            throw new NotSupportedException(
                Format("Don't know how to clone objects of type '{0}'", type.FullName));
        }

#if ZOMBIE // Manual
        /// <summary>
        /// Checks if two identities are equal.
        /// </summary>
        public static bool IsEqualUserIdentity(
            UserIdentityToken identity1,
            UserIdentityToken identity2)
        {
            // check for reference equality.
            if (ReferenceEquals(identity1, identity2))
            {
                return true;
            }

            if (identity1 == null || identity2 == null)
            {
                return false;
            }

            if (identity1 is AnonymousIdentityToken && identity2 is AnonymousIdentityToken)
            {
                return true;
            }

            if (identity1 is UserNameIdentityToken userName1 &&
                identity2 is UserNameIdentityToken userName2)
            {
                return string.Equals(
                    userName1.UserName,
                    userName2.UserName,
                    StringComparison.Ordinal);
            }

            if (identity1 is X509IdentityToken x509Token1 &&
                identity2 is X509IdentityToken x509Token2)
            {
                return IsEqual(x509Token1.CertificateData, x509Token2.CertificateData);
            }

            if (identity1 is IssuedIdentityToken issuedToken1 &&
                identity2 is IssuedIdentityToken issuedToken2)
            {
                return IsEqual(issuedToken1.DecryptedTokenData, issuedToken2.DecryptedTokenData);
            }

            return false;
        }
#endif

        /// <summary>
        /// Checks if two DateTime values are equal.
        /// </summary>
        public static bool IsEqual(DateTime time1, DateTime time2)
        {
            DateTime utcTime1 = ToOpcUaUniversalTime(time1);
            DateTime utcTime2 = ToOpcUaUniversalTime(time2);

            // values smaller than Timebase can not be binary encoded and are considered equal
            if (utcTime1 <= TimeBase && utcTime2 <= TimeBase)
            {
                return true;
            }

            if (utcTime1 >= DateTime.MaxValue && utcTime2 >= DateTime.MaxValue)
            {
                return true;
            }

            return utcTime1.CompareTo(utcTime2) == 0;
        }

        /// <summary>
        /// Checks if two T values are equal based on IEquatable compare.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(T value1, T value2)
            where T : IEquatable<T>
        {
            // check for reference equality.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (value1 is null)
            {
                if (value2 is not null)
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // use IEquatable comparer
            return value1.Equals(value2);
        }

        /// <summary>
        /// Checks if two IEnumerable T values are equal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(IEnumerable<T> value1, IEnumerable<T> value2)
            where T : IEquatable<T>
        {
            // check for reference equality.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (value1 is null || value2 is null)
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }

        /// <summary>
        /// Checks if two T[] values are equal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(T[] value1, T[] value2)
            where T : unmanaged, IEquatable<T>
        {
            // check for reference equality.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (value1 is null || value2 is null)
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }

#if NETFRAMEWORK
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(byte[] b1, byte[] b2, long count);

        /// <summary>
        /// Fast memcpy version of byte[] compare.
        /// </summary>
        public static bool IsEqual(byte[] value1, byte[] value2)
        {
            // check for reference equality.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (value1 is null || value2 is null)
            {
                return false;
            }

            // Validate buffers are the same length.
            // This also ensures that the count does not exceed the length of either buffer.
            return value1.Length == value2.Length && memcmp(value1, value2, value1.Length) == 0;
        }
#endif

        /// <summary>
        /// Checks if two values are equal.
        /// </summary>
        public static bool IsEqual(object value1, object value2)
        {
            // check for reference equality.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            // check for null values.
            if (value1 is null)
            {
                if (value2 is not null)
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // check for null values.
            if (value2 is null)
            {
                return value1.Equals(value2);
            }

            // check for encodeable objects.
            if (value1 is IEncodeable encodeable1)
            {
                if (value2 is not IEncodeable encodeable2)
                {
                    return false;
                }

                return encodeable1.IsEqual(encodeable2);
            }

            // check that data types are not the same.
            if (value1.GetType() != value2.GetType())
            {
                return value1.Equals(value2);
            }

            // check for DateTime objects
            if (value1 is DateTime time1)
            {
                return IsEqual(time1, (DateTime)value2);
            }

            // check for compareable objects.
            if (value1 is IComparable comparable1)
            {
                return comparable1.CompareTo(value2) == 0;
            }

            // check for XmlElement objects.
            if (value1 is XmlElement element1)
            {
                if (value2 is not XmlElement element2)
                {
                    return false;
                }

                return element1.OuterXml == element2.OuterXml;
            }

            // check for arrays.
            if (value1 is Array array1)
            {
                // arrays are greater than non-arrays.
                if (value2 is not Array array2)
                {
                    return false;
                }

                // shorter arrays are less than longer arrays.
                if (array1.Length != array2.Length)
                {
                    return false;
                }

                // compare the array dimension
                if (array1.Rank != array2.Rank)
                {
                    return false;
                }

                // compare each rank.
                for (int ii = 0; ii < array1.Rank; ii++)
                {
                    if (array1.GetLowerBound(ii) != array2.GetLowerBound(ii) ||
                        array1.GetUpperBound(ii) != array2.GetUpperBound(ii))
                    {
                        return false;
                    }
                }

                // handle byte[] special case fast
                if (array1 is byte[] byteArray1 && array2 is byte[] byteArray2)
                {
#if NETFRAMEWORK
                    return memcmp(byteArray1, byteArray2, byteArray1.Length) == 0;
#else
                    return byteArray1.SequenceEqual(byteArray2);
#endif
                }

                IEnumerator enumerator1 = array1.GetEnumerator();
                IEnumerator enumerator2 = array2.GetEnumerator();

                // compare each element.
                while (enumerator1.MoveNext())
                {
                    // length is already checked
                    enumerator2.MoveNext();

                    bool result = IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // arrays are identical.
                return true;
            }

            // check enumerables.

            if (value1 is IEnumerable enumerable1)
            {
                // collections are greater than non-collections.
                if (value2 is not IEnumerable enumerable2)
                {
                    return false;
                }

                IEnumerator enumerator1 = enumerable1.GetEnumerator();
                IEnumerator enumerator2 = enumerable2.GetEnumerator();

                while (enumerator1.MoveNext())
                {
                    // enumerable2 must be shorter.
                    if (!enumerator2.MoveNext())
                    {
                        return false;
                    }

                    bool result = IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // enumerable2 must be longer.
                if (enumerator2.MoveNext())
                {
                    return false;
                }

                // must be equal.
                return true;
            }

            // check for objects that override the Equals function.
            return value1.Equals(value2);
        }

        /// <summary>
        /// Tests if the specified string matches the specified pattern.
        /// </summary>
        public static bool Match(string target, string pattern, bool caseSensitive)
        {
            // an empty pattern always matches.
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            // an empty string never matches.
            if (string.IsNullOrEmpty(target))
            {
                return false;
            }

            // check for exact match
            if (caseSensitive)
            {
                if (target == pattern)
                {
                    return true;
                }
            }
            else if (string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            char c;
            char p;
            char l;

            int pIndex = 0;
            int tIndex = 0;

            while (tIndex < target.Length && pIndex < pattern.Length)
            {
                p = ConvertCase(pattern[pIndex++], caseSensitive);

                if (pIndex > pattern.Length)
                {
                    return tIndex >= target.Length; // if end of string true
                }

                switch (p)
                {
                    // match zero or more char.
                    case '*':
                        while (tIndex < target.Length)
                        {
                            if (Match(target[tIndex++..], pattern[pIndex..], caseSensitive))
                            {
                                return true;
                            }
                        }

                        return Match(target, pattern[pIndex..], caseSensitive);
                    // match any one char.
                    case '?':
                        // check if end of string when looking for a single character.
                        if (tIndex >= target.Length)
                        {
                            return false;
                        }

                        // check if end of pattern and still string data left.
                        if (pIndex >= pattern.Length && tIndex < target.Length - 1)
                        {
                            return false;
                        }

                        tIndex++;
                        break;
                    // match char set
                    case '[':
                        c = ConvertCase(target[tIndex++], caseSensitive);

                        if (tIndex > target.Length)
                        {
                            return false; // syntax
                        }

                        l = '\0';

                        // match a char if NOT in set []
                        if (pattern[pIndex] == '!')
                        {
                            ++pIndex;

                            p = ConvertCase(pattern[pIndex++], caseSensitive);

                            while (pIndex < pattern.Length)
                            {
                                if (p == ']') // if end of char set, then
                                {
                                    break; // no match found
                                }

                                if (p == '-')
                                {
                                    // check a range of chars?
                                    p = ConvertCase(pattern[pIndex], caseSensitive);

                                    // get high limit of range
                                    if (pIndex > pattern.Length || p == ']')
                                    {
                                        return false; // syntax
                                    }

                                    if (c >= l && c <= p)
                                    {
                                        return false; // if in range, return false
                                    }
                                }

                                l = p;

                                if (c == p) // if char matches this element
                                {
                                    return false; // return false
                                }

                                p = ConvertCase(pattern[pIndex++], caseSensitive);
                            }
                        }
                        // match if char is in set []
                        else
                        {
                            p = ConvertCase(pattern[pIndex++], caseSensitive);

                            while (pIndex < pattern.Length)
                            {
                                if (p == ']') // if end of char set, then no match found
                                {
                                    return false;
                                }

                                if (p == '-')
                                {
                                    // check a range of chars?
                                    p = ConvertCase(pattern[pIndex], caseSensitive);

                                    // get high limit of range
                                    if (pIndex > pattern.Length || p == ']')
                                    {
                                        return false; // syntax
                                    }

                                    if (c >= l && c <= p)
                                    {
                                        break; // if in range, move on
                                    }
                                }

                                l = p;

                                if (c == p) // if char matches this element move on
                                {
                                    break;
                                }

                                p = ConvertCase(pattern[pIndex++], caseSensitive);
                            }

                            while (pIndex < pattern.Length && p != ']') // got a match in char set skip to end of set
                            {
                                p = pattern[pIndex++];
                            }
                        }

                        break;
                    // match digit.
                    case '#':
                        c = target[tIndex++];

                        if (!char.IsDigit(c))
                        {
                            return false; // not a digit
                        }

                        break;
                    // match exact char.
                    default:
                        c = ConvertCase(target[tIndex++], caseSensitive);

                        if (c != p) // check for exact char
                        {
                            return false; // not a match
                        }

                        // check if end of pattern and still string data left.
                        if (pIndex >= pattern.Length && tIndex < target.Length - 1)
                        {
                            return false;
                        }

                        break;
                }
            }

            if (tIndex >= target.Length)
            {
                return pIndex >= pattern.Length; // if end of pattern true
            }

            return true;
        }

        /// <summary>
        /// ConvertCase
        /// </summary>
        private static char ConvertCase(char c, bool caseSensitive)
        {
            return caseSensitive ? c : char.ToUpperInvariant(c);
        }

        /// <summary>
        /// Returns the major/minor version number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblySoftwareVersion()
        {
            return typeof(Utils)
                .GetTypeInfo()
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
        }

        /// <summary>
        /// Returns the build/revision number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblyBuildNumber()
        {
            return typeof(Utils).GetTypeInfo().Assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                .Version;
        }

        /// <summary>
        /// Returns a XmlReaderSetting with safe defaults.
        /// DtdProcessing Prohibited, XmlResolver disabled and
        /// ConformanceLevel Document.
        /// </summary>
        public static XmlReaderSettings DefaultXmlReaderSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ConformanceLevel = ConformanceLevel.Document
            };
        }

        /// <summary>
        /// Returns a XmlWriterSetting with deterministic defaults across .NET versions.
        /// </summary>
        public static XmlWriterSettings DefaultXmlWriterSettings()
        {
            return new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                ConformanceLevel = ConformanceLevel.Document,
                IndentChars = "  ",
                CloseOutput = false
            };
        }

        /// <summary>
        /// Safe version for assignment of InnerXml.
        /// </summary>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="xml">The Xml document string.</param>
        internal static void LoadInnerXml(this XmlDocument doc, string xml)
        {
            using var sreader = new StringReader(xml);
            using var reader = XmlReader.Create(sreader, DefaultXmlReaderSettings());
            doc.XmlResolver = null;
            doc.Load(reader);
        }
    }
}
