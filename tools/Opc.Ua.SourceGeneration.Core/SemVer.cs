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
    /// sort below the corresponding release and are ordered among each other
    /// by the SemVer 2.0 section 11 identifier rules.
    /// </summary>
    /// <remarks>
    /// Parsing is intentionally more permissive than SemVer 2.0 (a "v" prefix,
    /// leading zeros, a missing or a fourth component, the condensed "105"
    /// form) so that it can consume version strings from heterogeneous OPC UA
    /// models without failing the build. Precedence follows SemVer 2.0 section
    /// 11 for everything SemVer defines; build metadata is ignored (section 10).
    /// </remarks>
    internal readonly struct SemVer : IEquatable<SemVer>, IComparable<SemVer>
    {
        /// <summary>
        /// Sentinel representing "no version declared". Compares as less than
        /// every parseable version; two missing versions compare equal.
        /// </summary>
        public static readonly SemVer Unspecified;

        private SemVer(
            int major,
            int minor,
            int patch,
            bool hasValue,
            string prerelease,
            int revision = 0)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Revision = revision;
            HasValue = hasValue;
            Prerelease = prerelease;
        }

        /// <summary>Major component (X in X.Y.Z).</summary>
        public int Major { get; }

        /// <summary>Minor component (Y in X.Y.Z).</summary>
        public int Minor { get; }

        /// <summary>Patch component (Z in X.Y.Z); zero when the source omits it.</summary>
        public int Patch { get; }

        /// <summary>
        /// Fourth (revision) component of a four-part version such as
        /// <c>"1.0.0.2"</c>; zero when the source omits it.
        /// </summary>
        public int Revision { get; }

        /// <summary>True when the version was present and parseable.</summary>
        public bool HasValue { get; }

        /// <summary>True when the original string carried a pre-release tag (e.g. <c>-rc1</c>).</summary>
        public bool IsPrerelease => Prerelease != null;

        /// <summary>
        /// The pre-release identifiers after the '-' (e.g. <c>"alpha.1"</c>);
        /// null for a release.
        /// </summary>
        public string Prerelease { get; }

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
                s = s[1..];
            }

            // Build metadata after '+' carries no precedence (SemVer 2.0 §10):
            // drop it without marking the version a pre-release. It comes last,
            // so a '-' inside it ("1.0+build-5") is not a pre-release tag either.
            int buildAt = s.IndexOf('+', StringComparison.Ordinal);
            if (buildAt >= 0)
            {
                s = s[..buildAt];
            }

            // Detach any pre-release tag after the first '-'; the identifiers
            // themselves may contain '-' (SemVer 2.0 section 9).
            string? prerelease = null;
            int tagAt = s.IndexOf('-', StringComparison.Ordinal);
            if (tagAt >= 0)
            {
                prerelease = s[(tagAt + 1)..];
                s = s[..tagAt];
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
                return TryParseCondensedForm(parts[0], prerelease!, out value);
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
            int revision = 0;
            if (parts.Length > 3 && !TryParseComponent(parts[3], out revision))
            {
                return false;
            }
            // parts.Length > 4 is tolerated; trailing components are ignored.

            value = new SemVer(
                major,
                minor,
                patch,
                hasValue: true,
                prerelease: prerelease!,
                revision: revision);
            return true;
        }

        /// <summary>
        /// Orders two model version strings. A model that declares no version
        /// carries its publication date instead, and a date must not go through
        /// <see cref="TryParse"/>: "2024-05-01" loses its month and day to the
        /// pre-release split and comes back as major 2024, which outranks every
        /// real version. Dates are therefore compared as dates, versions as
        /// versions, and a date against a version is reported as equal - the two
        /// are not comparable, and guessing would promote a stale model. Callers
        /// break that tie on the publication date.
        /// </summary>
        public static int CompareVersionStrings(string left, string right)
        {
            bool leftIsDate = IsIsoDate(left);
            bool rightIsDate = IsIsoDate(right);

            if (leftIsDate || rightIsDate)
            {
                // ISO dates sort correctly as ordinal text.
                return leftIsDate && rightIsDate ? string.CompareOrdinal(left, right) : 0;
            }

            bool leftParsed = TryParse(left, out SemVer leftVersion);
            bool rightParsed = TryParse(right, out SemVer rightVersion);

            if (leftParsed && rightParsed)
            {
                return leftVersion.CompareTo(rightVersion);
            }

            // Unspecified sorts below everything, which is not what an
            // unparseable string means - fall back to the ordinal order.
            return string.CompareOrdinal(left, right);
        }

        /// <summary>
        /// Orders two model version strings with a total order, so that picking
        /// the newest of several candidates does not depend on the order they
        /// are visited in. <see cref="CompareVersionStrings"/> reports a date
        /// against a version as equal, and a caller that breaks that tie on
        /// something else (the publication date) ends up with a non-transitive
        /// comparison. Here the kind of version ranks first - a parseable version
        /// above an unparseable one, above a bare date (a model that declared no
        /// version), above nothing at all - and only values of the same kind are
        /// compared with <see cref="CompareVersionStrings"/>.
        /// </summary>
        public static int CompareVersionStringsTotal(string left, string right)
        {
            int leftKind = GetVersionKind(left);
            int cmp = leftKind.CompareTo(GetVersionKind(right));
            if (cmp != 0 || leftKind == 0)
            {
                return cmp;
            }
            return CompareVersionStrings(left, right);
        }

        /// <summary>
        /// Orders two editions of the same model the way OPC 10000-6 F.2
        /// (Table F.1) prescribes. The ModelVersion (a SemVer 2.0 string) is
        /// compared first: when both declare one it decides, when only one
        /// declares one that one is newer. A tie, or two models without a
        /// ModelVersion, is settled by the PublicationDate. The ModelTableEntry
        /// Version is "not intended for programmatic comparisons", so it only
        /// breaks what is left as a last heuristic. Every step compares a key of
        /// its own, so the order is total and the newest of several candidates
        /// does not depend on the order they are visited in.
        /// </summary>
        /// <param name="leftModelVersion">ModelVersion of the left model, or null.</param>
        /// <param name="leftPublicationDate">PublicationDate of the left model
        /// (<see cref="DateTime.MinValue"/> when absent).</param>
        /// <param name="leftVersion">Version label of the left model, or null.</param>
        /// <param name="rightModelVersion">ModelVersion of the right model, or null.</param>
        /// <param name="rightPublicationDate">PublicationDate of the right model.</param>
        /// <param name="rightVersion">Version label of the right model, or null.</param>
        public static int CompareModels(
            string leftModelVersion,
            DateTime leftPublicationDate,
            string leftVersion,
            string rightModelVersion,
            DateTime rightPublicationDate,
            string rightVersion)
        {
            // A missing ModelVersion ranks below any declared one (kind 0 in
            // the total order), which is the "only one has a ModelVersion" rule.
            int cmp = CompareVersionStringsTotal(leftModelVersion, rightModelVersion);
            if (cmp != 0)
            {
                return cmp;
            }
            cmp = DateTime.Compare(
                leftPublicationDate.ToUniversalTime(),
                rightPublicationDate.ToUniversalTime());
            if (cmp != 0)
            {
                return cmp;
            }
            return CompareVersionStringsTotal(leftVersion, rightVersion);
        }

        /// <summary>
        /// 0 = missing, 1 = ISO date, 2 = unparseable text, 3 = parseable version.
        /// </summary>
        private static int GetVersionKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }
            if (IsIsoDate(value))
            {
                return 1;
            }
            return TryParse(value, out _) ? 3 : 2;
        }

        /// <summary>
        /// True when the text is a bare ISO-8601 date, the form a model version
        /// takes when it falls back to the publication date.
        /// </summary>
        public static bool IsIsoDate(string value)
        {
            return value != null &&
                DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _);
        }

        /// <summary>Parses or throws.</summary>
        /// <exception cref="FormatException"></exception>
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
            c = Revision.CompareTo(other.Revision);
            if (c != 0)
            {
                return c;
            }
            // Pre-release sorts below its release counterpart (SemVer 2.0 section 11.3).
            if (!IsPrerelease || !other.IsPrerelease)
            {
                if (IsPrerelease == other.IsPrerelease)
                {
                    return 0;
                }
                return IsPrerelease ? -1 : 1;
            }
            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        /// <summary>
        /// SemVer 2.0 section 11.4: pre-release versions are compared identifier
        /// by identifier (dot separated). Numeric identifiers compare
        /// numerically, alphanumeric identifiers in ASCII order, a numeric
        /// identifier ranks below an alphanumeric one, and when all shared
        /// identifiers are equal the version with fewer identifiers ranks lower.
        /// </summary>
        internal static int ComparePrerelease(string left, string right)
        {
            string[] leftIds = left.Split('.');
            string[] rightIds = right.Split('.');
            int count = Math.Min(leftIds.Length, rightIds.Length);
            for (int i = 0; i < count; i++)
            {
                int c = ComparePrereleaseIdentifier(leftIds[i], rightIds[i]);
                if (c != 0)
                {
                    return c;
                }
            }
            return leftIds.Length.CompareTo(rightIds.Length);
        }

        private static int ComparePrereleaseIdentifier(string left, string right)
        {
            bool leftNumeric = IsAsciiDigits(left);
            bool rightNumeric = IsAsciiDigits(right);
            if (leftNumeric && rightNumeric)
            {
                // Numerically and without overflow: drop leading zeros (SemVer
                // forbids them, the lenient parser tolerates them); then the
                // longer digit string is the larger number.
                string l = left.TrimStart('0');
                string r = right.TrimStart('0');
                int c = l.Length.CompareTo(r.Length);
                return c != 0 ? c : Math.Sign(string.CompareOrdinal(l, r));
            }
            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }
            return Math.Sign(string.CompareOrdinal(left, right));
        }

        private static bool IsAsciiDigits(string s)
        {
            if (s.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] is < '0' or > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(SemVer other)
        {
            // Equal precedence: the pre-release identifiers "01" and "1" are
            // the same number, so compare them rather than match the text.
            return HasValue == other.HasValue && CompareTo(other) == 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
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
                hash = (hash * 397) ^ Revision;
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
            string core = Major.ToString(CultureInfo.InvariantCulture) +
                "." +
                Minor.ToString(CultureInfo.InvariantCulture) +
                "." +
                Patch.ToString(CultureInfo.InvariantCulture);
            if (Revision != 0)
            {
                core += "." + Revision.ToString(CultureInfo.InvariantCulture);
            }
            return IsPrerelease ? core + "-" + Prerelease : core;
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
#if NET
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

        private static bool TryParseCondensedForm(string digits, string prerelease, out SemVer value)
        {
            // "105" -> 1.05.0 -> (1, 5, 0). "10506" -> (1, 5, 6).
            // Only 3-digit and 5-digit forms are decomposed; anything else is
            // treated as a bare major integer.
            if (digits.Length == 3)
            {
                int major = digits[0] - '0';
                if (TryParseSlice(digits, 1, 2, out int minor))
                {
                    value = new SemVer(major, minor, 0, hasValue: true, prerelease: prerelease);
                    return true;
                }
            }
            else if (digits.Length == 5)
            {
                int major = digits[0] - '0';
                if (TryParseSlice(digits, 1, 2, out int minor) &&
                    TryParseSlice(digits, 3, 2, out int patch))
                {
                    value = new SemVer(major, minor, patch, hasValue: true, prerelease: prerelease);
                    return true;
                }
            }

            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maj))
            {
                value = new SemVer(maj, 0, 0, hasValue: true, prerelease: prerelease);
                return true;
            }
            value = Unspecified;
            return false;
        }
    }
}
