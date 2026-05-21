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

using System.Text;
using System.Text.RegularExpressions;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Implements the OPC UA <c>Like</c>-operator wildcard pattern match
    /// described in OPC UA Part 4 §7.40 (FilterOperator Like) — used by
    /// Part 17 <c>FindAlias</c>/<c>FindAliasVerbose</c> methods to match
    /// the <c>AliasNameSearchPattern</c> input argument against alias
    /// names.
    /// </summary>
    /// <remarks>
    /// Supported wildcards:
    /// <list type="bullet">
    ///   <item><description><c>%</c> — matches zero or more characters.</description></item>
    ///   <item><description><c>_</c> — matches exactly one character.</description></item>
    ///   <item><description><c>[abc]</c> — matches any single character from the set.</description></item>
    ///   <item><description><c>[!abc]</c> — matches any single character not in the set.</description></item>
    ///   <item><description><c>\</c> — escapes the next wildcard character.</description></item>
    /// </list>
    /// Algorithm ported from the original implementation in the Quickstart
    /// reference server (which itself ported the private <c>Match</c> from
    /// <c>Stack/Opc.Ua.Core/Stack/Types/FilterEvaluator.cs</c>). Matching is
    /// case-sensitive and anchored: the entire target must match the entire
    /// pattern.
    /// </remarks>
    public static class AliasNameWildcardMatcher
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="target"/> matches the
        /// OPC UA Like wildcard <paramref name="pattern"/>.
        /// </summary>
        /// <param name="target">String to test; must not be <c>null</c>.</param>
        /// <param name="pattern">OPC UA Like pattern; must not be <c>null</c>.</param>
        /// <returns><c>true</c> if the target matches; otherwise <c>false</c>.
        /// Both <c>null</c> inputs and an empty <paramref name="pattern"/>
        /// return <c>false</c>.</returns>
        public static bool IsMatch(string? target, string? pattern)
        {
            if (target == null || pattern == null)
            {
                return false;
            }
            if (pattern.Length == 0)
            {
                return false;
            }

            // Translate the OPC UA Like pattern to an anchored .NET regex
            // by walking the input char-by-char. We need to:
            //   - escape regex metacharacters that aren't wildcards;
            //   - turn '%' / '_' / '[..]' / '[!..]' into the matching
            //     regex constructs;
            //   - honour the OPC UA escape character '\' which makes the
            //     next character match literally.
            StringBuilder sb = new StringBuilder(pattern.Length + 8)
                .Append('^');
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '\\':
                        // OPC UA escape: next character is matched
                        // literally (including '\' '%' '_' '[' ']').
                        if (i + 1 < pattern.Length)
                        {
                            sb.Append(Regex.Escape(pattern[i + 1].ToString()));
                            i += 2;
                        }
                        else
                        {
                            // Trailing backslash with nothing to escape —
                            // match a literal backslash.
                            sb.Append("\\\\");
                            i++;
                        }
                        break;
                    case '%':
                        sb.Append(".*");
                        i++;
                        break;
                    case '_':
                        sb.Append('.');
                        i++;
                        break;
                    case '[':
                        int end = pattern.IndexOf(']', i + 1);
                        if (end < 0)
                        {
                            // No matching close-bracket — treat as a
                            // literal '['.
                            sb.Append("\\[");
                            i++;
                        }
                        else
                        {
                            // [abc] or [!abc] — copy the contents
                            // verbatim, swapping leading '!' for '^' per
                            // Part 4 §7.40. The contents are taken as-is
                            // (regex char-class semantics are a superset
                            // of OPC UA — for simple character lists this
                            // works correctly).
                            string body = pattern.Substring(i + 1, end - i - 1);
                            if (body.Length > 0 && body[0] == '!')
                            {
                                sb.Append("[^").Append(body, 1, body.Length - 1)
                                    .Append(']');
                            }
                            else
                            {
                                sb.Append('[').Append(body).Append(']');
                            }
                            i = end + 1;
                        }
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                        break;
                }
            }
            sb.Append('$');
            return Regex.IsMatch(target, sb.ToString());
        }
    }
}
