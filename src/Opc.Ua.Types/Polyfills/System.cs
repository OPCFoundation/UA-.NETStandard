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
            // Honour the comparison instead of always using the ordinal
            // overload, which ignored a requested culture or case insensitivity.
            return comparisonType == StringComparison.Ordinal
                ? target.IndexOf(value)
                : target.IndexOf(value.ToString(), comparisonType);
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
            if (comparisonType == StringComparison.Ordinal)
            {
                return target.Replace(oldValue, newValue);
            }

            // The framework overload throws for these, and so does the ordinal
            // branch above through string.Replace. Returning the target instead
            // would make the contract depend on the target framework and on the
            // comparison the caller asked for.
            if (oldValue == null)
            {
                throw new ArgumentNullException(nameof(oldValue));
            }
            if (oldValue.Length == 0)
            {
                throw new ArgumentException(
                    "String cannot be of zero length.",
                    nameof(oldValue));
            }

            // Honour the comparison instead of always replacing ordinally.
            var builder = new System.Text.StringBuilder(target.Length);
            bool ordinalIgnoreCase = comparisonType == StringComparison.OrdinalIgnoreCase;
            int index = 0;

            while (index < target.Length)
            {
                int match = target.IndexOf(oldValue, index, comparisonType);

                if (match < 0)
                {
                    builder.Append(target, index, target.Length - index);
                    break;
                }

                // A culture sensitive match can span a different number of
                // characters than oldValue - under de-DE "ss" matches the single
                // sharp s character, and an ignorable oldValue matches none at
                // all - so advance by the length that actually matched. Only the
                // ordinal comparisons are guaranteed to match oldValue.Length
                // characters.
                int matched = ordinalIgnoreCase
                    ? oldValue.Length
                    : MatchLength(target, match, oldValue, comparisonType);

                if (matched == 0)
                {
                    // Nothing to consume here, so keep the character and move
                    // on rather than replacing the whole string one empty match
                    // at a time.
                    builder.Append(target, index, match - index + 1);
                    index = match + 1;
                    continue;
                }

                builder.Append(target, index, match - index).Append(newValue);
                index = match + matched;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns the number of characters at <paramref name="start"/> that
        /// compare equal to <paramref name="oldValue"/> under the given
        /// comparison, which is not necessarily the length of
        /// <paramref name="oldValue"/> for a culture sensitive comparison.
        /// </summary>
        private static int MatchLength(
            string target,
            int start,
            string oldValue,
            StringComparison comparisonType)
        {
            int available = target.Length - start;
            for (int length = 0; length <= available; length++)
            {
                if (string.Equals(
                    target.Substring(start, length),
                    oldValue,
                    comparisonType))
                {
                    return length;
                }
            }
            return oldValue.Length;
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
