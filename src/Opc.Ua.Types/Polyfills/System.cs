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

#if NETSTANDARD2_0 || NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0060, RCS1163 // Unused parameter
#pragma warning restore IDE0079 // Remove unnecessary suppression
#endif

namespace System
{
    /// <summary>
    /// Polyfills for System methods that are not available in .NET Standard 2.0 or .NET Framework.
    /// </summary>
    public static class Polyfills
    {
        /// <summary>
        /// Throws when a reference argument is null.
        /// </summary>
        /// <typeparam name="T">The reference type to validate.</typeparam>
        /// <param name="target">The reference to validate.</param>
        /// <param name="parameterName">The argument name.</param>
        /// <returns>The non-null reference.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static T ThrowIfNull<T>(
            this T? target,
            string parameterName)
            where T : class
        {
            return target ?? throw new ArgumentNullException(parameterName);
        }

        /// <summary>
        /// Returns true when a double is neither NaN nor infinity.
        /// </summary>
        public static bool IsFinite(this double target)
        {
            return !double.IsNaN(target) && !double.IsInfinity(target);
        }

#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Return hash code assuming ordinal
        /// </summary>
        public static int GetHashCode(this string target, StringComparison comparisonType)
        {
            return target.GetHashCode();
        }

        /// <summary>
        /// Replace line endings
        /// </summary>
        public static string ReplaceLineEndings(this string target)
        {
            return target.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        /// <summary>
        /// Replace line endings with a specified string
        /// </summary>
        public static string ReplaceLineEndings(this string target, string replacementText)
        {
            return target.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", replacementText);
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison type assuming ordinal.
        /// </summary>
        public static bool Contains(this string target, char value, StringComparison comparisonType)
        {
            return target.IndexOf(value, comparisonType) >= 0;
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison typeassuming ordinal.
        /// </summary>
        public static bool Contains(
            this string target,
            string value,
            StringComparison comparisonType)
        {
            return target.IndexOf(value, comparisonType) >= 0;
        }

        /// <summary>
        /// Compare to a string using a specified comparison type.
        /// </summary>
        public static int CompareTo(
            this string target,
            string value,
            StringComparison comparisonType)
        {
            return string.Compare(target, value, comparisonType);
        }

        /// <summary>
        /// Starts with a character in a string
        /// </summary>
        public static bool StartsWith(this string target, char value)
        {
            return target.Length != 0 && target[0] == value;
        }

        /// <summary>
        /// Ends with a character in a string
        /// </summary>
        public static bool EndsWith(this string target, char value)
        {
            return target.Length != 0 && target[^1] == value;
        }

        /// <summary>
        /// Index of a character in a string using a specified comparison type.
        /// </summary>
        public static int IndexOf(this string target, char value, StringComparison comparisonType)
        {
            return target.IndexOf(value);
        }

        /// <summary>
        /// Replace a string in a string using a specified comparison type.
        /// </summary>
        public static string Replace(
            this string target,
            string oldValue,
            string newValue,
            StringComparison comparisonType)
        {
            return target.Replace(oldValue, newValue);
        }

        /// <summary>
        /// Multiply the timespan with a factor
        /// </summary>
        /// <returns></returns>
        public static TimeSpan Multiply(this TimeSpan timespan, double factor)
        {
            return new TimeSpan((long)(timespan.Ticks * factor));
        }

        /// <summary>
        /// Divide the timespan by a factor
        /// </summary>
        /// <returns></returns>
        public static TimeSpan Divide(this TimeSpan timespan, double factor)
        {
            return new TimeSpan((long)(timespan.Ticks / factor));
        }

        /// <summary>
        /// Concatenates the string representations of the elements and appends the
        /// result, using the specified separator between each member.
        /// </summary>
        /// <typeparam name="T">The type of the members to join.</typeparam>
        public static StringBuilder AppendJoin<T>(
            this StringBuilder target,
            string separator,
            IEnumerable<T> values)
        {
            return target.Append(string.Join(separator, values));
        }

        /// <summary>
        /// Concatenates the string representations of the elements and appends the
        /// result, using the specified separator between each member.
        /// </summary>
        /// <typeparam name="T">The type of the members to join.</typeparam>
        public static StringBuilder AppendJoin<T>(
            this StringBuilder target,
            char separator,
            IEnumerable<T> values)
        {
            return target.Append(string.Join(separator.ToString(), values));
        }

        /// <summary>
        /// Concatenates the string representations of the values and appends the
        /// result, using the specified separator between each member.
        /// </summary>
        public static StringBuilder AppendJoin(
            this StringBuilder target,
            string separator,
            params object[] values)
        {
            return target.Append(string.Join(separator, values));
        }

        /// <summary>
        /// Concatenates the string representations of the values and appends the
        /// result, using the specified separator between each member.
        /// </summary>
        public static StringBuilder AppendJoin(
            this StringBuilder target,
            char separator,
            params object[] values)
        {
            return target.Append(string.Join(separator.ToString(), values));
        }

        /// <summary>
        /// Concatenates the strings and appends the result, using the specified
        /// separator between each member.
        /// </summary>
        public static StringBuilder AppendJoin(
            this StringBuilder target,
            string separator,
            params string[] values)
        {
            return target.Append(string.Join(separator, values));
        }

        /// <summary>
        /// Concatenates the strings and appends the result, using the specified
        /// separator between each member.
        /// </summary>
        public static StringBuilder AppendJoin(
            this StringBuilder target,
            char separator,
            params string[] values)
        {
            return target.Append(string.Join(separator.ToString(), values));
        }
#endif
    }
}
