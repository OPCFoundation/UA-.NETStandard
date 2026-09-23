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

#if NET10_0
using System.Diagnostics;
using System.Text;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Helpers for tests that assert on the console output of the
    /// repository's PowerShell scripts.
    /// </summary>
    internal static class PowerShellScriptOutput
    {
        /// <summary>
        /// Configures a pwsh process so its diagnostics are stable across
        /// operating systems: no ANSI colouring, and no terminal-dependent
        /// decoration.
        /// </summary>
        public static void ConfigureDeterministicOutput(ProcessStartInfo startInfo)
        {
            startInfo.Environment["NO_COLOR"] = "1";
            startInfo.Environment["TERM"] = "dumb";
        }

        /// <summary>
        /// Undoes the host formatting PowerShell applies to a terminating
        /// error so a script's message can be matched verbatim.
        /// </summary>
        /// <remarks>
        /// PowerShell's default ConciseView renders a thrown message as a
        /// block that is hard-wrapped to the console width, continuing each
        /// line with a "     | " marker. The console width differs per
        /// operating system - the Windows agents are wide enough to emit the
        /// message unwrapped, while the Linux and macOS agents fall back to
        /// 80 columns - so an assertion on any phrase long enough to straddle
        /// a wrap point passes on one agent and fails on another. Wrapping
        /// always happens at a space, so rejoining the continuations with a
        /// single space reconstructs the original message exactly.
        /// </remarks>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var normalized = new StringBuilder(value.Length);
            for (int ii = 0; ii < value.Length; ii++)
            {
                char character = value[ii];
                if (character == '\u001b' && ii + 1 < value.Length && value[ii + 1] == '[')
                {
                    ii += 2;
                    while (ii < value.Length && (value[ii] < '@' || value[ii] > '~'))
                    {
                        ii++;
                    }
                    continue;
                }

                if (character is '\r' or '\n' &&
                    TryMeasureContinuation(value, ii, out int length))
                {
                    normalized.Append(' ');
                    ii += length - 1;
                    continue;
                }

                normalized.Append(character);
            }

            return normalized.ToString();
        }

        /// <summary>
        /// Measures a line break followed by a ConciseView continuation
        /// marker: a run of horizontal whitespace, a '|', and the single
        /// space that separates the marker from the wrapped text.
        /// </summary>
        private static bool TryMeasureContinuation(string value, int start, out int length)
        {
            length = 0;
            int index = start;
            if (value[index] == '\r')
            {
                index++;
            }
            if (index >= value.Length || value[index] != '\n')
            {
                return false;
            }

            index++;
            int whitespaceStart = index;
            while (index < value.Length && (value[index] == ' ' || value[index] == '\t'))
            {
                index++;
            }

            if (index == whitespaceStart || index >= value.Length || value[index] != '|')
            {
                return false;
            }

            index++;
            if (index < value.Length && (value[index] == ' ' || value[index] == '\t'))
            {
                index++;
            }

            length = index - start;
            return true;
        }
    }
}
#endif
