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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The RFC 8785 JSON Canonicalization Scheme (JCS), which is the form two
    /// JSON <em>values</em> are compared in when the WoT Binding asks whether
    /// they are the same value (Section 9.4, Annex G).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately not the form anything is <em>digested</em> in. The
    /// residue digest of Section 10.2 is taken over the retained bytes exactly,
    /// because a residue member carries a value Section 6.6 forbids a consumer
    /// to reformat; and the opaque-object size bound of Section 6.6 is measured
    /// over the compact received form of Annex G.4. Canonicalization answers a
    /// third question - "are these two values equal?" - and answering it by
    /// comparing serializations is what makes a reordered object, an equivalent
    /// escape or <c>1.0</c> beside <c>1</c> stop being a conflict.
    /// </para>
    /// <para>
    /// The scheme is implemented here rather than taken from a package: it is
    /// a page of rules, it has to hold on every target framework this library
    /// builds for including the ones without a shortest-round-trip
    /// <c>double</c> formatter, and it has to be Native AOT clean. The rules
    /// are:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Object members are sorted by the UTF-16 code units of their names
    /// (ordinal comparison) and an object that repeats a name is rejected: JCS
    /// is defined over values, and a repeated name is two values.
    /// </description></item>
    /// <item><description>
    /// Arrays keep the order they were written in, because order is part of an
    /// array's value.
    /// </description></item>
    /// <item><description>
    /// Strings carry the minimal escaping of ECMAScript <c>JSON.stringify</c>:
    /// the quote, the backslash and the seven short forms, every other C0
    /// control as a lower-case <c>\u00xx</c>, and everything else - non-ASCII
    /// included - literally. No HTML-sensitive character is escaped, because
    /// escaping one would make two spellings of one string.
    /// </description></item>
    /// <item><description>
    /// Numbers are written in the ECMAScript <c>Number::toString</c> form of
    /// their IEEE-754 double value, so <c>1.0</c>, <c>1</c>, <c>1e0</c> and
    /// <c>1.0e+00</c> are one number, <c>-0</c> is <c>0</c>, and the exponent
    /// thresholds are the ones ECMAScript states rather than a platform's.
    /// </description></item>
    /// </list>
    /// <para>
    /// A number outside the interoperable domain of RFC 8259 Section 6 is
    /// <em>diagnosed</em> rather than canonicalized: a literal that carries
    /// more precision than an IEEE-754 double can hold - <c>9007199254740993</c>
    /// beside <c>9007199254740992</c> - would otherwise be reported equal to a
    /// value it is not equal to. A caller that cannot canonicalize compares the
    /// values some other, conservative way; it never treats the failure as
    /// equality.
    /// </para>
    /// </remarks>
    internal static class WotJsonCanonicalizer
    {
        /// <summary>
        /// The nesting depth this canonicalizer accepts. A value is compared
        /// after it was parsed, so the parser's own limit has already been
        /// applied; this bounds the recursion of a value assembled in memory.
        /// </summary>
        public const int MaxDepth = 256;

        /// <summary>
        /// Canonicalizes a parsed JSON value.
        /// </summary>
        /// <param name="element">The value.</param>
        /// <param name="canonical">The RFC 8785 canonical form.</param>
        /// <param name="error">Why the value could not be canonicalized.</param>
        /// <returns><c>true</c> when the value was canonicalized.</returns>
        public static bool TryCanonicalize(
            JsonElement element, out string canonical, out string error)
        {
            var text = new StringBuilder();
            if (!Write(text, element, 0, out error))
            {
                canonical = string.Empty;
                return false;
            }
            canonical = text.ToString();
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Canonicalizes a JSON value held as a mutable node.
        /// </summary>
        /// <remarks>
        /// The node is written and re-read rather than walked directly: a
        /// <see cref="JsonNode"/> holds a number as whatever CLR value produced
        /// it, and the round trip gives one lexical form to canonicalize from.
        /// </remarks>
        /// <param name="node">The value, or <c>null</c> for JSON null.</param>
        /// <param name="canonical">The RFC 8785 canonical form.</param>
        /// <param name="error">Why the value could not be canonicalized.</param>
        /// <returns><c>true</c> when the value was canonicalized.</returns>
        public static bool TryCanonicalize(
            JsonNode? node, out string canonical, out string error)
        {
            if (node is null)
            {
                canonical = "null";
                error = string.Empty;
                return true;
            }
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(
                    node.ToJsonString(),
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = MaxDepth
                    });
            }
            catch (JsonException ex)
            {
                canonical = string.Empty;
                error = "The value could not be read as JSON: " + ex.Message;
                return false;
            }
            using (document)
            {
                return TryCanonicalize(document.RootElement, out canonical, out error);
            }
        }

        /// <summary>
        /// Canonicalizes a parsed JSON value into UTF-8, which is the octet
        /// form RFC 8785 defines.
        /// </summary>
        /// <param name="element">The value.</param>
        /// <param name="utf8">The canonical UTF-8 octets.</param>
        /// <param name="error">Why the value could not be canonicalized.</param>
        /// <returns><c>true</c> when the value was canonicalized.</returns>
        public static bool TryGetUtf8(
            JsonElement element, out byte[] utf8, out string error)
        {
            if (!TryCanonicalize(element, out string canonical, out error))
            {
                utf8 = Array.Empty<byte>();
                return false;
            }
            utf8 = new UTF8Encoding(false).GetBytes(canonical);
            return true;
        }

        /// <summary>
        /// Determines whether two JSON values are the same value under RFC 8785.
        /// </summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <param name="equal">Whether the two values are equal.</param>
        /// <param name="error">
        /// Why the comparison could not be made under the scheme.
        /// </param>
        /// <returns>
        /// <c>true</c> when both values could be canonicalized, so
        /// <paramref name="equal"/> is the answer the scheme gives.
        /// </returns>
        public static bool TryEquals(
            JsonNode? left, JsonNode? right, out bool equal, out string error)
        {
            equal = false;
            if (!TryCanonicalize(left, out string leftText, out error) ||
                !TryCanonicalize(right, out string rightText, out error))
            {
                return false;
            }
            equal = string.Equals(leftText, rightText, StringComparison.Ordinal);
            return true;
        }

        /// <summary>
        /// Writes the ECMAScript <c>Number::toString</c> form of a JSON number
        /// literal (RFC 8785 Section 3.2.2.3).
        /// </summary>
        /// <param name="literal">The number as it was written.</param>
        /// <param name="formatted">The canonical form.</param>
        /// <param name="error">Why the literal is not an interoperable number.</param>
        /// <returns><c>true</c> when the literal was canonicalized.</returns>
        public static bool TryFormatNumber(
            string literal, out string formatted, out string error)
        {
            formatted = string.Empty;
            if (literal is null)
            {
                error = "A JSON number literal is required.";
                return false;
            }
            if (!double.TryParse(
                    literal,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                error = $"The number '{literal}' is outside the interoperable domain of " +
                    "RFC 8259 Section 6: it is not a finite IEEE-754 double.";
                return false;
            }

            formatted = FormatDouble(value);
            if (!SameDecimalValue(literal, formatted))
            {
                // The literal names a value the double it parsed to is not: two
                // such literals would canonicalize to one string and be reported
                // equal although they are two numbers.
                error = $"The number '{literal}' is outside the interoperable domain of " +
                    "RFC 8259 Section 6: it carries more precision than an IEEE-754 double " +
                    $"holds, and the nearest double is '{formatted}'.";
                formatted = string.Empty;
                return false;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Appends the minimally escaped JSON string form of a value
        /// (RFC 8785 Section 3.2.2.2).
        /// </summary>
        /// <param name="text">The destination.</param>
        /// <param name="value">The string value.</param>
        public static void AppendString(StringBuilder text, string value)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            text.Append('"');
            for (int ii = 0; ii < value.Length; ii++)
            {
                char unit = value[ii];
                switch (unit)
                {
                    case '"':
                        text.Append("\\\"");
                        continue;
                    case '\\':
                        text.Append("\\\\");
                        continue;
                    case '\b':
                        text.Append("\\b");
                        continue;
                    case '\f':
                        text.Append("\\f");
                        continue;
                    case '\n':
                        text.Append("\\n");
                        continue;
                    case '\r':
                        text.Append("\\r");
                        continue;
                    case '\t':
                        text.Append("\\t");
                        continue;
                    default:
                        break;
                }
                if (unit < 0x20)
                {
                    AppendCodeUnit(text, unit);
                    continue;
                }
                if (char.IsHighSurrogate(unit))
                {
                    if (ii + 1 < value.Length && char.IsLowSurrogate(value[ii + 1]))
                    {
                        text.Append(unit).Append(value[ii + 1]);
                        ii++;
                        continue;
                    }
                    // A lone surrogate is not a character and cannot be encoded;
                    // it is escaped, which is what a well-formed JSON.stringify
                    // does and what keeps the canonical form encodable.
                    AppendCodeUnit(text, unit);
                    continue;
                }
                if (char.IsLowSurrogate(unit))
                {
                    AppendCodeUnit(text, unit);
                    continue;
                }
                text.Append(unit);
            }
            text.Append('"');
        }

        private static bool Write(
            StringBuilder text, JsonElement element, int depth, out string error)
        {
            if (depth > MaxDepth)
            {
                error = $"The value nests deeper than the {MaxDepth} levels this " +
                    "canonicalizer accepts.";
                return false;
            }
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return WriteObject(text, element, depth, out error);
                case JsonValueKind.Array:
                    return WriteArray(text, element, depth, out error);
                case JsonValueKind.String:
                    AppendString(text, element.GetString() ?? string.Empty);
                    error = string.Empty;
                    return true;
                case JsonValueKind.Number:
                    if (!TryFormatNumber(element.GetRawText(), out string number, out error))
                    {
                        return false;
                    }
                    text.Append(number);
                    return true;
                case JsonValueKind.True:
                    text.Append("true");
                    error = string.Empty;
                    return true;
                case JsonValueKind.False:
                    text.Append("false");
                    error = string.Empty;
                    return true;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    text.Append("null");
                    error = string.Empty;
                    return true;
            }
        }

        private static bool WriteObject(
            StringBuilder text, JsonElement element, int depth, out string error)
        {
            var members = new List<KeyValuePair<string, JsonElement>>();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                members.Add(
                    new KeyValuePair<string, JsonElement>(property.Name, property.Value));
            }
            members.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            text.Append('{');
            for (int ii = 0; ii < members.Count; ii++)
            {
                if (ii > 0)
                {
                    if (string.Equals(
                        members[ii].Key, members[ii - 1].Key, StringComparison.Ordinal))
                    {
                        error = $"The object repeats the member '{members[ii].Key}'. " +
                            "RFC 8785 canonicalizes a JSON value, and an object that names " +
                            "one member twice is not one value.";
                        return false;
                    }
                    text.Append(',');
                }
                AppendString(text, members[ii].Key);
                text.Append(':');
                if (!Write(text, members[ii].Value, depth + 1, out error))
                {
                    return false;
                }
            }
            text.Append('}');
            error = string.Empty;
            return true;
        }

        private static bool WriteArray(
            StringBuilder text, JsonElement element, int depth, out string error)
        {
            text.Append('[');
            bool first = true;
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!first)
                {
                    text.Append(',');
                }
                first = false;
                if (!Write(text, item, depth + 1, out error))
                {
                    return false;
                }
            }
            text.Append(']');
            error = string.Empty;
            return true;
        }

        private static void AppendCodeUnit(StringBuilder text, char unit)
        {
            text.Append("\\u")
                .Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Formats a double the way ECMAScript <c>Number::toString</c> does,
        /// which is what RFC 8785 Section 3.2.2.3 requires.
        /// </summary>
        /// <remarks>
        /// The shortest round-tripping digit string is found by asking for one
        /// significant digit more until the result parses back to the same
        /// value. That is the definition of the shortest form, and it holds on
        /// the target frameworks whose <c>double</c> formatter is not itself
        /// shortest-round-trip.
        /// </remarks>
        private static string FormatDouble(double value)
        {
            if (value == 0)
            {
                // ECMAScript renders negative zero as "0"; two zeros are one
                // number, and a canonical form that kept the sign would make
                // them two.
                return "0";
            }
            string shortest = value.ToString("G17", CultureInfo.InvariantCulture);
            for (int digits = 1; digits < 17; digits++)
            {
                string candidate = value.ToString(
                    "G" + digits.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture);
                if (double.TryParse(
                        candidate,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double parsed) &&
                    parsed.Equals(value))
                {
                    shortest = candidate;
                    break;
                }
            }
            Decompose(shortest, out bool negative, out string digitsOnly, out int exponent);
            if (digitsOnly.Length == 0)
            {
                return "0";
            }
            var text = new StringBuilder();
            if (negative)
            {
                text.Append('-');
            }
            AppendEcmaScriptDigits(text, digitsOnly, exponent);
            return text.ToString();
        }

        /// <summary>
        /// Writes the digits of a value as ECMAScript states it, where the
        /// value is <c>0.&lt;digits&gt; * 10^exponent</c>.
        /// </summary>
        private static void AppendEcmaScriptDigits(
            StringBuilder text, string digits, int exponent)
        {
            int count = digits.Length;
            if (count <= exponent && exponent <= 21)
            {
                text.Append(digits).Append('0', exponent - count);
                return;
            }
            if (exponent > 0 && exponent <= 21)
            {
                text.Append(digits, 0, exponent)
                    .Append('.')
                    .Append(digits, exponent, count - exponent);
                return;
            }
            if (exponent > -6 && exponent <= 0)
            {
                text.Append("0.").Append('0', -exponent).Append(digits);
                return;
            }
            if (count == 1)
            {
                text.Append(digits);
            }
            else
            {
                text.Append(digits, 0, 1).Append('.').Append(digits, 1, count - 1);
            }
            int power = exponent - 1;
            text.Append('e').Append(power < 0 ? '-' : '+')
                .Append(Math.Abs(power).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Decomposes a decimal literal into its sign, its significant digits
        /// and the exponent that places them, so that the value is
        /// <c>0.&lt;digits&gt; * 10^exponent</c> with no leading or trailing
        /// zero among the digits.
        /// </summary>
        private static void Decompose(
            string literal, out bool negative, out string digits, out int exponent)
        {
            negative = false;
            exponent = 0;
            int index = 0;
            if (index < literal.Length && (literal[index] == '-' || literal[index] == '+'))
            {
                negative = literal[index] == '-';
                index++;
            }
            var mantissa = new StringBuilder();
            int pointPosition = -1;
            int power = 0;
            for (; index < literal.Length; index++)
            {
                char unit = literal[index];
                if (unit == '.')
                {
                    pointPosition = mantissa.Length;
                    continue;
                }
                if (unit is 'e' or 'E')
                {
                    power = ParseExponent(literal, index + 1);
                    break;
                }
                mantissa.Append(unit);
            }
            if (pointPosition < 0)
            {
                pointPosition = mantissa.Length;
            }
            string allDigits = mantissa.ToString();
            int leading = 0;
            while (leading < allDigits.Length && allDigits[leading] == '0')
            {
                leading++;
            }
            int trailing = allDigits.Length;
            while (trailing > leading && allDigits[trailing - 1] == '0')
            {
                trailing--;
            }
            if (leading >= trailing)
            {
                digits = string.Empty;
                exponent = 0;
                negative = false;
                return;
            }
            digits = allDigits.Substring(leading, trailing - leading);
            exponent = pointPosition - leading + power;
        }

        /// <summary>
        /// Reads the exponent of a decimal literal, saturating rather than
        /// overflowing: a literal whose exponent no <c>int</c> holds names a
        /// value no double holds, and the caller rejects it on the comparison
        /// that follows.
        /// </summary>
        private static int ParseExponent(string literal, int index)
        {
            bool negative = false;
            if (index < literal.Length && (literal[index] == '-' || literal[index] == '+'))
            {
                negative = literal[index] == '-';
                index++;
            }
            const int limit = int.MaxValue / 4;
            int power = 0;
            for (; index < literal.Length; index++)
            {
                char unit = literal[index];
                if (unit is < '0' or > '9')
                {
                    break;
                }
                if (power < limit)
                {
                    power = (power * 10) + (unit - '0');
                }
            }
            return negative ? -power : power;
        }

        /// <summary>
        /// Determines whether two decimal literals name the same exact value,
        /// which is how a literal is held to the double it parsed to.
        /// </summary>
        private static bool SameDecimalValue(string left, string right)
        {
            Decompose(left, out bool leftNegative, out string leftDigits, out int leftExponent);
            Decompose(
                right, out bool rightNegative, out string rightDigits, out int rightExponent);
            if (leftDigits.Length == 0 || rightDigits.Length == 0)
            {
                return leftDigits.Length == rightDigits.Length;
            }
            return leftNegative == rightNegative &&
                leftExponent == rightExponent &&
                string.Equals(leftDigits, rightDigits, StringComparison.Ordinal);
        }
    }
}
