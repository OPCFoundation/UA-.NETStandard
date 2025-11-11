/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;

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
        /// Replace line endings
        /// </summary>
        public static string ReplaceLineEndings(this string target)
        {
            return target.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison type.
        /// </summary>
        public static bool Contains(this string target, char value, StringComparison comparisonType)
        {
            return target.Contains(value);
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison type.
        /// </summary>
        public static bool Contains(
            this string target,
            string value,
            StringComparison comparisonType)
        {
            return target.Contains(value);
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
#endif
    }
}
