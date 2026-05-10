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

using System.Text.RegularExpressions;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Implements the OPC UA Like-operator wildcard pattern match
    /// described in OPC UA Part 4 §7.40 (FilterOperator Like).
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
    /// Algorithm ported from
    /// <c>Stack/Opc.Ua.Core/Stack/Types/FilterEvaluator.cs</c> (private <c>Match</c>).
    /// </remarks>
    public static partial class AliasNameWildcardMatcher
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="target"/> matches the
        /// OPC UA Like wildcard <paramref name="pattern"/>.
        /// </summary>
        /// <param name="target">String to test.</param>
        /// <param name="pattern">OPC UA Like pattern.</param>
        public static bool IsMatch(string target, string pattern)
        {
            if (target == null || pattern == null)
            {
                return false;
            }

            string expression = pattern;

            // Suppress the regex meta characters that are not OPC UA wildcards
            // so they will not interfere with the match.
            expression = SuppressUnusedCharacters.Replace(expression, "\\$1");

            // Replace the OPC UA wildcards with their regex equivalents.
            expression = ReplaceWildcards.Replace(expression, ".*");
            expression = ReplaceUnderscores.Replace(expression, ".");
            expression = ReplaceBrackets.Replace(expression, "[^");

            return Regex.IsMatch(target, "^" + expression + "$");
        }

#if NET8_0_OR_GREATER
        [GeneratedRegex("([\\^\\$\\.\\|\\?\\*\\+\\(\\)])", RegexOptions.Compiled)]
        private static partial Regex _SuppressUnusedCharacters();
        private static Regex SuppressUnusedCharacters => _SuppressUnusedCharacters();

        [GeneratedRegex("(?<!\\\\)%", RegexOptions.Compiled)]
        private static partial Regex _ReplaceWildcards();
        private static Regex ReplaceWildcards => _ReplaceWildcards();

        [GeneratedRegex("(?<!\\\\)_", RegexOptions.Compiled)]
        private static partial Regex _ReplaceUnderscores();
        private static Regex ReplaceUnderscores => _ReplaceUnderscores();

        [GeneratedRegex("(?<!\\\\)(\\[!)", RegexOptions.Compiled)]
        private static partial Regex _ReplaceBrackets();
        private static Regex ReplaceBrackets => _ReplaceBrackets();
#else
        private static Regex SuppressUnusedCharacters { get; }
            = new("([\\^\\$\\.\\|\\?\\*\\+\\(\\)])", RegexOptions.Compiled);
        private static Regex ReplaceWildcards { get; }
            = new("(?<!\\\\)%", RegexOptions.Compiled);
        private static Regex ReplaceUnderscores { get; }
            = new("(?<!\\\\)_", RegexOptions.Compiled);
        private static Regex ReplaceBrackets { get; }
            = new("(?<!\\\\)(\\[!)", RegexOptions.Compiled);
#endif
    }
}
