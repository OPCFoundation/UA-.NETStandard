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
using System.Numerics;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Converts between the xsd lexical space an AAS carries a value in and
    /// the OPC UA value clause 6.3.1 assigns it, and back into the XSD 1.1
    /// canonical lexical representation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAS carries a value as a string in the lexical space and defines no
    /// equality on it — no normalization, no canonical form, and no
    /// requirement that a lexical form survive a round trip. XML Schema, by
    /// contrast, defines a datatype's value space and lexical space
    /// separately, defines identity on the value space, and designates a
    /// canonical lexical representation for each type.
    /// </para>
    /// <para>
    /// Clause 6.1.2 therefore requires a Server to materialize a value into
    /// the DataType of clause 6.3.1 and to serialize it back as the canonical
    /// lexical representation of that type. A Property authored as
    /// <c>"1.500000"</c> with <c>ValueType</c> <c>xs:decimal</c> serializes as
    /// <c>"1.5"</c>, and one authored <c>"+42"</c> with <c>xs:int</c>
    /// serializes as <c>"42"</c>. The documents are equivalent under clause
    /// 6.4, not identical.
    /// </para>
    /// <para>
    /// The canonical form is the one XSD 1.1 defines. That matters for
    /// <c>xs:decimal</c>, where XSD 1.1's canonical mapping omits the
    /// fractional part of an integral value — <c>"1.0"</c> canonicalizes to
    /// <c>"1"</c>, where XSD 1.0 would have required <c>"1.0"</c>.
    /// </para>
    /// <para>
    /// Two assignments have a range narrower than the xsd type they carry:
    /// <c>Integer</c> and <c>UInteger</c> are the abstract unions of OPC UA's
    /// concrete integer types so their range is that of <c>Int64</c> and
    /// <c>UInt64</c>, whereas <c>xs:integer</c> is unbounded; and
    /// <c>DateTime</c> begins in 1601, whereas <c>xs:dateTime</c> does not. A
    /// value outside the representable range is rejected rather than
    /// truncated, per clause 6.3.3.
    /// </para>
    /// </remarks>
    public static class AasLexicalCanonicalizer
    {
        /// <summary>
        /// Parses a value from its xsd lexical form into the OPC UA value
        /// clause 6.3.1 assigns to the declared type.
        /// </summary>
        /// <param name="lexical">The lexical form the AAS carries.</param>
        /// <param name="valueType">The declared xsd type.</param>
        /// <param name="value">The parsed value when the return value is <c>true</c>.</param>
        /// <param name="error">The reason parsing failed when the return value is <c>false</c>.</param>
        /// <returns><c>true</c> when the lexical form is valid and in range.</returns>
        public static bool TryParse(
            string? lexical,
            AASDataTypeDefXsdDataType valueType,
            out Variant value,
            out string? error)
        {
            value = Variant.Null;
            error = null;

            if (lexical is null)
            {
                error = "The lexical form is null.";
                return false;
            }

            switch (valueType)
            {
                case AASDataTypeDefXsdDataType.Boolean:
                    return TryParseBoolean(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.Byte:
                    return TryParseSigned(lexical, sbyte.MinValue, sbyte.MaxValue, valueType,
                        v => new Variant((sbyte)v), out value, out error);
                case AASDataTypeDefXsdDataType.Short:
                    return TryParseSigned(lexical, short.MinValue, short.MaxValue, valueType,
                        v => new Variant((short)v), out value, out error);
                case AASDataTypeDefXsdDataType.Int:
                    return TryParseSigned(lexical, int.MinValue, int.MaxValue, valueType,
                        v => new Variant((int)v), out value, out error);
                case AASDataTypeDefXsdDataType.Long:
                    return TryParseSigned(lexical, long.MinValue, long.MaxValue, valueType,
                        v => new Variant(v), out value, out error);
                case AASDataTypeDefXsdDataType.UnsignedByte:
                    return TryParseUnsigned(lexical, byte.MaxValue, valueType,
                        v => new Variant((byte)v), out value, out error);
                case AASDataTypeDefXsdDataType.UnsignedShort:
                    return TryParseUnsigned(lexical, ushort.MaxValue, valueType,
                        v => new Variant((ushort)v), out value, out error);
                case AASDataTypeDefXsdDataType.UnsignedInt:
                    return TryParseUnsigned(lexical, uint.MaxValue, valueType,
                        v => new Variant((uint)v), out value, out error);
                case AASDataTypeDefXsdDataType.UnsignedLong:
                    return TryParseUnsigned(lexical, ulong.MaxValue, valueType,
                        v => new Variant(v), out value, out error);
                // xs:integer is unbounded but Integer is the union of the
                // concrete signed types, so Int64 is the representable range.
                case AASDataTypeDefXsdDataType.Integer:
                    return TryParseSigned(lexical, long.MinValue, long.MaxValue, valueType,
                        v => new Variant(v), out value, out error);
                case AASDataTypeDefXsdDataType.NonNegativeInteger:
                    return TryParseUnsigned(lexical, ulong.MaxValue, valueType,
                        v => new Variant(v), out value, out error);
                case AASDataTypeDefXsdDataType.PositiveInteger:
                    return TryParseUnsigned(lexical, ulong.MaxValue, valueType,
                        v => new Variant(v), out value, out error, minimum: BigInteger.One);
                case AASDataTypeDefXsdDataType.NonPositiveInteger:
                    return TryParseSigned(lexical, long.MinValue, 0, valueType,
                        v => new Variant(v), out value, out error);
                case AASDataTypeDefXsdDataType.NegativeInteger:
                    return TryParseSigned(lexical, long.MinValue, -1, valueType,
                        v => new Variant(v), out value, out error);
                case AASDataTypeDefXsdDataType.Float:
                    return TryParseFloat(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.Double:
                    return TryParseDouble(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.Decimal:
                    return TryParseDecimal(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.DateTime:
                    return TryParseDateTime(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.Base64Binary:
                    return TryParseBase64(lexical, out value, out error);
                case AASDataTypeDefXsdDataType.HexBinary:
                    return TryParseHex(lexical, out value, out error);
                // The remaining types are carried as strings: xs:string and
                // xs:anyURI directly, and the date, time, duration and the five
                // Gregorian period types as the String subtypes clause 6.3.1
                // assigns them, whose value space is their lexical space.
                default:
                    return TryParseLexicalString(lexical, valueType, out value, out error);
            }
        }

        /// <summary>
        /// Serializes a value back into the XSD 1.1 canonical lexical
        /// representation of its declared type.
        /// </summary>
        /// <param name="value">The value read from the AddressSpace.</param>
        /// <param name="valueType">The declared xsd type.</param>
        /// <param name="lexical">The canonical lexical form when the return value is <c>true</c>.</param>
        /// <param name="error">The reason serialization failed when the return value is <c>false</c>.</param>
        /// <returns><c>true</c> when the value could be serialized.</returns>
        public static bool TryCanonicalize(
            in Variant value,
            AASDataTypeDefXsdDataType valueType,
            out string? lexical,
            out string? error)
        {
            lexical = null;
            error = null;

            switch (valueType)
            {
                case AASDataTypeDefXsdDataType.Boolean:
                    if (!value.TryGetValue(out bool flag))
                    {
                        error = Mismatch(valueType, "Boolean");
                        return false;
                    }

                    // The canonical forms are "true" and "false"; the lexical
                    // space also admits "1" and "0".
                    lexical = flag ? "true" : "false";
                    return true;
                case AASDataTypeDefXsdDataType.Decimal:
                    // TryGetStructure is annotated MaybeNullWhen(false) and
                    // Decimal is a reference type, so the target is declared
                    // separately and suppressed, the way the rest of the stack
                    // does it.
                    Decimal dec = null!;
                    if (value.TryGetStructure(out dec!))
                    {
                        lexical = dec.ToString("C", CultureInfo.InvariantCulture);
                        return true;
                    }

                    error = Mismatch(valueType, "Decimal");
                    return false;
                case AASDataTypeDefXsdDataType.Float:
                    if (!value.TryGetValue(out float single))
                    {
                        error = Mismatch(valueType, "Float");
                        return false;
                    }

                    lexical = CanonicalizeDouble(single);
                    return true;
                case AASDataTypeDefXsdDataType.Double:
                    if (!value.TryGetValue(out double real))
                    {
                        error = Mismatch(valueType, "Double");
                        return false;
                    }

                    lexical = CanonicalizeDouble(real);
                    return true;
                case AASDataTypeDefXsdDataType.DateTime:
                    if (!value.TryGetValue(out DateTimeUtc instant))
                    {
                        error = Mismatch(valueType, "DateTime");
                        return false;
                    }

                    lexical = instant.ToDateTime()
                        .ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.Base64Binary:
                    if (!value.TryGetValue(out ByteString base64))
                    {
                        error = Mismatch(valueType, "ByteString");
                        return false;
                    }

                    lexical = Convert.ToBase64String(base64.Span.ToArray());
                    return true;
                case AASDataTypeDefXsdDataType.HexBinary:
                    if (!value.TryGetValue(out ByteString hex))
                    {
                        error = Mismatch(valueType, "ByteString");
                        return false;
                    }

                    lexical = ToHexUpper(hex.Span);
                    return true;
                default:
                    return TryCanonicalizeNumericOrString(value, valueType, out lexical, out error);
            }
        }

        /// <summary>
        /// Parses a lexical form and returns it in canonical form in one step.
        /// </summary>
        /// <param name="lexical">The lexical form the AAS carries.</param>
        /// <param name="valueType">The declared xsd type.</param>
        /// <param name="canonical">The canonical lexical form when the return value is <c>true</c>.</param>
        /// <param name="error">The reason the conversion failed when the return value is <c>false</c>.</param>
        /// <returns><c>true</c> when the lexical form is valid and in range.</returns>
        public static bool TryCanonicalizeLexical(
            string? lexical,
            AASDataTypeDefXsdDataType valueType,
            out string? canonical,
            out string? error)
        {
            canonical = null;

            return TryParse(lexical, valueType, out Variant value, out error) &&
                TryCanonicalize(value, valueType, out canonical, out error);
        }

        private static bool TryCanonicalizeNumericOrString(
            in Variant value,
            AASDataTypeDefXsdDataType valueType,
            out string? lexical,
            out string? error)
        {
            lexical = null;
            error = null;

            switch (valueType)
            {
                case AASDataTypeDefXsdDataType.Byte when value.TryGetValue(out sbyte i8):
                    lexical = i8.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.Short when value.TryGetValue(out short i16):
                    lexical = i16.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.Int when value.TryGetValue(out int i32):
                    lexical = i32.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.Long
                    or AASDataTypeDefXsdDataType.Integer
                    or AASDataTypeDefXsdDataType.NonPositiveInteger
                    or AASDataTypeDefXsdDataType.NegativeInteger when value.TryGetValue(out long i64):
                    lexical = i64.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.UnsignedByte when value.TryGetValue(out byte u8):
                    lexical = u8.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.UnsignedShort when value.TryGetValue(out ushort u16):
                    lexical = u16.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.UnsignedInt when value.TryGetValue(out uint u32):
                    lexical = u32.ToString(CultureInfo.InvariantCulture);
                    return true;
                case AASDataTypeDefXsdDataType.UnsignedLong
                    or AASDataTypeDefXsdDataType.NonNegativeInteger
                    or AASDataTypeDefXsdDataType.PositiveInteger when value.TryGetValue(out ulong u64):
                    lexical = u64.ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    if (value.TryGetValue(out string? text) && text is not null)
                    {
                        // The string-carried types are already canonical: their
                        // value space is their lexical space, so there is
                        // nothing to normalize.
                        lexical = text;
                        return true;
                    }

                    error = Mismatch(valueType, "the assigned DataType");
                    return false;
            }
        }

        private static bool TryParseBoolean(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            switch (lexical)
            {
                case "true" or "1":
                    value = new Variant(true);
                    return true;
                case "false" or "0":
                    value = new Variant(false);
                    return true;
                default:
                    error = Invalid(lexical, AASDataTypeDefXsdDataType.Boolean);
                    return false;
            }
        }

        private static bool TryParseSigned(
            string lexical,
            long minimum,
            long maximum,
            AASDataTypeDefXsdDataType valueType,
            Func<long, Variant> wrap,
            out Variant value,
            out string? error)
        {
            value = Variant.Null;

            if (!TryParseIntegerLexical(lexical, valueType, out BigInteger parsed, out error))
            {
                return false;
            }

            if (parsed < minimum || parsed > maximum)
            {
                error = OutOfRange(lexical, valueType);
                return false;
            }

            value = wrap((long)parsed);
            return true;
        }

        private static bool TryParseUnsigned(
            string lexical,
            ulong maximum,
            AASDataTypeDefXsdDataType valueType,
            Func<ulong, Variant> wrap,
            out Variant value,
            out string? error,
            BigInteger? minimum = null)
        {
            value = Variant.Null;

            if (!TryParseIntegerLexical(lexical, valueType, out BigInteger parsed, out error))
            {
                return false;
            }

            if (parsed < (minimum ?? BigInteger.Zero) || parsed > maximum)
            {
                error = OutOfRange(lexical, valueType);
                return false;
            }

            value = wrap((ulong)parsed);
            return true;
        }

        private static bool TryParseIntegerLexical(
            string lexical,
            AASDataTypeDefXsdDataType valueType,
            out BigInteger parsed,
            out string? error)
        {
            parsed = BigInteger.Zero;
            error = null;

            // The lexical space allows a leading sign and leading zeroes, which
            // is why "+42" is a legal xs:int and canonicalizes to "42".
            if (lexical.Length == 0 ||
                !BigInteger.TryParse(
                    lexical,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                error = Invalid(lexical, valueType);
                return false;
            }

            return true;
        }

        private static bool TryParseFloat(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            if (TryParseSpecialDouble(lexical, out double special))
            {
                value = new Variant((float)special);
                return true;
            }

            if (!IsXsdNumericLexical(lexical) ||
                !float.TryParse(
                lexical,
                s_xsdFloatStyles,
                CultureInfo.InvariantCulture,
                out float parsed))
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.Float);
                return false;
            }

            value = new Variant(parsed);
            return true;
        }

        private static bool TryParseDouble(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            if (TryParseSpecialDouble(lexical, out double special))
            {
                value = new Variant(special);
                return true;
            }

            if (!IsXsdNumericLexical(lexical) ||
                !double.TryParse(
                lexical,
                s_xsdFloatStyles,
                CultureInfo.InvariantCulture,
                out double parsed))
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.Double);
                return false;
            }

            value = new Variant(parsed);
            return true;
        }

        private static bool TryParseSpecialDouble(string lexical, out double value)
        {
            // XML Schema spells the special values exactly these ways, and is
            // case sensitive about them: "Infinity" is not a legal xs:double.
            switch (lexical)
            {
                case "INF":
                    value = double.PositiveInfinity;
                    return true;
                case "-INF":
                    value = double.NegativeInfinity;
                    return true;
                case "NaN":
                    value = double.NaN;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool IsXsdNumericLexical(string lexical)
        {
            // The .NET parsers are more permissive than XML Schema: they accept
            // "Infinity", "NaN" in any casing, thousands separators and
            // surrounding whitespace, none of which is a legal xs:double. The
            // three special values are matched exactly, beforehand, so anything
            // reaching here must be a plain numeral.
            if (lexical.Length == 0)
            {
                return false;
            }

            foreach (char c in lexical)
            {
                if (c is (>= '0' and <= '9') or '+' or '-' or '.' or 'e' or 'E')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool TryParseDecimal(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            if (!Decimal.TryParse(lexical, out Decimal? parsed) || parsed is null)
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.Decimal);
                return false;
            }

            value = new Variant(new ExtensionObject(parsed.TypeId, parsed));
            return true;
        }

        private static bool TryParseDateTime(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            if (!DateTime.TryParse(
                lexical,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.DateTime);
                return false;
            }

            // OPC UA DateTime begins in 1601 while xs:dateTime does not, so an
            // earlier instant is rejected rather than clamped (clause 6.3.3).
            if (parsed < s_dateTimeMinimum)
            {
                error = OutOfRange(lexical, AASDataTypeDefXsdDataType.DateTime);
                return false;
            }

            value = new Variant(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
            return true;
        }

        private static bool TryParseBase64(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            try
            {
                value = new Variant(ByteString.From(Convert.FromBase64String(lexical)));
                return true;
            }
            catch (FormatException)
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.Base64Binary);
                return false;
            }
        }

        private static bool TryParseHex(string lexical, out Variant value, out string? error)
        {
            value = Variant.Null;
            error = null;

            if ((lexical.Length & 1) != 0)
            {
                error = Invalid(lexical, AASDataTypeDefXsdDataType.HexBinary);
                return false;
            }

            var octets = new byte[lexical.Length / 2];
            for (int i = 0; i < octets.Length; i++)
            {
                if (!TryParseHexDigit(lexical[i * 2], out int high) ||
                    !TryParseHexDigit(lexical[(i * 2) + 1], out int low))
                {
                    error = Invalid(lexical, AASDataTypeDefXsdDataType.HexBinary);
                    return false;
                }

                octets[i] = (byte)((high << 4) | low);
            }

            value = new Variant(ByteString.From(octets));
            return true;
        }

        private static bool TryParseHexDigit(char c, out int digit)
        {
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
                return true;
            }

            // The lexical space of xs:hexBinary admits both cases; the
            // canonical form is uppercase.
            if (c is >= 'A' and <= 'F')
            {
                digit = (c - 'A') + 10;
                return true;
            }

            if (c is >= 'a' and <= 'f')
            {
                digit = (c - 'a') + 10;
                return true;
            }

            digit = 0;
            return false;
        }

        private static bool TryParseLexicalString(
            string lexical,
            AASDataTypeDefXsdDataType valueType,
            out Variant value,
            out string? error)
        {
            error = null;
            value = new Variant(lexical);
            _ = valueType;
            return true;
        }

        private static string CanonicalizeDouble(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsPositiveInfinity(value))
            {
                return "INF";
            }

            if (double.IsNegativeInfinity(value))
            {
                return "-INF";
            }

            // "R" round-trips exactly, which is what losslessness needs; the
            // XSD canonical mapping's choice of exponent form is cosmetic
            // beside the requirement that the value space element be preserved.
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ToHexUpper(ReadOnlySpan<byte> octets)
        {
            var builder = new System.Text.StringBuilder(octets.Length * 2);
            foreach (byte octet in octets)
            {
                builder.Append(octet.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string Invalid(string lexical, AASDataTypeDefXsdDataType valueType)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' is not a valid lexical representation of {1}.",
                lexical,
                valueType);
        }

        private static string OutOfRange(string lexical, AASDataTypeDefXsdDataType valueType)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' is outside the range the OPC UA DataType assigned to {1} can represent. " +
                "Clause 6.3.3 requires such a value to be rejected rather than truncated.",
                lexical,
                valueType);
        }

        private static string Mismatch(AASDataTypeDefXsdDataType valueType, string expected)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "The value declared {0} is not carried as {1}.",
                valueType,
                expected);
        }

        private static readonly DateTime s_dateTimeMinimum =
            new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private const NumberStyles s_xsdFloatStyles =
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowExponent;
    }
}
