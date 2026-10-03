/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Counts text the way OPC 10000-110 states lengths: in Unicode
    /// characters (code points), so a character outside the Basic
    /// Multilingual Plane counts once although it takes two UTF-16 units.
    /// </summary>
    internal static class UnicodeText
    {
        /// <summary>
        /// Gets the number of Unicode characters of a text; an unpaired
        /// surrogate counts as one.
        /// </summary>
        /// <param name="text">The text.</param>
        public static int Length(string text)
        {
            int count = 0;
            for (int ii = 0; ii < text.Length; ii++)
            {
                if (char.IsHighSurrogate(text[ii]) && ii + 1 < text.Length && char.IsLowSurrogate(text[ii + 1]))
                {
                    ii++;
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// Cuts a text to at most a number of Unicode characters, without
        /// splitting a surrogate pair.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="maxLength">The number of characters to keep at most.</param>
        public static string Truncate(string text, int maxLength)
        {
            int count = 0;
            for (int ii = 0; ii < text.Length; ii++)
            {
                if (count == maxLength)
                {
                    return text.Substring(0, ii);
                }
                if (char.IsHighSurrogate(text[ii]) && ii + 1 < text.Length && char.IsLowSurrogate(text[ii + 1]))
                {
                    ii++;
                }
                count++;
            }
            return text;
        }
    }
}
