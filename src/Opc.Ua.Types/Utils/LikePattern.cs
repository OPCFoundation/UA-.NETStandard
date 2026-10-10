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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// A parsed search pattern of the <c>Like</c> FilterOperator
    /// (OPC 10000-4 §7.7.3, "Wildcard characters" table). The same syntax is
    /// used by the GDS <c>QueryApplications</c>/<c>QueryServers</c> filters
    /// (OPC 10000-12 §6.5.10/§6.5.11) and by the <c>FindAlias</c> search
    /// pattern (OPC 10000-17 §6.3.2).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><c>%</c> matches zero or more characters.</description></item>
    ///   <item><description><c>_</c> matches exactly one character.</description></item>
    ///   <item><description><c>\</c> makes the next character match literally.</description></item>
    ///   <item><description><c>[list]</c> matches one character of the list; a
    ///   list holds characters and ranges such as <c>13-68</c>.</description></item>
    ///   <item><description><c>[^list]</c> matches one character not in the
    ///   list. The <c>^</c> shall be the first character inside the brackets;
    ///   the legacy <c>[!list]</c> form is accepted as well.</description></item>
    /// </list>
    /// Matching is case sensitive and covers the whole target string. A pattern
    /// with a trailing escape character, an unterminated or empty list, a
    /// descending range or a <c>^</c> that is not the first list character is
    /// not a valid search string.
    /// </remarks>
    public sealed class LikePattern
    {
        private LikePattern(string pattern, Token[] tokens)
        {
            Pattern = pattern;
            m_tokens = tokens;
        }

        /// <summary>
        /// The pattern text.
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// Parses a Like search pattern.
        /// </summary>
        /// <param name="pattern">The pattern to parse.</param>
        /// <param name="likePattern">The parsed pattern if it is valid.</param>
        /// <returns><c>true</c> if <paramref name="pattern"/> is a valid search
        /// string; <c>false</c> if it is <c>null</c> or malformed.</returns>
        public static bool TryParse(string? pattern, [NotNullWhen(true)] out LikePattern? likePattern)
        {
            likePattern = null;
            if (pattern == null)
            {
                return false;
            }

            var tokens = new List<Token>(pattern.Length);
            int ii = 0;
            while (ii < pattern.Length)
            {
                char ch = pattern[ii];
                switch (ch)
                {
                    case '%':
                        // Consecutive wildcards are equivalent to one.
                        if (tokens.Count == 0 || tokens[tokens.Count - 1].Kind != TokenKind.AnyString)
                        {
                            tokens.Add(new Token(TokenKind.AnyString));
                        }
                        ii++;
                        break;
                    case '_':
                        tokens.Add(new Token(TokenKind.AnyCharacter));
                        ii++;
                        break;
                    case '\\':
                        if (ii + 1 >= pattern.Length)
                        {
                            return false;
                        }
                        tokens.Add(new Token(pattern[ii + 1]));
                        ii += 2;
                        break;
                    case '[':
                        if (!TryParseList(pattern, ref ii, out Token list))
                        {
                            return false;
                        }
                        tokens.Add(list);
                        break;
                    default:
                        tokens.Add(new Token(ch));
                        ii++;
                        break;
                }
            }

            likePattern = new LikePattern(pattern, [.. tokens]);
            return true;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="pattern"/> is a valid Like
        /// search string.
        /// </summary>
        public static bool IsValid(string? pattern)
        {
            return TryParse(pattern, out _);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="target"/> matches the Like
        /// search string <paramref name="pattern"/>. A <c>null</c> target, a
        /// <c>null</c> pattern or an invalid pattern never matches.
        /// </summary>
        public static bool IsMatch(string? target, string? pattern)
        {
            return target != null &&
                TryParse(pattern, out LikePattern? likePattern) &&
                likePattern.IsMatch(target);
        }

        /// <summary>
        /// Returns <c>true</c> if the whole <paramref name="target"/> matches
        /// this pattern. A <c>null</c> target never matches.
        /// </summary>
        public bool IsMatch(string? target)
        {
            return IsMatch(target, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Matches the whole target string within the specified evaluation timeout.
        /// </summary>
        /// <param name="target">The target string; a null target never matches.</param>
        /// <param name="matchTimeout">A positive timeout, or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
        /// <returns>Whether the whole target matches this pattern.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive or infinite.</exception>
        /// <exception cref="TimeoutException">Evaluation exceeded the specified timeout.</exception>
        public bool IsMatch(string? target, TimeSpan matchTimeout)
        {
            if (matchTimeout <= TimeSpan.Zero && matchTimeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(matchTimeout), "The timeout must be positive or infinite.");
            }
            if (target == null)
            {
                return false;
            }
            Stopwatch? stopwatch = matchTimeout == Timeout.InfiniteTimeSpan ? null : Stopwatch.StartNew();

            // Every token other than '%' consumes exactly one character, so the
            // classic wildcard walk with a single backtrack point is sufficient:
            // on a mismatch retry from the most recent '%' one character later.
            int t = 0;
            int p = 0;
            int starToken = -1;
            int starTarget = 0;
            while (t < target.Length)
            {
                CheckTimeout(stopwatch, matchTimeout);
                if (p < m_tokens.Length && m_tokens[p].Kind == TokenKind.AnyString)
                {
                    starToken = p++;
                    starTarget = t;
                }
                else if (p < m_tokens.Length && m_tokens[p].Matches(target[t], stopwatch, matchTimeout))
                {
                    p++;
                    t++;
                }
                else if (starToken >= 0)
                {
                    p = starToken + 1;
                    t = ++starTarget;
                }
                else
                {
                    return false;
                }
            }

            while (p < m_tokens.Length && m_tokens[p].Kind == TokenKind.AnyString)
            {
                CheckTimeout(stopwatch, matchTimeout);
                p++;
            }
            return p == m_tokens.Length;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Pattern;
        }

        /// <summary>
        /// Enforces the evaluation deadline during both wildcard traversal and character-list scans.
        /// </summary>
        private static void CheckTimeout(Stopwatch? stopwatch, TimeSpan matchTimeout)
        {
            if (stopwatch != null && stopwatch.Elapsed >= matchTimeout)
            {
                throw new TimeoutException("Like-pattern evaluation exceeded its time limit.");
            }
        }

        private static bool TryParseList(string pattern, ref int index, out Token token)
        {
            token = default;
            int ii = index + 1;
            bool negated = false;
            if (ii < pattern.Length && pattern[ii] is '^' or '!')
            {
                negated = true;
                ii++;
            }

            var ranges = new List<(char First, char Last)>();
            while (true)
            {
                if (ii >= pattern.Length)
                {
                    // Unterminated list.
                    return false;
                }

                char ch = pattern[ii];
                if (ch == ']')
                {
                    ii++;
                    break;
                }

                if (ch == '^')
                {
                    // The ^ shall be the first character inside the brackets.
                    return false;
                }

                if (!TryReadListCharacter(pattern, ref ii, out char first))
                {
                    return false;
                }

                char last = first;
                if (ii + 1 < pattern.Length && pattern[ii] == '-' && pattern[ii + 1] != ']')
                {
                    ii++;
                    if (pattern[ii] == '^' || !TryReadListCharacter(pattern, ref ii, out last))
                    {
                        return false;
                    }
                    if (last < first)
                    {
                        return false;
                    }
                }
                ranges.Add((first, last));
            }

            if (ranges.Count == 0)
            {
                // An empty list matches no character.
                return false;
            }

            token = new Token(negated, [.. ranges]);
            index = ii;
            return true;
        }

        private static bool TryReadListCharacter(string pattern, ref int index, out char ch)
        {
            ch = pattern[index];
            if (ch != '\\')
            {
                index++;
                return true;
            }
            if (index + 1 >= pattern.Length)
            {
                return false;
            }
            ch = pattern[index + 1];
            index += 2;
            return true;
        }

        private enum TokenKind
        {
            Literal,
            AnyCharacter,
            AnyString,
            List
        }

        private readonly struct Token
        {
            public Token(TokenKind kind)
            {
                Kind = kind;
                m_literal = default;
                m_negated = false;
                m_ranges = null;
            }

            public Token(char literal)
            {
                Kind = TokenKind.Literal;
                m_literal = literal;
                m_negated = false;
                m_ranges = null;
            }

            public Token(bool negated, (char First, char Last)[] ranges)
            {
                Kind = TokenKind.List;
                m_literal = default;
                m_negated = negated;
                m_ranges = ranges;
            }

            public TokenKind Kind { get; }

            /// <summary>
            /// Matches one character while enforcing the shared deadline within character lists.
            /// </summary>
            public bool Matches(char ch, Stopwatch? stopwatch, TimeSpan matchTimeout)
            {
                switch (Kind)
                {
                    case TokenKind.Literal:
                        return ch == m_literal;
                    case TokenKind.AnyCharacter:
                        return true;
                    case TokenKind.List:
                        foreach ((char first, char last) in m_ranges!)
                        {
                            CheckTimeout(stopwatch, matchTimeout);
                            if (ch >= first && ch <= last)
                            {
                                return !m_negated;
                            }
                        }
                        return m_negated;
                    default:
                        return false;
                }
            }

            private readonly char m_literal;
            private readonly bool m_negated;
            private readonly (char First, char Last)[]? m_ranges;
        }

        private readonly Token[] m_tokens;
    }
}
