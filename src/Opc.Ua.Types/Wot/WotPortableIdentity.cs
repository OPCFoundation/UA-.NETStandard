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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// One element of an absolute browse path: a name, and the namespace it is
    /// qualified by.
    /// </summary>
    /// <param name="NamespaceUri">
    /// The element's NamespaceUri, or <c>null</c> for the base OPC UA
    /// namespace, which Annex G.1 writes bare.
    /// </param>
    /// <param name="Name">The element's unqualified name.</param>
    public readonly record struct WotBrowsePathElement(string? NamespaceUri, string Name);

    /// <summary>
    /// The portable-identity and deterministic-encoding algorithms the WoT
    /// Binding states as formulas: the generated NodeId of Annex G.1, the
    /// injective sequence encoding of Annex G.3, the portable ExpandedNodeId
    /// and QualifiedName forms of Sections 5.1.1 and 5.1.3, the anchored
    /// browse paths of Section 5.1.4 and the <c>ViewVersion</c> of Section
    /// 12.6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of these is a function the specification publishes vectors for, and
    /// each was previously buried inside the code path that happened to need
    /// it - which made a vector unrunnable and let two call sites drift. They
    /// live here so a vector can call exactly what a conversion calls.
    /// </para>
    /// <para>
    /// The class performs no I/O and holds no state, so every result depends
    /// only on its arguments.
    /// </para>
    /// </remarks>
    public static class WotPortableIdentity
    {
        /// <summary>
        /// Builds the generated NodeId of Annex G.1:
        /// <c>"nsu=" + U + ";s=" + P</c>, where <c>P</c> is the Node's absolute
        /// browse path in OPC 10000-4 Annex A.2 relative-path syntax.
        /// </summary>
        /// <remarks>
        /// Each element is preceded by <c>/</c>. An element of the base OPC UA
        /// namespace is written bare; any other is written
        /// <c>nsu=&lt;escaped NamespaceUri&gt;;&lt;name&gt;</c>. A reserved
        /// character inside a name is escaped with <c>&amp;</c>, so a name
        /// containing <c>/</c> cannot imitate a path separator.
        /// </remarks>
        /// <param name="namespaceUri">
        /// The NamespaceUri the synthesized Node is created in.
        /// </param>
        /// <param name="path">The Node's absolute browse path.</param>
        /// <returns>The generated portable NodeId.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUri"/> is <c>null</c>.
        /// </exception>
        public static string GenerateNodeId(
            string namespaceUri,
            ArrayOf<WotBrowsePathElement> path)
        {
            if (namespaceUri is null)
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }
            return new StringBuilder("nsu=")
                .Append(CoreUtils.EscapeUri(namespaceUri))
                .Append(";s=")
                .Append(GenerateBrowsePath(path))
                .ToString();
        }

        /// <summary>
        /// Builds the <c>P</c> of Annex G.1: a Node's absolute browse path in
        /// OPC 10000-4 Annex A.2 relative-path syntax.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each element is preceded by <c>/</c>. An element of the base OPC UA
        /// namespace is written bare; any other is written
        /// <c>nsu=&lt;escaped NamespaceUri&gt;;&lt;name&gt;</c>. A reserved
        /// character inside a name is escaped with <c>&amp;</c>.
        /// </para>
        /// <para>
        /// The escaping and the leading separator are what make the encoding
        /// injective. Without them a member named <c>A/B</c> of <c>Root</c> and
        /// a member named <c>B</c> of <c>Root/A</c> produce the same string,
        /// and two different Nodes then answer to one identifier.
        /// </para>
        /// </remarks>
        /// <param name="path">The Node's absolute browse path.</param>
        /// <returns>The relative-path encoding of the browse path.</returns>
        public static string GenerateBrowsePath(ArrayOf<WotBrowsePathElement> path)
        {
            var builder = new StringBuilder();
            foreach (WotBrowsePathElement element in path)
            {
                builder.Append('/');
                if (!string.IsNullOrEmpty(element.NamespaceUri) &&
                    !string.Equals(
                        element.NamespaceUri,
                        WotVocabulary.OpcUaNamespace,
                        StringComparison.Ordinal))
                {
                    builder
                        .Append("nsu=")
                        .Append(EscapeNamespaceUri(element.NamespaceUri!))
                        .Append(';');
                }
                builder.Append(EscapeName(element.Name ?? string.Empty));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Gets whether a NodeId string uses the session-local
        /// <c>ns=&lt;index&gt;</c> form, which is never persisted
        /// (Section 5.1.1).
        /// </summary>
        /// <param name="value">The authored value.</param>
        /// <returns><c>true</c> when the value names a namespace index.</returns>
        public static bool IsSessionLocalNodeId(string? value)
        {
            const string prefix = "ns=";
            if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            int index = prefix.Length;
            int start = index;
            while (index < value.Length && value[index] is >= '0' and <= '9')
            {
                index++;
            }
            return index > start && index < value.Length && value[index] == ';';
        }

        /// <summary>
        /// Gets whether a NodeId-valued member is portable: an OPC 10000-6
        /// ExpandedNodeId with no session-local namespace index and no
        /// ServerIndex prefix (Section 5.1.1).
        /// </summary>
        /// <param name="value">The authored value.</param>
        /// <returns><c>true</c> when the value may be persisted.</returns>
        public static bool IsPortableNodeId(string? value)
        {
            if (string.IsNullOrEmpty(value) ||
                value!.StartsWith("svr=", StringComparison.Ordinal) ||
                value.StartsWith("ns=", StringComparison.Ordinal))
            {
                return false;
            }
            string identifier = value;
            if (value.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = value.IndexOf(';', 4);
                if (delimiter < 0 || delimiter == 4 || delimiter + 1 >= value.Length)
                {
                    return false;
                }
                identifier = value[(delimiter + 1)..];
            }
            return HasIdentifierType(identifier) &&
                NodeId.TryParse(identifier, out NodeId parsed) &&
                parsed.NamespaceIndex == 0;
        }

        /// <summary>
        /// Gets whether a persisted QualifiedName is portable: a compact
        /// prefixed name, a bare namespace-0 name, or the OPC 10000-6
        /// <c>nsu=</c> form. A numeric NamespaceIndex prefix is never persisted
        /// (Section 5.1.3).
        /// </summary>
        /// <param name="value">The authored value.</param>
        /// <returns><c>true</c> when the value may be persisted.</returns>
        public static bool IsPortableQualifiedName(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            if (value!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = value.IndexOf(';', 4);
                return delimiter > 4 && delimiter + 1 < value.Length;
            }
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                return true;
            }
            if (colon == 0 || colon + 1 >= value.Length)
            {
                return false;
            }
            for (int ii = 0; ii < colon; ii++)
            {
                if (value[ii] is < '0' or > '9')
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool TryResolveQualifiedName(
            string? value,
            WotDocument document,
            JsonElement carryingNode,
            out WotBrowsePathElement qualifiedName)
        {
            qualifiedName = default;
            if (!IsPortableQualifiedName(value))
            {
                return false;
            }
            if (value!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = value.IndexOf(';', 4);
                qualifiedName = new WotBrowsePathElement(
                    CoreUtils.UnescapeUri(value.AsSpan(4, delimiter - 4)),
                    value[(delimiter + 1)..]);
                return true;
            }
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                qualifiedName = new WotBrowsePathElement(WotVocabulary.OpcUaNamespace, value);
                return true;
            }
            if (!document.TryGetContextPrefix(
                value[..colon], out string namespaceUri, carryingNode))
            {
                return false;
            }
            qualifiedName = new WotBrowsePathElement(namespaceUri, value[(colon + 1)..]);
            return true;
        }

        /// <summary>
        /// Gets whether a browse path has a starting Node (Section 5.1.4).
        /// </summary>
        /// <remarks>
        /// An absolute path starts at a well-known Node and always resolves. A
        /// relative path resolves only where a <c>uav:browsePathAnchor</c> says
        /// what it is relative to; without one it names a sequence of steps from
        /// nowhere. Either kind is unresolvable when an element uses a numeric
        /// NamespaceIndex, which is never persisted.
        /// This is an annotation portability check: Root and a final folder
        /// separator remain valid here. A native operation additionally needs
        /// the complete target validated by <see cref="WotBrowsePathTarget"/>.
        /// </remarks>
        /// <param name="path">The authored browse path.</param>
        /// <param name="anchored">
        /// Whether the carrying node states a <c>uav:browsePathAnchor</c>.
        /// </param>
        /// <returns><c>true</c> when the path can be resolved.</returns>
        public static bool IsResolvableBrowsePath(string? path, bool anchored)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            bool absolute = path![0] == '/';
            if (!absolute && !anchored)
            {
                return false;
            }
            try
            {
                var parsed = RelativePathFormatter.ParsePortable(
                    path, new NamespaceTable(), static _ => "urn:wot:lexical-prefix", 1024);
                if (parsed.Elements.Count == 0)
                {
                    return false;
                }
                for (int index = 0; index < parsed.Elements.Count; index++)
                {
                    RelativePathFormatter.Element element = parsed.Elements[index];
                    if ((element.TargetName.IsNull || string.IsNullOrEmpty(element.TargetName.Name)) &&
                        (index != parsed.Elements.Count - 1 ||
                            element.ElementType != RelativePathFormatter.ElementType.AnyHierarchical))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        /// <summary>
        /// Encodes a sequence of strings with the injective encoding of
        /// Annex G.3: for each string, its length in UTF-8 octets as decimal
        /// digits, U+003A COLON, the string, and U+000A LINE FEED.
        /// </summary>
        /// <remarks>
        /// The length prefix is what makes the encoding injective. A string may
        /// itself contain U+000A, so joining on the separator alone would let
        /// one item embedding a newline serialize as the two items it imitates,
        /// and two different sequences would then hash identically.
        /// </remarks>
        /// <param name="items">The strings, in the order the caller states.</param>
        /// <returns>The UTF-8 encoding of the sequence.</returns>
        public static ByteString EncodeSequence(ArrayOf<string> items)
        {
            var builder = new StringBuilder();
            foreach (string item in items)
            {
                string value = item ?? string.Empty;
                builder
                    .Append(Encoding.UTF8.GetByteCount(value)
                        .ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(value)
                    .Append('\n');
            }
            return ByteString.From(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        /// <summary>
        /// Takes the SHA-256 of the Annex G.3 encoding of a sequence.
        /// </summary>
        /// <param name="items">The strings, in the order the caller states.</param>
        /// <returns>The 32-octet digest.</returns>
        public static ByteString SequenceDigest(ArrayOf<string> items)
        {
            byte[] encoded = EncodeSequence(items).Span.ToArray();
#if NET6_0_OR_GREATER
            return ByteString.From(SHA256.HashData(encoded));
#else
            using var algorithm = SHA256.Create();
            return ByteString.From(algorithm.ComputeHash(encoded));
#endif
        }

        /// <summary>
        /// Computes the <c>ViewVersion</c> of Section 12.6 from a View's
        /// resolved membership.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The membership is a <em>set</em>: a Node the View reaches through
        /// more than one organized group is one member and contributes once,
        /// because a View organizes a Node or it does not. Duplicates are
        /// removed, the remainder is sorted ascending by Unicode code point -
        /// which is not UTF-16 code-unit order - encoded as Annex G.3 defines,
        /// and the first four octets of the SHA-256 digest are read as a
        /// big-endian UInt32.
        /// </para>
        /// <para>
        /// A value of zero is reported as one, because OPC 10000-3 requires a
        /// <c>ViewVersion</c> greater than zero. A UInt32 cannot separate every
        /// membership, so the clause admits that two different memberships may
        /// compute the same value: a Client treats inequality as proof that the
        /// membership changed and equality as evidence rather than proof.
        /// </para>
        /// </remarks>
        /// <param name="members">
        /// The portable ExpandedNodeId of each member, in any order.
        /// </param>
        /// <returns>The ViewVersion.</returns>
        public static uint ComputeViewVersion(ArrayOf<string> members)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>(members.Count);
            foreach (string member in members)
            {
                string value = member ?? string.Empty;
                if (distinct.Add(value))
                {
                    ordered.Add(value);
                }
            }
            ordered.Sort(WotCodePointComparer.Instance);

            ReadOnlySpan<byte> digest = SequenceDigest(ordered.ToArrayOf()).Span;
            uint value2 = ((uint)digest[0] << 24) |
                ((uint)digest[1] << 16) |
                ((uint)digest[2] << 8) |
                digest[3];
            return value2 == 0 ? 1u : value2;
        }

        /// <summary>
        /// Splits a browse path into its elements, honouring the <c>&amp;</c>
        /// escape so an escaped separator inside a name does not split it.
        /// </summary>
        /// <summary>
        /// Allocates a projection name or qualified identity segment within its caller's scope.
        /// </summary>
        internal static string AllocateName(string? candidate, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(candidate) ? "member" : candidate!;
            if (used.Add(name))
            {
                return name;
            }
            int suffix = 2;
            string unique = name + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            while (!used.Add(unique))
            {
                suffix++;
                unique = name + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            }
            return unique;
        }

        /// <summary>
        /// Gets the local authored BrowseName, falling back to the affordance key.
        /// </summary>
        internal static string AffordanceName(JsonElement affordance, string key)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                affordance.TryGetProperty("uav:browseName", out JsonElement browseName) &&
                browseName.ValueKind == JsonValueKind.String
                ? LocalName(browseName.GetString()) ?? key
                : key;
        }

        internal static string? LocalName(string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return null;
            }
            if (browseName!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = browseName.IndexOf(';', 4);
                return delimiter >= 0 && delimiter + 1 < browseName.Length
                    ? browseName[(delimiter + 1)..]
                    : null;
            }
            int colon = browseName.IndexOf(':', StringComparison.Ordinal);
            return colon >= 0 && colon + 1 < browseName.Length
                ? browseName[(colon + 1)..]
                : browseName;
        }

        /// <summary>
        /// Gets whether an ExpandedNodeId identifier names one of the four
        /// OPC 10000-6 identifier types.
        /// </summary>
        private static bool HasIdentifierType(string identifier)
        {
            return identifier.Length > 2 &&
                identifier[1] == '=' &&
                identifier[0] is 'i' or 's' or 'g' or 'b';
        }

        /// <summary>
        /// Percent-encodes the characters an OPC 10000-4 relative path gives a
        /// meaning to, so a NamespaceUri cannot end an element early.
        /// </summary>
        private static string EscapeNamespaceUri(string namespaceUri)
        {
            var builder = new StringBuilder(namespaceUri.Length);
            foreach (char character in namespaceUri)
            {
                switch (character)
                {
                    case '/':
                        builder.Append("%2F");
                        break;
                    case ':':
                        builder.Append("%3A");
                        break;
                    case ';':
                        builder.Append("%3B");
                        break;
                    case '%':
                        builder.Append("%25");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Escapes the OPC 10000-4 Annex A.2 reserved characters inside a name.
        /// </summary>
        private static string EscapeName(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char character in name)
            {
                if (character is '/' or '.' or '<' or '>' or ':' or '#' or '!' or '&')
                {
                    builder.Append('&');
                }
                builder.Append(character);
            }
            return builder.ToString();
        }
    }
}
