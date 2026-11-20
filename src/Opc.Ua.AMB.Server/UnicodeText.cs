// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
