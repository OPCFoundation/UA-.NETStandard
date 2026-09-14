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
using System.Diagnostics.CodeAnalysis;
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
    /// A malformed pattern (trailing escape character, unterminated
    /// or empty <c>[..]</c> list) is not a valid search string and matches
    /// nothing; see <see cref="IsValidPattern"/>.
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
        /// Both <c>null</c> inputs, an empty <paramref name="pattern"/> and an
        /// invalid pattern return <c>false</c>.</returns>
        public static bool IsMatch(string? target, string? pattern)
        {
            if (target == null || string.IsNullOrEmpty(pattern))
            {
                return false;
            }
            return TryCreateRegex(pattern!, out Regex? regex) && Matches(target, regex);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="pattern"/> is a valid OPC UA
        /// Like search string (Part 4 §7.7.3). A <c>null</c> or empty pattern is
        /// valid and simply matches nothing. Part 17 §6.3.2 requires
        /// <c>Bad_InvalidArgument</c> from <c>FindAlias</c> for an invalid one.
        /// </summary>
        /// <param name="pattern">The search string to validate.</param>
        /// <returns><c>false</c> for a trailing escape character, an
        /// unterminated or empty <c>[..]</c> list, or a list that cannot be
        /// evaluated; otherwise <c>true</c>.</returns>
        public static bool IsValidPattern(string? pattern)
        {
            return string.IsNullOrEmpty(pattern) || TryCreateRegex(pattern!, out _);
        }

        /// <summary>
        /// Converts a valid OPC UA Like pattern into an anchored expression with a finite match timeout.
        /// </summary>
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
                            throw ServiceResultException.Create(
                                StatusCodes.BadInvalidArgument,
                                "Alias-name search pattern ends with an escape character.");
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
                            throw ServiceResultException.Create(
                                StatusCodes.BadInvalidArgument,
                                "Alias-name search pattern contains an unterminated character set.");
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
                            if (i == end)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadInvalidArgument,
                                    "Alias-name search pattern contains an empty character set.");
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

        /// <summary>
        /// Tests an alias name and reports expression timeouts as BadTimeout service errors.
        /// </summary>
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

        /// <summary>
        /// Attempts to compile a wildcard pattern, returning false for invalid search syntax.
        /// </summary>
        private static bool TryCreateRegex(string pattern, [NotNullWhen(true)] out Regex? regex)
        {
            try
            {
                regex = CreateRegex(pattern);
                return true;
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidArgument)
            {
                regex = null;
                return false;
            }
        }

        /// <summary>
        /// Limits the time spent evaluating one alias-name match.
        /// </summary>
        private static readonly TimeSpan s_matchTimeout = TimeSpan.FromMilliseconds(100);
    }
}
