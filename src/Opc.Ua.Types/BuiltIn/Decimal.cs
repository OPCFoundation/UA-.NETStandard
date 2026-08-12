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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The OPC UA <c>Decimal</c> DataType (<c>i=50</c>): a high-precision
    /// signed decimal number, consisting of an arbitrary precision integer
    /// unscaled value and an integer scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 10000-6 clause 5.1.10 defines the scale as the <em>inverse</em>
    /// power of ten applied to the unscaled value, so the number represented is
    /// <c>UnscaledValue × 10^-Scale</c>. A scale of 2 with an unscaled value of
    /// 150 is therefore <c>1.50</c>, and the scale is what preserves the
    /// authored number of decimal places across a round trip.
    /// </para>
    /// <para>
    /// The unscaled value is arbitrary precision, which is the whole point of
    /// the type: neither <see cref="long"/> nor <see cref="decimal"/> can carry
    /// every value an <c>xs:decimal</c> may hold.
    /// </para>
    /// <para>
    /// Part 6 notes that a <c>Decimal</c> "is like a built-in type and a
    /// DevelopmentPlatform has to have hardcoded knowledge of the type", and
    /// that no Structure metadata is published for it. It is therefore written
    /// by hand rather than generated, and it is carried in a Variant as an
    /// ExtensionObject.
    /// </para>
    /// <para>
    /// The type is named for its BrowseName, following the precedent of the
    /// generated <c>Opc.Ua.Range</c>, which likewise shadows a
    /// similarly-named BCL type inside this namespace.
    /// </para>
    /// </remarks>
    public sealed class Decimal : IEncodeable, IEquatable<Decimal>, IFormattable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Decimal"/> class with
        /// the value zero.
        /// </summary>
        /// <remarks>
        /// The parameterless constructor exists because the decoder activates
        /// an instance before calling <see cref="Decode"/> on it.
        /// </remarks>
        public Decimal()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Decimal"/> class.
        /// </summary>
        /// <param name="unscaledValue">The arbitrary precision unscaled value.</param>
        /// <param name="scale">
        /// The inverse power of ten applied to <paramref name="unscaledValue"/>.
        /// </param>
        public Decimal(BigInteger unscaledValue, short scale)
        {
            UnscaledValue = unscaledValue;
            Scale = scale;
        }

        /// <summary>
        /// Gets or sets the arbitrary precision unscaled value.
        /// </summary>
        public BigInteger UnscaledValue { get; set; }

        /// <summary>
        /// Gets or sets the inverse power of ten applied to
        /// <see cref="UnscaledValue"/>, so that the number represented is
        /// <c>UnscaledValue × 10^-Scale</c>.
        /// </summary>
        public short Scale { get; set; }

        /// <summary>
        /// Gets the value zero, with a scale of zero.
        /// </summary>
        public static Decimal Zero => new();

        /// <summary>
        /// Gets a value indicating whether the number represented is zero,
        /// whatever its scale.
        /// </summary>
        public bool IsZero => UnscaledValue.IsZero;

        /// <summary>
        /// Gets the sign of the number represented: -1, 0 or 1.
        /// </summary>
        public int Sign => UnscaledValue.Sign;

        /// <summary>
        /// Creates a value from the two's complement unscaled octets of the
        /// OPC UA binary encoding, which are ordered least significant byte
        /// first.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="unscaledValue">
        /// The two's complement unscaled value, least significant byte first.
        /// An empty span is the value zero.
        /// </param>
        /// <returns>The decimal.</returns>
        public static Decimal FromLittleEndian(short scale, ReadOnlySpan<byte> unscaledValue)
        {
            if (unscaledValue.Length == 0)
            {
                return new Decimal(BigInteger.Zero, scale);
            }

#if NET5_0_OR_GREATER
            var value = new BigInteger(unscaledValue, isUnsigned: false, isBigEndian: false);
#else
            var value = new BigInteger(unscaledValue.ToArray());
#endif
            return new Decimal(value, scale);
        }

        /// <summary>
        /// Returns the two's complement unscaled octets of the OPC UA binary
        /// encoding, ordered least significant byte first.
        /// </summary>
        /// <returns>The unscaled value's octets.</returns>
        public byte[] ToLittleEndian()
        {
#if NET5_0_OR_GREATER
            return UnscaledValue.ToByteArray(isUnsigned: false, isBigEndian: false);
#else
            return UnscaledValue.ToByteArray();
#endif
        }

        /// <summary>
        /// Parses the XSD lexical representation of an <c>xs:decimal</c>,
        /// retaining the authored number of decimal places as the scale.
        /// </summary>
        /// <remarks>
        /// The lexical space permits a leading sign, and digits either side of
        /// an optional period. <c>"1.500"</c> parses to an unscaled value of
        /// 1500 with a scale of 3, so re-formatting it reproduces the authored
        /// precision; canonicalization is a separate step.
        /// </remarks>
        /// <param name="value">The lexical representation.</param>
        /// <returns>The parsed decimal.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not a valid <c>xs:decimal</c>.</exception>
        public static Decimal Parse(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!TryParse(value, out Decimal? result))
            {
                throw new FormatException(
                    $"'{value}' is not a valid lexical representation of xs:decimal.");
            }

            return result;
        }

        /// <summary>
        /// Parses the XSD lexical representation of an <c>xs:decimal</c>
        /// without throwing.
        /// </summary>
        /// <param name="value">The lexical representation.</param>
        /// <param name="result">The parsed decimal when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when <paramref name="value"/> is a valid <c>xs:decimal</c>.</returns>
        public static bool TryParse(string? value, [NotNullWhen(true)] out Decimal? result)
        {
            result = null;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            int index = 0;
            bool negative = false;

            if (value![index] is '+' or '-')
            {
                negative = value[index] == '-';
                index++;
            }

            var digits = new StringBuilder(value.Length);
            int scale = 0;
            bool seenPeriod = false;
            bool seenDigit = false;

            for (; index < value.Length; index++)
            {
                char c = value[index];

                if (c == '.')
                {
                    // The lexical space allows at most one period.
                    if (seenPeriod)
                    {
                        return false;
                    }

                    seenPeriod = true;
                    continue;
                }

                if (c is < '0' or > '9')
                {
                    return false;
                }

                seenDigit = true;
                digits.Append(c);

                if (seenPeriod)
                {
                    scale++;
                }
            }

            if (!seenDigit || scale > short.MaxValue)
            {
                return false;
            }

            if (!BigInteger.TryParse(
                digits.ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out BigInteger unscaled))
            {
                return false;
            }

            result = new Decimal(negative ? -unscaled : unscaled, (short)scale);
            return true;
        }

        /// <summary>
        /// Returns the value with trailing fractional zeroes removed and the
        /// scale reduced accordingly, which is the XSD 1.1 canonical form.
        /// </summary>
        /// <remarks>
        /// XSD 1.1's <c>decimalCanonicalMap</c> emits no fractional part for an
        /// integral value, so <c>1.500</c> canonicalizes to <c>1.5</c> and
        /// <c>1.0</c> canonicalizes to <c>1</c>. A negative scale is raised to
        /// zero, since the lexical space has no exponent.
        /// </remarks>
        /// <returns>The canonical value.</returns>
        public Decimal Canonicalize()
        {
            BigInteger unscaled = UnscaledValue;
            int scale = Scale;

            if (unscaled.IsZero)
            {
                return new Decimal(BigInteger.Zero, 0);
            }

            while (scale > 0)
            {
                BigInteger quotient = BigInteger.DivRem(unscaled, s_ten, out BigInteger remainder);
                if (!remainder.IsZero)
                {
                    break;
                }

                unscaled = quotient;
                scale--;
            }

            // A negative scale means trailing zeroes the lexical space must
            // spell out, because xs:decimal has no exponent notation.
            while (scale < 0)
            {
                unscaled *= s_ten;
                scale++;
            }

            return new Decimal(unscaled, (short)scale);
        }

        /// <summary>
        /// Returns the XSD lexical representation of the value, preserving the
        /// scale as the number of decimal places.
        /// </summary>
        /// <returns>The lexical representation.</returns>
        public override string ToString()
        {
            return ToString(null, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the XSD lexical representation of the value.
        /// </summary>
        /// <param name="format">
        /// <c>C</c> or <c>c</c> for the XSD 1.1 canonical form; <c>null</c> or
        /// <c>G</c> to preserve the scale as authored.
        /// </param>
        /// <param name="formatProvider">Ignored; the lexical space is culture-invariant.</param>
        /// <returns>The lexical representation.</returns>
        /// <exception cref="FormatException"><paramref name="format"/> is not recognized.</exception>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            Decimal value = format switch
            {
                null or "" or "G" or "g" => this,
                "C" or "c" => Canonicalize(),
                _ => throw new FormatException($"The format '{format}' is not supported.")
            };

            return value.Format();
        }

        /// <inheritdoc/>
        public bool Equals(Decimal? other)
        {
            if (other is null)
            {
                return false;
            }

            // Equality is on the number represented, not on the spelling:
            // 1.50 and 1.5 are the same value at different scales.
            Decimal left = Canonicalize();
            Decimal right = other.Canonicalize();
            return left.Scale == right.Scale && left.UnscaledValue == right.UnscaledValue;
        }

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is Decimal other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            Decimal canonical = Canonicalize();
            return HashCode.Combine(canonical.UnscaledValue, canonical.Scale);
        }

        /// <summary>
        /// Compares two decimals for equality of the number represented.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when both represent the same number.</returns>
        public static bool operator ==(Decimal? left, Decimal? right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        /// <summary>
        /// Compares two decimals for inequality of the number represented.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when they represent different numbers.</returns>
        public static bool operator !=(Decimal? left, Decimal? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// OPC 10000-6 5.1.10 Table 3 gives the ExtensionObject's TypeId as
        /// "the identifier for the Decimal DataType" itself. Unlike an ordinary
        /// Structure, a Decimal has no separate binary and XML encoding Objects,
        /// because no Structure metadata is published for it.
        /// </remarks>
        public ExpandedNodeId TypeId => s_typeId;

        /// <inheritdoc/>
        public ExpandedNodeId BinaryEncodingId => s_typeId;

        /// <inheritdoc/>
        public ExpandedNodeId XmlEncodingId => s_typeId;

        /// <inheritdoc/>
        /// <remarks>
        /// The three encodings genuinely differ, which is why this branches
        /// rather than writing two fields. In binary the body is the
        /// <c>Scale</c> followed by the unscaled octets with no length of their
        /// own, because OPC 10000-6 5.1.10 derives their count from the
        /// enclosing ExtensionObject's <c>Length</c>. In JSON, 5.4.3 renders
        /// the value as a base-10 signed integer string rather than as the
        /// octets.
        /// </remarks>
        public void Encode(IEncoder encoder)
        {
            if (encoder is null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }

            encoder.WriteInt16("Scale", Scale);

            if (encoder.EncodingType == EncodingType.Binary && encoder is BinaryEncoder binary)
            {
                byte[] octets = ToLittleEndian();
                binary.WriteRawBytes(octets, 0, octets.Length);
                return;
            }

            encoder.WriteString(
                "Value",
                UnscaledValue.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void Decode(IDecoder decoder)
        {
            if (decoder is null)
            {
                throw new ArgumentNullException(nameof(decoder));
            }

            Scale = decoder.ReadInt16("Scale");

            if (decoder.EncodingType == EncodingType.Binary && decoder is BinaryDecoder binary)
            {
                if (!binary.TryReadRemainingBodyBytes(out byte[] unscaled))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "A Decimal cannot be decoded from an ExtensionObject whose body length " +
                        "is unknown, because the unscaled value has no length of its own.");
                }

                UnscaledValue = FromLittleEndian(Scale, unscaled).UnscaledValue;
                return;
            }

            string? text = decoder.ReadString("Value");
            UnscaledValue = string.IsNullOrEmpty(text)
                ? BigInteger.Zero
                : BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public bool IsEqual(IEncodeable? encodeable)
        {
            return ReferenceEquals(this, encodeable) ||
                (encodeable is Decimal other && Equals(other));
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new Decimal(UnscaledValue, Scale);
        }

        private string Format()
        {
            string digits = BigInteger.Abs(UnscaledValue)
                .ToString(CultureInfo.InvariantCulture);
            string sign = UnscaledValue.Sign < 0 ? "-" : string.Empty;

            if (Scale <= 0)
            {
                // A negative scale is trailing zeroes; the lexical space has no
                // exponent notation to express them with.
                return string.Concat(sign, digits, new string('0', -Scale));
            }

            if (digits.Length <= Scale)
            {
                return string.Concat(
                    sign,
                    "0.",
                    new string('0', Scale - digits.Length),
                    digits);
            }

            return string.Concat(
                sign,
                digits[..(digits.Length - Scale)],
                ".",
                digits[(digits.Length - Scale)..]);
        }

        private static readonly BigInteger s_ten = new(10);

        private static readonly ExpandedNodeId s_typeId = new(DataTypes.Decimal);
    }
}
