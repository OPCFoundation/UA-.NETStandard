// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
