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
using System.Text;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// The deterministic String NodeId encoding of clause 6.1.3: two
    /// implementations materializing the same AAS produce the same NodeIds,
    /// which is what lets a source generator compile an AAS into a Server and
    /// what lets two generations of one document contain the same nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An identifier is a node-kind discriminator followed by decimal length
    /// prefixes and reversibly escaped source components:
    /// </para>
    /// <code>
    /// i4aas3:A:&lt;n&gt;:E(&lt;id&gt;)                        Asset Administration Shell
    /// i4aas3:S:&lt;n&gt;:E(&lt;id&gt;)                        Submodel
    /// i4aas3:C:&lt;n&gt;:E(&lt;id&gt;)                        Concept Description
    /// i4aas3:E:&lt;n&gt;:&lt;m&gt;:E(&lt;owner&gt;)E(&lt;idShortPath&gt;) Submodel Element
    /// </code>
    /// <para>
    /// The lengths are counted in Unicode code points of the <em>encoded</em>
    /// components, so they split the concatenated payload before either
    /// component is decoded and never rely on a delimiter a source string may
    /// itself contain.
    /// </para>
    /// </remarks>
    public static class AasNodeIdEncoding
    {
        /// <summary>
        /// The prefix every AAS String NodeId identifier carries.
        /// </summary>
        public const string Prefix = "i4aas3:";

        /// <summary>
        /// The maximum length of an OPC UA String NodeId identifier.
        /// </summary>
        /// <remarks>
        /// Clause 6.1.3 requires a materializer to derive every identifier in
        /// an identifiable's subtree before creating any node, and to reject
        /// the identifiable whole where one exceeds this limit. Truncating,
        /// replacing or hashing the source strings would not implement the
        /// reversible encoding, so it is not an option.
        /// </remarks>
        public const int MaxIdentifierLength = 4096;

        /// <summary>
        /// Applies the reversible escape <c>E</c> of clause 6.1.3 to one raw
        /// component.
        /// </summary>
        /// <remarks>
        /// The scan is over Unicode scalar values and performs no
        /// normalization. A literal <c>%</c>, every C0 control from U+0000
        /// through U+001F and every C1 control from U+007F through U+009F is
        /// encoded as its UTF-8 bytes, each byte written as <c>%HH</c> with
        /// uppercase hexadecimal digits. Every other scalar value is copied
        /// unchanged.
        /// </remarks>
        /// <param name="value">The raw component.</param>
        /// <returns>The escaped component.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        public static string Escape(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }


            if (!NeedsEscaping(value))
            {
                return value;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!MustEscape(c))
                {
                    builder.Append(c);
                    continue;
                }

                AppendUtf8Escaped(builder, c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reverses <see cref="Escape(string)"/>, accepting only the canonical
        /// form the escape produces.
        /// </summary>
        /// <remarks>
        /// A raw C0 or C1 control, a malformed escape, or an escape whose
        /// decoded value would not itself have been escaped is invalid. That
        /// last rule is what makes the encoding a bijection rather than merely
        /// reversible: <c>%41</c> is rejected because <c>A</c> is copied
        /// unchanged, so only one spelling of any component exists.
        /// </remarks>
        /// <param name="value">The escaped component.</param>
        /// <returns>The raw component.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not in canonical escaped form.</exception>
        public static string Unescape(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }


            if (!TryUnescape(value, out string? decoded, out string? error))
            {
                throw new FormatException(error);
            }

            return decoded;
        }

        /// <summary>
        /// Reverses <see cref="Escape(string)"/> without throwing.
        /// </summary>
        /// <param name="value">The escaped component.</param>
        /// <param name="decoded">The raw component when the return value is <c>true</c>.</param>
        /// <param name="error">The reason the component is not canonical when the return value is <c>false</c>.</param>
        /// <returns><c>true</c> when <paramref name="value"/> is in canonical escaped form.</returns>
        public static bool TryUnescape(
            string? value,
            [NotNullWhen(true)] out string? decoded,
            [NotNullWhen(false)] out string? error)
        {
            decoded = null;
            error = null;

            if (value is null)
            {
                error = "The escaped component is null.";
                return false;
            }

            var builder = new StringBuilder(value.Length);
            List<byte>? pending = null;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (c != '%')
                {
                    if (IsControl(c))
                    {
                        error = string.Format(
                            CultureInfo.InvariantCulture,
                            "A raw control character U+{0:X4} at index {1} is not canonical; it is escaped as %HH.",
                            (int)c,
                            i);
                        return false;
                    }

                    if (pending is { Count: > 0 } &&
                        !FlushPendingBytes(builder, pending, out error))
                    {
                        return false;
                    }

                    builder.Append(c);
                    continue;
                }

                if (i + 2 >= value.Length)
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "The escape at index {0} is truncated; two hexadecimal digits are required.",
                        i);
                    return false;
                }

                if (!TryParseUpperHexByte(value[i + 1], value[i + 2], out byte octet))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "The escape '%{0}{1}' at index {2} is not two uppercase hexadecimal digits.",
                        value[i + 1],
                        value[i + 2],
                        i);
                    return false;
                }

                // A run of escapes is decoded as one UTF-8 sequence rather than
                // byte by byte, because one escaped scalar value spans up to
                // four octets and consecutive escaped scalar values are not
                // separated by anything.
                pending ??= [];
                pending.Add(octet);
                i += 2;
            }

            if (pending is { Count: > 0 } &&
                !FlushPendingBytes(builder, pending, out error))
            {
                return false;
            }

            decoded = builder.ToString();
            return true;
        }

        /// <summary>
        /// Builds the String NodeId identifier of one of the three
        /// Identifiables — a shell, a submodel or a concept description.
        /// </summary>
        /// <param name="kind">The node kind; must not be <see cref="AasNodeKind.SubmodelElement"/>.</param>
        /// <param name="id">The authored AAS identifier, verbatim.</param>
        /// <returns>The String NodeId identifier.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is <see cref="AasNodeKind.SubmodelElement"/>.</exception>
        public static string CreateIdentifiableId(AasNodeKind kind, string id)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }


            if (kind == AasNodeKind.SubmodelElement)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    "A submodel element identifier is built by CreateElementId.");
            }

            string encoded = Escape(id);
            return string.Concat(
                Prefix,
                DiscriminatorOf(kind),
                ":",
                CodePointCount(encoded).ToString(CultureInfo.InvariantCulture),
                ":",
                encoded);
        }

        /// <summary>
        /// Builds the String NodeId identifier of one submodel element.
        /// </summary>
        /// <param name="ownerId">The authored identifier of the owning Identifiable, verbatim.</param>
        /// <param name="idShortPath">The metamodel <c>idShortPath</c> of the element within its owner.</param>
        /// <returns>The String NodeId identifier.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ownerId"/> or <paramref name="idShortPath"/> is <c>null</c>.</exception>
        public static string CreateElementId(string ownerId, string idShortPath)
        {
            if (ownerId is null)
            {
                throw new ArgumentNullException(nameof(ownerId));
            }

            if (idShortPath is null)
            {
                throw new ArgumentNullException(nameof(idShortPath));
            }


            string encodedOwner = Escape(ownerId);
            string encodedPath = Escape(idShortPath);

            return string.Concat(
                Prefix,
                "E:",
                CodePointCount(encodedOwner).ToString(CultureInfo.InvariantCulture),
                ":",
                CodePointCount(encodedPath).ToString(CultureInfo.InvariantCulture),
                ":",
                encodedOwner,
                encodedPath);
        }

        /// <summary>
        /// Parses a String NodeId identifier produced by this class.
        /// </summary>
        /// <param name="identifier">The String NodeId identifier.</param>
        /// <param name="parsed">The parsed identifier when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when <paramref name="identifier"/> is a canonical AAS identifier.</returns>
        public static bool TryParse(string? identifier, out AasParsedNodeId parsed)
        {
            parsed = default;

            if (identifier is null || !identifier.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            int cursor = Prefix.Length;
            if (cursor >= identifier.Length ||
                !TryParseDiscriminator(identifier[cursor], out AasNodeKind kind) ||
                cursor + 1 >= identifier.Length ||
                identifier[cursor + 1] != ':')
            {
                return false;
            }

            cursor += 2;

            if (!TryReadLength(identifier, ref cursor, out int first))
            {
                return false;
            }

            if (kind != AasNodeKind.SubmodelElement)
            {
                string payload = identifier[cursor..];
                if (CodePointCount(payload) != first ||
                    !TryUnescape(payload, out string? id, out _))
                {
                    return false;
                }

                parsed = new AasParsedNodeId(kind, id, null);
                return true;
            }

            if (!TryReadLength(identifier, ref cursor, out int second))
            {
                return false;
            }

            string combined = identifier[cursor..];
            if (!TrySplitByCodePoints(combined, first, out string? ownerPart, out string? pathPart) ||
                CodePointCount(pathPart) != second ||
                !TryUnescape(ownerPart, out string? owner, out _) ||
                !TryUnescape(pathPart, out string? path, out _))
            {
                return false;
            }

            parsed = new AasParsedNodeId(kind, owner, path);
            return true;
        }

        /// <summary>
        /// Reports whether an identifier fits within the OPC UA String NodeId
        /// limit of <see cref="MaxIdentifierLength"/> characters.
        /// </summary>
        /// <param name="identifier">The String NodeId identifier.</param>
        /// <returns><c>true</c> when the identifier is within the limit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <c>null</c>.</exception>
        public static bool IsWithinLengthLimit(string identifier)
        {
            if (identifier is null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            return identifier.Length <= MaxIdentifierLength;
        }

        /// <summary>
        /// Returns the clause 6.1.3 discriminator character of a node kind.
        /// </summary>
        /// <param name="kind">The node kind.</param>
        /// <returns>One of <c>A</c>, <c>S</c>, <c>C</c> or <c>E</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
        public static char DiscriminatorOf(AasNodeKind kind)
        {
            return kind switch
            {
                AasNodeKind.Shell => 'A',
                AasNodeKind.Submodel => 'S',
                AasNodeKind.ConceptDescription => 'C',
                AasNodeKind.SubmodelElement => 'E',
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private static bool TryParseDiscriminator(char c, out AasNodeKind kind)
        {
            switch (c)
            {
                case 'A':
                    kind = AasNodeKind.Shell;
                    return true;
                case 'S':
                    kind = AasNodeKind.Submodel;
                    return true;
                case 'C':
                    kind = AasNodeKind.ConceptDescription;
                    return true;
                case 'E':
                    kind = AasNodeKind.SubmodelElement;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static bool TryReadLength(string identifier, ref int cursor, out int length)
        {
            length = 0;
            int start = cursor;

            while (cursor < identifier.Length && identifier[cursor] is >= '0' and <= '9')
            {
                cursor++;
            }

            if (cursor == start || cursor >= identifier.Length || identifier[cursor] != ':')
            {
                return false;
            }

            ReadOnlySpan<char> digits = identifier.AsSpan(start, cursor - start);

            // Leading zeroes are not canonical: clause 6.1.3 writes the length
            // with ASCII decimal digits and without them, so accepting "05"
            // would give one node two spellings.
            if (digits.Length > 1 && digits[0] == '0')
            {
                return false;
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out length))
#else
            if (!int.TryParse(
                identifier.Substring(start, cursor - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out length))
#endif
            {
                return false;
            }

            cursor++;
            return true;
        }

        private static bool TrySplitByCodePoints(
            string value,
            int codePoints,
            out string head,
            out string tail)
        {
            head = string.Empty;
            tail = string.Empty;

            int index = 0;
            int seen = 0;

            while (seen < codePoints)
            {
                if (index >= value.Length)
                {
                    return false;
                }

                index += char.IsHighSurrogate(value[index]) &&
                    index + 1 < value.Length &&
                    char.IsLowSurrogate(value[index + 1])
                    ? 2
                    : 1;
                seen++;
            }

            head = value[..index];
            tail = value[index..];
            return true;
        }

        private static bool FlushPendingBytes(
            StringBuilder builder,
            List<byte> pending,
            [NotNullWhen(false)] out string? error)
        {
            error = null;

            string decoded;
            try
            {
                decoded = s_strictUtf8.GetString(pending.ToArray());
            }
            catch (DecoderFallbackException)
            {
                error = "An escape sequence is not a valid UTF-8 encoding.";
                return false;
            }

            foreach (char c in decoded)
            {
                if (!MustEscape(c))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "The escape decoding to U+{0:X4} is not canonical; that scalar value is copied unchanged.",
                        (int)c);
                    return false;
                }
            }

            builder.Append(decoded);
            pending.Clear();
            return true;
        }

        private static void AppendUtf8Escaped(StringBuilder builder, char c)
        {
            // A lone surrogate cannot be encoded; the caller only reaches this
            // path for '%' and the C0/C1 controls, none of which is one.
            byte[] octets = Encoding.UTF8.GetBytes(c.ToString());
            foreach (byte octet in octets)
            {
                builder.Append('%');
                builder.Append(s_upperHex[octet >> 4]);
                builder.Append(s_upperHex[octet & 0x0F]);
            }
        }

        private static bool TryParseUpperHexByte(char high, char low, out byte octet)
        {
            octet = 0;

            if (!TryParseUpperHexDigit(high, out int hi) ||
                !TryParseUpperHexDigit(low, out int lo))
            {
                return false;
            }

            octet = (byte)((hi << 4) | lo);
            return true;
        }

        private static bool TryParseUpperHexDigit(char c, out int value)
        {
            if (c is >= '0' and <= '9')
            {
                value = c - '0';
                return true;
            }

            // Lowercase is rejected on purpose: clause 6.1.3 writes each byte
            // "using uppercase hexadecimal digits" and decoding "accepts only
            // this canonical form".
            if (c is >= 'A' and <= 'F')
            {
                value = (c - 'A') + 10;
                return true;
            }

            value = 0;
            return false;
        }

        private static bool NeedsEscaping(string value)
        {
            foreach (char c in value)
            {
                if (MustEscape(c))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MustEscape(char c)
        {
            return c == '%' || IsControl(c);
        }

        private static bool IsControl(char c)
        {
            return c <= '\u001F' || (c >= '\u007F' && c <= '\u009F');
        }

        private static int CodePointCount(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) &&
                    i + 1 < value.Length &&
                    char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }

        private static readonly char[] s_upperHex = "0123456789ABCDEF".ToCharArray();

        private static readonly UTF8Encoding s_strictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
    }
}
