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

            if (scale > 0)
            {
                unscaled = StripTrailingZeros(unscaled, scale, out int stripped);
                scale -= stripped;
            }

            // A negative scale means trailing zeroes the lexical space must
            // spell out, because xs:decimal has no exponent notation. One
            // multiplication instead of one per digit.
            if (scale < 0)
            {
                unscaled *= BigInteger.Pow(s_ten, -scale);
                scale = 0;
            }

            return new Decimal(unscaled, (short)scale);
        }

        /// <summary>
        /// Removes up to <paramref name="maxZeros"/> trailing decimal zeroes
        /// from <paramref name="value"/> with a logarithmic number of
        /// divisions (by 10, 10^2, 10^4, ...) rather than one division per
        /// digit, so a wire supplied scale cannot force tens of thousands of
        /// full size divisions.
        /// </summary>
        private static BigInteger StripTrailingZeros(
            BigInteger value,
            int maxZeros,
            out int stripped)
        {
            stripped = 0;
            if (value.IsZero || maxZeros <= 0)
            {
                return value;
            }

            // Upper bound for the number of decimal digits of the value. A
            // divisor with more digits than that cannot divide it.
            double digits = Math.Floor(BigInteger.Log10(BigInteger.Abs(value))) + 1;
            var powers = new List<(int Step, BigInteger Power)>();
            int step = 1;
            BigInteger power = s_ten;
            while (step <= maxZeros - stripped && step <= digits)
            {
                BigInteger quotient = BigInteger.DivRem(value, power, out BigInteger remainder);
                if (!remainder.IsZero)
                {
                    break;
                }
                value = quotient;
                stripped += step;
                powers.Add((step, power));
                step *= 2;
                if (step > maxZeros - stripped || step > digits)
                {
                    break;
                }
                power *= power;
            }

            // The remaining trailing zeroes are fewer than the last step, so
            // each smaller power is needed at most once.
            for (int ii = powers.Count - 1; ii >= 0; ii--)
            {
                (int smallerStep, BigInteger smallerPower) = powers[ii];
                if (smallerStep > maxZeros - stripped)
                {
                    continue;
                }
                BigInteger quotient = BigInteger.DivRem(value, smallerPower, out BigInteger remainder);
                if (remainder.IsZero)
                {
                    value = quotient;
                    stripped += smallerStep;
                }
            }

            return value;
        }

        /// <summary>
        /// Returns the number represented modulo the prime 2^31-1. Equal
        /// numbers have the same residue whatever their scale, because 10 is
        /// invertible modulo the prime, and it costs one linear pass over the
        /// unscaled value instead of a canonicalization.
        /// </summary>
        private long GetResidue()
        {
            long unscaled = (long)(UnscaledValue % kResiduePrime);
            if (unscaled < 0)
            {
                unscaled += kResiduePrime;
            }
            // value = unscaled * 10^-scale = unscaled * (10^-1)^scale.
            BigInteger factor = Scale >= 0
                ? BigInteger.ModPow(s_tenInverse, Scale, kResiduePrime)
                : BigInteger.ModPow(s_ten, -Scale, kResiduePrime);
            return (long)(unscaled * factor % kResiduePrime);
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
            // 1.50 and 1.5 are the same value at different scales. The scales
            // are wire controlled, so no canonicalization (one division per
            // scale step) is done; instead both are brought to the same scale
            // with a single multiplication after the cheap checks.
            if (UnscaledValue.Sign != other.UnscaledValue.Sign)
            {
                return false;
            }
            if (UnscaledValue.IsZero || Scale == other.Scale)
            {
                return UnscaledValue == other.UnscaledValue;
            }
            if (GetResidue() != other.GetResidue())
            {
                return false;
            }
            Decimal coarse = Scale < other.Scale ? this : other;
            Decimal fine = Scale < other.Scale ? other : this;
            int difference = fine.Scale - coarse.Scale;
            // The finer value has at least as many digits as the scale
            // difference adds; a large gap in magnitude is not equal.
            double coarseDigits = BigInteger.Log10(BigInteger.Abs(coarse.UnscaledValue));
            double fineDigits = BigInteger.Log10(BigInteger.Abs(fine.UnscaledValue));
            if (Math.Abs(coarseDigits + difference - fineDigits) > 1)
            {
                return false;
            }
            return coarse.UnscaledValue * BigInteger.Pow(s_ten, difference) == fine.UnscaledValue;
        }

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is Decimal other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // The residue is scale independent, so equal numbers at different
            // scales hash alike without canonicalizing.
            return GetResidue().GetHashCode();
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
                // The octets are a ByteString in all but name, so the
                // MaxByteStringLength limit applies; the fixed cap keeps every
                // later rendering as decimal digits (which is quadratic in the
                // number of digits) cheap. Both are checked before reading.
                int maxOctets = decoder.Context.MaxByteStringLength > 0
                    ? Math.Min(decoder.Context.MaxByteStringLength, MaxUnscaledOctets)
                    : MaxUnscaledOctets;
                if (!binary.TryReadRemainingBodyBytes(maxOctets, out byte[] unscaled))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "A Decimal cannot be decoded from an ExtensionObject whose body length " +
                        "is unknown, because the unscaled value has no length of its own.");
                }

                // OPC 10000-6 5.1.10 Table 3: a Length of two or less leaves no
                // octets for the unscaled value, and the clause calls that an
                // invalid value that cannot be used. Reading it as zero would
                // accept a Decimal the specification says does not exist.
                if (unscaled.Length == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "A Decimal whose ExtensionObject body is no longer than its Scale carries " +
                        "no unscaled value and is invalid.");
                }

                UnscaledValue = FromLittleEndian(Scale, unscaled).UnscaledValue;
                return;
            }

            string? text = decoder.ReadString("Value");
            if (string.IsNullOrEmpty(text))
            {
                UnscaledValue = BigInteger.Zero;
                return;
            }

            // Parsing decimal digits is quadratic on some runtimes; bound the
            // digits like the binary octets before parsing them.
            int digits = text!.Length - (text[0] is '-' or '+' ? 1 : 0);
            if (digits > MaxUnscaledDigits)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The Decimal value has {0} digits, more than the limit of {1}.",
                    digits,
                    MaxUnscaledDigits);
            }

            // OPC 10000-6 5.4.3 renders the unscaled value as a base-10 signed
            // integer string; anything else is a malformed value.
            if (!BigInteger.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out BigInteger unscaledValue))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The Decimal value '{0}' is not a base-10 integer.",
                    text.Length > 32 ? text[..32] + "..." : text);
            }
            UnscaledValue = unscaledValue;
        }

        /// <summary>
        /// The largest number of octets of the unscaled value that is decoded.
        /// </summary>
        /// <remarks>
        /// OPC 10000-6 5.1.10 does not bound the unscaled value. Rendering a
        /// value as decimal digits (<see cref="ToString()"/>, the JSON and XML
        /// encodings) costs time quadratic in its length, so a decoded value is
        /// held to 2048 octets, a 16384-bit integer of up to 4933 decimal
        /// digits, far beyond any practical precision.
        /// </remarks>
        public const int MaxUnscaledOctets = 2048;

        /// <summary>
        /// The largest number of decimal digits of the unscaled value that is
        /// decoded from the JSON and XML encodings: the most digits
        /// <see cref="MaxUnscaledOctets"/> octets can carry.
        /// </summary>
        public const int MaxUnscaledDigits = 4933;

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
        private const long kResiduePrime = int.MaxValue;
        private static readonly BigInteger s_tenInverse =
            BigInteger.ModPow(s_ten, kResiduePrime - 2, kResiduePrime);

        private static readonly ExpandedNodeId s_typeId = new(DataTypes.Decimal);
    }
}
