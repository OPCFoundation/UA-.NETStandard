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

using System;
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
    ///   <item><description><c>[^abc]</c> — matches any single character not in the set.
    ///   The legacy <c>[!abc]</c> spelling is also accepted.</description></item>
    ///   <item><description><c>\</c> — escapes the next wildcard character.</description></item>
    /// </list>
    /// Matching is case-sensitive and anchored: the entire target must match
    /// the entire pattern. Evaluation has a finite timeout on all target frameworks.
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
            return Matches(target, CreateRegex(pattern));
        }

        internal static Regex CreateRegex(string pattern)
        {
            StringBuilder sb = new StringBuilder(pattern.Length + 8)
                .Append(@"\A");
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '\\':
                        if (i + 1 < pattern.Length)
                        {
                            sb.Append(Regex.Escape(pattern[i + 1].ToString()));
                            i += 2;
                        }
                        else
                        {
                            sb.Append("\\\\");
                            i++;
                        }
                        break;
                    case '%':
                        sb.Append(".*");
                        do
                        {
                            i++;
                        }
                        while (i < pattern.Length && pattern[i] == '%');
                        break;
                    case '_':
                        sb.Append('.');
                        i++;
                        break;
                    case '[':
                        int end = i + 1;
                        while (end < pattern.Length && pattern[end] != ']')
                        {
                            end += pattern[end] == '\\' && end + 1 < pattern.Length ? 2 : 1;
                        }
                        if (end == pattern.Length)
                        {
                            sb.Append("\\[");
                            i++;
                        }
                        else
                        {
                            sb.Append('[');
                            i++;
                            if (i < end && pattern[i] is '^' or '!')
                            {
                                sb.Append('^');
                                i++;
                            }
                            while (i < end)
                            {
                                char member = pattern[i++];
                                bool escaped = member == '\\';
                                if (escaped)
                                {
                                    member = pattern[i++];
                                }
                                if (member is '\\' or ']' or '[' or '^' || (member == '-' && escaped))
                                {
                                    sb.Append('\\');
                                }
                                sb.Append(member);
                            }
                            sb.Append(']');
                            i = end + 1;
                        }
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                        break;
                }
            }
            sb.Append(@"\z");
            try
            {
                return new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant, s_matchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, ex, "Invalid alias-name search pattern.");
            }
        }

        internal static bool Matches(string target, Regex pattern)
        {
            try
            {
                return pattern.IsMatch(target);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTimeout, ex, "Alias-name pattern evaluation exceeded its time limit.");
            }
        }

        private static readonly TimeSpan s_matchTimeout = TimeSpan.FromMilliseconds(100);
    }
}
