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
 *
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

// Adapted from xregistry-dotnet (MIT), copyright (c) 2026 xregistry-dotnet contributors.

using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Opc.Ua.Wot
{

    /// <summary>
    /// A validated SemVer 2.0.0 version, ordered by SemVer precedence.
    /// </summary>
    public sealed class WotSemanticVersion : IComparable<WotSemanticVersion>, IEquatable<WotSemanticVersion>
    {
        private readonly BigInteger m_major;
        private readonly BigInteger m_minor;
        private readonly BigInteger m_patch;
        private readonly string[]? m_prerelease;

        private WotSemanticVersion(BigInteger major, BigInteger minor, BigInteger patch, string[]? prerelease)
        {
            m_major = major;
            m_minor = minor;
            m_patch = patch;
            m_prerelease = prerelease;
        }

        /// <summary>
        /// Gets whether this version contains prerelease identifiers.
        /// </summary>
        public bool IsPrerelease => m_prerelease is not null;

        internal bool HasSameMajor(WotSemanticVersion other) => m_major == other.m_major;

        /// <summary>
        /// Parses a SemVer 2.0.0 string without build metadata and within Core's identifier limit.
        /// </summary>
        /// <exception cref="ArgumentException">The input is not a valid version identifier.</exception>
        public static WotSemanticVersion Parse(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (!TryParse(value, out var parsed))
            {
                throw new ArgumentException("The value is not a SemVer 2.0.0 identifier.", nameof(value));
            }

            return parsed!;
        }

        /// <summary>
        /// Attempts to parse SemVer 2.0.0 without build metadata and within Core's identifier limit.
        /// </summary>
        public static bool TryParse(string? value, out WotSemanticVersion? version) => TryParse(value, enforceLength: true, out version);

        /// <inheritdoc />
        public int CompareTo(WotSemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            var comparison = m_major.CompareTo(other.m_major);
            if (comparison == 0)
            {
                comparison = m_minor.CompareTo(other.m_minor);
            }

            if (comparison == 0)
            {
                comparison = m_patch.CompareTo(other.m_patch);
            }

            if (comparison != 0)
            {
                return comparison;
            }

            if (m_prerelease is null || other.m_prerelease is null)
            {
                return m_prerelease is null ? other.m_prerelease is null ? 0 : 1 : -1;
            }

            for (var index = 0; index < Math.Min(m_prerelease.Length, other.m_prerelease.Length); index++)
            {
                var left = m_prerelease[index];
                var right = other.m_prerelease[index];
                var leftNumeric = IsNumeric(left);
                var rightNumeric = IsNumeric(right);
                comparison = leftNumeric && rightNumeric
                    ? BigInteger.Parse(left, CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(right, CultureInfo.InvariantCulture))
                    : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(left, right);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return m_prerelease.Length.CompareTo(other.m_prerelease.Length);
        }

        /// <inheritdoc />
        public bool Equals(WotSemanticVersion? other) => other is not null && CompareTo(other) == 0;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is WotSemanticVersion other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_major);
            hash.Add(m_minor);
            hash.Add(m_patch);
            if (m_prerelease is not null)
            {
                foreach (var identifier in m_prerelease)
                {
                    hash.Add(identifier, StringComparer.Ordinal);
                }
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Compares SemVer precedence for two versions.
        /// </summary>
        public static bool operator ==(WotSemanticVersion? left, WotSemanticVersion? right) => Equals(left, right);

        /// <summary>
        /// Compares SemVer precedence for two versions.
        /// </summary>
        public static bool operator !=(WotSemanticVersion? left, WotSemanticVersion? right) => !Equals(left, right);

        /// <summary>
        /// Tests whether the left version has lower SemVer precedence.
        /// </summary>
        public static bool operator <(WotSemanticVersion left, WotSemanticVersion right) => left.CompareTo(right) < 0;

        /// <summary>
        /// Tests whether the left version has lower or equal SemVer precedence.
        /// </summary>
        public static bool operator <=(WotSemanticVersion left, WotSemanticVersion right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// Tests whether the left version has higher SemVer precedence.
        /// </summary>
        public static bool operator >(WotSemanticVersion left, WotSemanticVersion right) => left.CompareTo(right) > 0;

        /// <summary>
        /// Tests whether the left version has higher or equal SemVer precedence.
        /// </summary>
        public static bool operator >=(WotSemanticVersion left, WotSemanticVersion right) => left.CompareTo(right) >= 0;
        internal static bool IsSemVerSyntax(string? value) => TryParse(value, enforceLength: false, out _);

        internal static bool HasValidBuildMetadata(string? value)
        {
            if (value is null)
            {
                return false;
            }

            var separator = value.AsSpan().IndexOf('+');
            return separator > 0 && separator == value.AsSpan().LastIndexOf('+') &&
                TryParse(value[..separator], enforceLength: false, out _) && IsIdentifierList(value[(separator + 1)..], allowNumericLeadingZero: true);
        }

        private static bool TryParse(string? value, bool enforceLength, out WotSemanticVersion? version)
        {
            version = null;
            if (string.IsNullOrEmpty(value) || enforceLength && value.Length > 128)
            {
                return false;
            }

            var separator = value.AsSpan().IndexOf('-');
            var core = separator < 0 ? value : value[..separator];
            var components = core.Split('.');
            if (components.Length != 3 || !TryNumber(components[0], out var major) ||
                !TryNumber(components[1], out var minor) || !TryNumber(components[2], out var patch))
            {
                return false;
            }

            string[]? prerelease = null;
            if (separator >= 0)
            {
                var text = value[(separator + 1)..];
                if (!IsIdentifierList(text, allowNumericLeadingZero: false))
                {
                    return false;
                }

                prerelease = text.Split('.');
            }

            version = new(major, minor, patch, prerelease);
            return true;
        }

        private static bool TryNumber(string value, out BigInteger number)
        {
            number = BigInteger.Zero;
            return value.Length > 0 && (value.Length == 1 || value[0] != '0') && value.All(IsAsciiDigit) &&
                BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        private static bool IsIdentifierList(string value, bool allowNumericLeadingZero)
        {
            var identifiers = value.Split('.');
            return identifiers.All(identifier => identifier.Length > 0 && identifier.All(static character =>
                IsAsciiLetterOrDigit(character) || character == '-') &&
                (allowNumericLeadingZero || !IsNumeric(identifier) || identifier.Length == 1 || identifier[0] != '0'));
        }

        private static bool IsNumeric(string value) => value.Length > 0 && value.All(IsAsciiDigit);
        private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

        private static bool IsAsciiLetterOrDigit(char value) =>
            IsAsciiDigit(value) || value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    }
}
