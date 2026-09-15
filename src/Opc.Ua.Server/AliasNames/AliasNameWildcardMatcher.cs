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

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Implements the OPC UA <c>Like</c>-operator wildcard pattern match
    /// described in OPC UA Part 4 §7.7.3 (FilterOperator Like) — used by
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
    ///   <item><description><c>[^abc]</c> (or legacy <c>[!abc]</c>) — matches any single character not in the set.</description></item>
    ///   <item><description><c>\</c> — escapes the next wildcard character.</description></item>
    /// </list>
    /// The pattern is evaluated by <see cref="LikePattern"/>. Matching is
    /// case-sensitive and anchored: the entire target must match the entire
    /// pattern. A malformed pattern (trailing escape character, unterminated
    /// or empty <c>[..]</c> list, descending range, <c>^</c> that is not the
    /// first list character) is not a valid search string and matches
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
            return !string.IsNullOrEmpty(pattern) && LikePattern.IsMatch(target, pattern);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="pattern"/> is a valid OPC UA
        /// Like search string (Part 4 §7.7.3). A <c>null</c> or empty pattern is
        /// valid and simply matches nothing. Part 17 §6.3.2 requires
        /// <c>Bad_InvalidArgument</c> from <c>FindAlias</c> for an invalid one.
        /// </summary>
        /// <param name="pattern">The search string to validate.</param>
        /// <returns><c>false</c> for a trailing escape character, an
        /// unterminated or empty <c>[..]</c> list, a descending range or a
        /// misplaced <c>^</c>; otherwise <c>true</c>.</returns>
        public static bool IsValidPattern(string? pattern)
        {
            return string.IsNullOrEmpty(pattern) || LikePattern.IsValid(pattern);
        }
    }
}
