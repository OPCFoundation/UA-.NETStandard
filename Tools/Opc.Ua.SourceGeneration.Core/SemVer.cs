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
using System.Globalization;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Lenient (major, minor, patch) version used for dependency resolution.
    /// Parses OPC-UA-style strings such as <c>"1.05.07"</c>, <c>"1.5.7"</c>,
    /// <c>"v105"</c>, <c>"1.5"</c> or <c>"1.5.7-rc1"</c>. Pre-release suffixes
    /// are detected and cause the value to sort below the corresponding release.
    /// </summary>
    /// <remarks>
    /// This is not a full SemVer 2.0 implementation. It is intentionally
    /// permissive so that it can consume version strings from heterogeneous
    /// OPC UA models without failing the build.
    /// </remarks>
    internal readonly struct SemVer : IEquatable<SemVer>, IComparable<SemVer>
    {
        /// <summary>
        /// Sentinel representing "no version declared". Compares as less than
        /// every parseable version; two missing versions compare equal.
        /// </summary>
        public static readonly SemVer Unspecified = default;

        private static readonly char[] s_prereleaseSeparators = ['-', '+'];

        private SemVer(int major, int minor, int patch, bool hasValue, bool isPrerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            HasValue = hasValue;
            IsPrerelease = isPrerelease;
        }

        /// <summary>Major component (X in X.Y.Z).</summary>
        public int Major { get; }

        /// <summary>Minor component (Y in X.Y.Z).</summary>
        public int Minor { get; }

        /// <summary>Patch component (Z in X.Y.Z); zero when the source omits it.</summary>
        public int Patch { get; }

        /// <summary>True when the version was present and parseable.</summary>
        public bool HasValue { get; }

        /// <summary>True when the original string carried a pre-release tag (e.g. <c>-rc1</c>).</summary>
        public bool IsPrerelease { get; }

        /// <summary>
        /// Tries to parse a version string. Returns <see cref="Unspecified"/> and
        /// <c>false</c> when the input is null, empty, or unparseable.
        /// </summary>
        public static bool TryParse(string text, out SemVer value)
        {
            value = Unspecified;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // Strip a leading "v"/"V" prefix.
            string s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            {
                s = s.Substring(1);
            }

            // Detach any pre-release tag after '-' or '+'.
            bool isPrerelease = false;
            int tagAt = s.IndexOfAny(s_prereleaseSeparators);
            if (tagAt >= 0)
            {
                isPrerelease = true;
                s = s.Substring(0, tagAt);
            }

            if (s.Length == 0)
            {
                return false;
            }

            // Split on '.'; int.Parse ignores leading zeros.
            string[] parts = s.Split('.');

            // Compact "v105" form has no dots: treat as condensed digit form when
            // the length makes decomposition unambiguous.
            if (parts.Length == 1 && IsAllDigits(parts[0]))
            {
                return TryParseCondensedForm(parts[0], isPrerelease, out value);
            }

            int major = 0;
            int minor = 0;
            int patch = 0;
            if (parts.Length > 0 && !TryParseComponent(parts[0], out major))
            {
                return false;
            }
            if (parts.Length > 1 && !TryParseComponent(parts[1], out minor))
            {
                return false;
            }
            if (parts.Length > 2 && !TryParseComponent(parts[2], out patch))
            {
                return false;
            }
            // parts.Length > 3 is tolerated; trailing components are ignored.

            value = new SemVer(major, minor, patch, hasValue: true, isPrerelease: isPrerelease);
            return true;
        }

        /// <summary>Parses or throws.</summary>
        public static SemVer Parse(string text)
        {
            if (!TryParse(text, out SemVer v))
            {
                throw new FormatException("Unparseable version string: '" + text + "'");
            }
            return v;
        }

        /// <summary>True when the two values share a major component. Unspecified values never match.</summary>
        public bool SameMajor(SemVer other)
        {
            return HasValue && other.HasValue && Major == other.Major;
        }

        /// <inheritdoc/>
        public int CompareTo(SemVer other)
        {
            // Unspecified sorts below everything; two Unspecifieds are equal.
            if (!HasValue)
            {
                return other.HasValue ? -1 : 0;
            }
            if (!other.HasValue)
            {
                return 1;
            }
            int c = Major.CompareTo(other.Major);
            if (c != 0)
            {
                return c;
            }
            c = Minor.CompareTo(other.Minor);
            if (c != 0)
            {
                return c;
            }
            c = Patch.CompareTo(other.Patch);
            if (c != 0)
            {
                return c;
            }
            // Pre-release sorts below its release counterpart.
            if (IsPrerelease == other.IsPrerelease)
            {
                return 0;
            }
            return IsPrerelease ? -1 : 1;
        }

        /// <inheritdoc/>
        public bool Equals(SemVer other)
        {
            return HasValue == other.HasValue
                && Major == other.Major
                && Minor == other.Minor
                && Patch == other.Patch
                && IsPrerelease == other.IsPrerelease;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is SemVer v && Equals(v);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (!HasValue)
            {
                return 0;
            }
            unchecked
            {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                hash = (hash * 397) ^ (IsPrerelease ? 1 : 0);
                return hash;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (!HasValue)
            {
                return "<unspecified>";
            }
            string core = Major.ToString(CultureInfo.InvariantCulture) + "." +
                          Minor.ToString(CultureInfo.InvariantCulture) + "." +
                          Patch.ToString(CultureInfo.InvariantCulture);
            return IsPrerelease ? core + "-pre" : core;
        }

        public static bool operator ==(SemVer left, SemVer right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SemVer left, SemVer right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(SemVer left, SemVer right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(SemVer left, SemVer right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(SemVer left, SemVer right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(SemVer left, SemVer right)
        {
            return left.CompareTo(right) >= 0;
        }

        private static bool TryParseSlice(string source, int start, int length, out int value)
        {
#if NET || NETSTANDARD2_1_OR_GREATER
            return int.TryParse(source.AsSpan(start, length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
#else
            return int.TryParse(source.Substring(start, length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
#endif
        }

        private static bool TryParseComponent(string text, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
            {
                return true;
            }
            value = 0;
            return false;
        }

        private static bool IsAllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryParseCondensedForm(string digits, bool isPrerelease, out SemVer value)
        {
            // "105" -> 1.05.0 -> (1, 5, 0). "10506" -> (1, 5, 6).
            // Only 3-digit and 5-digit forms are decomposed; anything else is
            // treated as a bare major integer.
            if (digits.Length == 3)
            {
                int major = digits[0] - '0';
                if (TryParseSlice(digits, 1, 2, out int minor))
                {
                    value = new SemVer(major, minor, 0, hasValue: true, isPrerelease: isPrerelease);
                    return true;
                }
            }
            else if (digits.Length == 5)
            {
                int major = digits[0] - '0';
                if (TryParseSlice(digits, 1, 2, out int minor) &&
                    TryParseSlice(digits, 3, 2, out int patch))
                {
                    value = new SemVer(major, minor, patch, hasValue: true, isPrerelease: isPrerelease);
                    return true;
                }
            }

            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maj))
            {
                value = new SemVer(maj, 0, 0, hasValue: true, isPrerelease: isPrerelease);
                return true;
            }
            value = Unspecified;
            return false;
        }
    }
}
