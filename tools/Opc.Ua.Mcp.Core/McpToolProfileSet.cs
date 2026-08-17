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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// A set of <see cref="McpToolProfile"/> values a host wants to expose,
    /// so a single MCP server can carry the tools of several bounded profiles
    /// at once.
    /// </summary>
    /// <remarks>
    /// This is the composition primitive an application uses when it needs
    /// tools from more than one profile - a vision-guided pick-and-place agent,
    /// for example, that has to both look at a camera through the
    /// <see cref="McpToolProfile.Vision"/> tools and command a robot through
    /// the <see cref="McpToolProfile.Robotics"/> tools. The set is passed to the
    /// <c>McpToolProfileSet</c> overloads of each package's
    /// <c>With…Tools</c> extension so the packages together register the tools
    /// of every selected profile exactly once - notably including the
    /// <c>ConnectionTools</c> that every session-scoped profile needs but that
    /// would otherwise be registered several times.
    /// </remarks>
    public readonly struct McpToolProfileSet : IEquatable<McpToolProfileSet>
    {
        private const char kListSeparatorComma = ',';
        private const char kListSeparatorPlus = '+';
        private const char kListSeparatorSemicolon = ';';
        private const char kListSeparatorPipe = '|';

        private readonly uint m_bits;

        /// <summary>
        /// Creates a set from a single profile.
        /// </summary>
        /// <param name="profile">The profile to include.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="profile"/> is not a defined profile.
        /// </exception>
        public McpToolProfileSet(McpToolProfile profile)
        {
            ValidateProfile(profile);
            m_bits = ToBit(profile);
        }

        /// <summary>
        /// Creates a set from a sequence of profiles. Duplicates are collapsed.
        /// </summary>
        /// <param name="profiles">The profiles to include.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="profiles"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="profiles"/> contains a value that is not a defined profile.
        /// </exception>
        public McpToolProfileSet(IEnumerable<McpToolProfile> profiles)
        {
            ArgumentNullException.ThrowIfNull(profiles);
            uint bits = 0;
            foreach (McpToolProfile profile in profiles)
            {
                ValidateProfile(profile);
                bits |= ToBit(profile);
            }
            m_bits = bits;
        }

        private McpToolProfileSet(uint bits)
        {
            m_bits = bits;
        }

        /// <summary>
        /// The empty set - no profiles selected.
        /// </summary>
        public static McpToolProfileSet Empty => default;

        /// <summary>
        /// Whether the set is empty.
        /// </summary>
        public bool IsEmpty => m_bits == 0;

        /// <summary>
        /// The number of distinct profiles in the set.
        /// </summary>
        public int Count => BitOperations.PopCount(m_bits);

        /// <summary>
        /// Whether <paramref name="profile"/> is a member of the set.
        /// </summary>
        /// <param name="profile">The profile to test for membership.</param>
        /// <returns>
        /// <c>true</c> when the set contains <paramref name="profile"/>.
        /// </returns>
        public bool Contains(McpToolProfile profile)
        {
            return Enum.IsDefined(profile) && (m_bits & ToBit(profile)) != 0;
        }

        /// <summary>
        /// Returns the same set with <paramref name="profile"/> added.
        /// </summary>
        /// <param name="profile">The profile to add.</param>
        /// <returns>A set containing the union.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="profile"/> is not a defined profile.
        /// </exception>
        public McpToolProfileSet With(McpToolProfile profile)
        {
            ValidateProfile(profile);
            return new McpToolProfileSet(m_bits | ToBit(profile));
        }

        /// <summary>
        /// Enumerates the profiles in the set in <see cref="McpToolProfile"/>
        /// declaration order.
        /// </summary>
        /// <returns>The profiles in the set.</returns>
        public IEnumerable<McpToolProfile> Enumerate()
        {
            uint bits = m_bits;
            foreach (McpToolProfile profile in Enum.GetValues<McpToolProfile>())
            {
                if ((bits & ToBit(profile)) != 0)
                {
                    yield return profile;
                }
            }
        }

        /// <summary>
        /// Parses a comma or plus separated list of profile names
        /// (case-insensitive). Duplicates are collapsed.
        /// </summary>
        /// <param name="value">The text to parse.</param>
        /// <returns>The parsed set.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="value"/> is empty or contains a token that is not
        /// a profile name.
        /// </exception>
        public static McpToolProfileSet Parse(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!TryParse(value, out McpToolProfileSet set, out string? error))
            {
                throw new FormatException(error);
            }
            return set;
        }

        /// <summary>
        /// Tries to parse a comma or plus separated list of profile names
        /// (case-insensitive). Duplicates are collapsed.
        /// </summary>
        /// <param name="value">The text to parse.</param>
        /// <param name="set">The parsed set on success.</param>
        /// <returns>
        /// <c>true</c> when the text is a well-formed list of known profile
        /// names; <c>false</c> when the text is <c>null</c>, empty, or names
        /// an unknown profile.
        /// </returns>
        public static bool TryParse(
            [NotNullWhen(true)] string? value,
            out McpToolProfileSet set)
        {
            return TryParse(value, out set, out _);
        }

        /// <summary>
        /// Tries to parse a comma or plus separated list of profile names
        /// (case-insensitive) and reports the parse error on failure.
        /// </summary>
        /// <param name="value">The text to parse.</param>
        /// <param name="set">The parsed set on success.</param>
        /// <param name="error">The parse error on failure.</param>
        /// <returns>
        /// <c>true</c> when the text is a well-formed list of known profile
        /// names.
        /// </returns>
        public static bool TryParse(
            [NotNullWhen(true)] string? value,
            out McpToolProfileSet set,
            [NotNullWhen(false)] out string? error)
        {
            set = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "The tool profile list is empty.";
                return false;
            }

            uint bits = 0;
            string[] tokens = value.Split(
                [
                    kListSeparatorComma,
                    kListSeparatorPlus,
                    kListSeparatorSemicolon,
                    kListSeparatorPipe,
                    ' ',
                    '\t'
                ],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                error = "The tool profile list is empty.";
                return false;
            }

            foreach (string token in tokens)
            {
                if (!Enum.TryParse(token, ignoreCase: true, out McpToolProfile profile) ||
                    !Enum.IsDefined(profile))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "Unknown MCP tool profile '{0}'. Valid profiles: {1}.",
                        token,
                        string.Join(", ", Enum.GetNames<McpToolProfile>()));
                    return false;
                }
                bits |= ToBit(profile);
            }

            set = new McpToolProfileSet(bits);
            error = null;
            return true;
        }

        /// <summary>
        /// Renders the set as a comma-separated list of profile names in
        /// <see cref="McpToolProfile"/> declaration order.
        /// </summary>
        /// <returns>The text form of the set.</returns>
        public override string ToString()
        {
            if (m_bits == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (McpToolProfile profile in Enumerate())
            {
                if (builder.Length > 0)
                {
                    builder.Append(kListSeparatorComma);
                }
                builder.Append(profile.ToString());
            }
            return builder.ToString();
        }

        /// <inheritdoc/>
        public bool Equals(McpToolProfileSet other)
        {
            return m_bits == other.m_bits;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is McpToolProfileSet other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return unchecked((int)m_bits);
        }

        /// <summary>
        /// Whether two sets contain the same profiles.
        /// </summary>
        public static bool operator ==(McpToolProfileSet left, McpToolProfileSet right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Whether two sets contain different profiles.
        /// </summary>
        public static bool operator !=(McpToolProfileSet left, McpToolProfileSet right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Implicit conversion from a single profile so an existing
        /// single-profile call site can be re-typed to
        /// <see cref="McpToolProfileSet"/> without changing its intent.
        /// </summary>
        /// <param name="profile">The profile to wrap in a set.</param>
        public static implicit operator McpToolProfileSet(McpToolProfile profile)
        {
            return new McpToolProfileSet(profile);
        }

        internal uint Bits => m_bits;

        private static uint ToBit(McpToolProfile profile)
        {
            return 1u << (int)profile;
        }

        private static void ValidateProfile(McpToolProfile profile)
        {
            if (!Enum.IsDefined(profile))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    profile,
                    "Unknown MCP tool profile.");
            }
        }
    }
}
