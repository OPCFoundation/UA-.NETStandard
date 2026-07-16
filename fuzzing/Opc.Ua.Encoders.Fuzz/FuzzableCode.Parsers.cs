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

using System;
using System.Text;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for OPC UA string parsers.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// The NodeId parser fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzNodeIdParse(string input)
        {
            FuzzParser(() => _ = NodeId.Parse(input));
        }

        /// <summary>
        /// The NodeId parser fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzNodeIdParse(ReadOnlySpan<byte> input)
        {
            AflfuzzNodeIdParse(ToUtf8String(input));
        }

        /// <summary>
        /// The ExpandedNodeId parser fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExpandedNodeIdParse(string input)
        {
            FuzzParser(() => _ = ExpandedNodeId.Parse(input));
        }

        /// <summary>
        /// The ExpandedNodeId parser fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExpandedNodeIdParse(ReadOnlySpan<byte> input)
        {
            AflfuzzExpandedNodeIdParse(ToUtf8String(input));
        }

        /// <summary>
        /// The RelativePathFormatter parser fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzRelativePathFormatterParse(string input)
        {
            FuzzParser(() => _ = RelativePathFormatter.Parse(input));
        }

        /// <summary>
        /// The RelativePathFormatter parser fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzRelativePathFormatterParse(ReadOnlySpan<byte> input)
        {
            AflfuzzRelativePathFormatterParse(ToUtf8String(input));
        }

        /// <summary>
        /// The QualifiedName parser fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzQualifiedNameParse(string input)
        {
            FuzzParser(() => _ = QualifiedName.Parse(input));
        }

        /// <summary>
        /// The QualifiedName parser fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzQualifiedNameParse(ReadOnlySpan<byte> input)
        {
            AflfuzzQualifiedNameParse(ToUtf8String(input));
        }

        /// <summary>
        /// The NumericRange parser fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzNumericRangeParse(string input)
        {
            FuzzParser(() => _ = NumericRange.Parse(input));
        }

        /// <summary>
        /// The NumericRange parser fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzNumericRangeParse(ReadOnlySpan<byte> input)
        {
            AflfuzzNumericRangeParse(ToUtf8String(input));
        }

        /// <summary>
        /// The Uuid round-trip fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzUuidRoundTrip(string input)
        {
            FuzzParser(() =>
            {
                var uuid = Uuid.Parse(input);
                var roundTripped = Uuid.Parse(uuid.ToString());
                if (!uuid.Equals(roundTripped))
                {
                    throw new InvalidOperationException("Uuid round-trip failed.");
                }
            });
        }

        /// <summary>
        /// The Uuid round-trip fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzUuidRoundTrip(ReadOnlySpan<byte> input)
        {
            AflfuzzUuidRoundTrip(ToUtf8String(input));
        }

        private static void FuzzParser(Action parse)
        {
            try
            {
                parse();
            }
            catch (ServiceResultException)
            {
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        private static string ToUtf8String(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            return Encoding.UTF8.GetString(input.ToArray());
#else
            return Encoding.UTF8.GetString(input);
#endif
        }
    }
}
