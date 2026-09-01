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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.Export
{
    /// <summary>
    /// The result of comparing two NodeSet2 documents after normalizing
    /// insignificant XML serialization differences.
    /// </summary>
    public sealed class NodeSetComparisonResult
    {
        internal NodeSetComparisonResult(bool equivalent, IReadOnlyList<string> differences)
        {
            AreEquivalent = equivalent;
            Differences = differences;
        }

        /// <summary>Gets a value indicating whether the documents are semantically equivalent.</summary>
        public bool AreEquivalent { get; }

        /// <summary>Gets the human-readable differences, empty when equivalent.</summary>
        public IReadOnlyList<string> Differences { get; }
    }

    /// <summary>
    /// Compares NodeSet2 documents on a canonical basis.
    /// The canonical form ignores indentation, line endings and attribute order
    /// while preserving element structure, attribute values and text so that
    /// semantic changes are detected.
    /// </summary>
    public static class NodeSetComparer
    {
        /// <summary>
        /// Compares two NodeSet2 documents on a canonical basis.
        /// </summary>
        /// <param name="left">The first document.</param>
        /// <param name="right">The second document.</param>
        /// <param name="options">
        /// Optional resource limits used during comparison. The alias policy
        /// is not read: this comparison answers whether a document was
        /// reproduced as written, and a name is part of how it is written.
        /// </param>
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult Compare(
            UANodeSet left,
            UANodeSet right,
            NodeSetComparisonOptions? options = null)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            return CompareXml(Serialize(left), Serialize(right), options);
        }

        /// <summary>
        /// Compares two serialized NodeSet2 documents on a canonical basis.
        /// </summary>
        /// <param name="left">The first serialized document.</param>
        /// <param name="right">The second serialized document.</param>
        /// <param name="options">
        /// Optional resource limits used during comparison. The alias policy
        /// is not read, for the reason given on <see cref="Compare"/>.
        /// </param>
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult CompareXml(
            ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right,
            NodeSetComparisonOptions? options = null)
        {
            options ??= new NodeSetComparisonOptions();
            options.Validate();

            string canonicalLeft;
            string canonicalRight;
            try
            {
                canonicalLeft = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(left).ToArray()),
                    options.MaxXmlDepth);
                canonicalRight = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(right).ToArray()),
                    options.MaxXmlDepth);
            }
            catch (FormatException ex)
            {
                return new NodeSetComparisonResult(false, [ex.Message]);
            }
            return BuildResult(canonicalLeft, canonicalRight);
        }

        /// <summary>
        /// Compares two NodeSet2 documents for <i>equivalence</i> rather than
        /// canonical text equality.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A NodeSet may write a DataType or a ReferenceType either as an
        /// alias its own <c>Aliases</c> table declares or as the identifier
        /// that alias stands for, and the two say the same thing. A caller
        /// that asks whether a document reproduces the <em>content</em> of
        /// another - a converter proving that it lost nothing, for instance -
        /// is answered wrongly by a text comparison, which reports a
        /// difference in spelling as a difference in content.
        /// </para>
        /// <para>
        /// This resolves each side through its own table and drops the table
        /// itself, which is only the definition of the shorthand. A name a
        /// document does not declare is answered by
        /// <see cref="NodeSetComparisonOptions.AliasResolver"/>, and left
        /// exactly as written when no policy was supplied: it is not an alias
        /// then, and a document that uses one cannot be imported at all. It is
        /// separate from <see cref="Compare"/> because that comparison is used
        /// where exact reproduction is the question, and loosening it there
        /// would stop it answering that question.
        /// </para>
        /// </remarks>
        /// <param name="left">The first document.</param>
        /// <param name="right">The second document.</param>
        /// <param name="options">
        /// Optional resource limits and alias policy used during comparison.
        /// </param>
        /// <returns>The comparison result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="left"/> or <paramref name="right"/> is <c>null</c>.
        /// </exception>
        public static NodeSetComparisonResult CompareEquivalent(
            UANodeSet left,
            UANodeSet right,
            NodeSetComparisonOptions? options = null)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            options ??= new NodeSetComparisonOptions();
            options.Validate();

            string canonicalLeft;
            string canonicalRight;
            try
            {
                canonicalLeft = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(Serialize(left)).ToArray()),
                    options.MaxXmlDepth,
                    options.AliasResolver,
                    resolveAliases: true);
                canonicalRight = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(Serialize(right)).ToArray()),
                    options.MaxXmlDepth,
                    options.AliasResolver,
                    resolveAliases: true);
            }
            catch (FormatException ex)
            {
                return new NodeSetComparisonResult(false, [ex.Message]);
            }
            return BuildResult(canonicalLeft, canonicalRight);
        }

        private static NodeSetComparisonResult BuildResult(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                return new NodeSetComparisonResult(true, []);
            }
            return new NodeSetComparisonResult(false, [DescribeDifference(left, right)]);
        }

        private static string DescribeDifference(string left, string right)
        {
            int limit = Math.Min(left.Length, right.Length);
            int index = 0;
            while (index < limit && left[index] == right[index])
            {
                index++;
            }
            int start = Math.Max(0, index - 24);
            string leftContext = Excerpt(left, start, index);
            string rightContext = Excerpt(right, start, index);
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Canonical NodeSet documents differ at position {0}: '{1}' vs '{2}'.",
                index,
                leftContext,
                rightContext);
        }

        private static string Excerpt(string text, int start, int index)
        {
            int end = Math.Min(text.Length, index + 24);
            return text.Substring(start, end - start);
        }

        private static string Canonicalize(
            string xml,
            int maxXmlDepth,
            INodeSetAliasResolver? fallback = null,
            bool resolveAliases = false)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };
            XDocument document;
            using (var stringReader = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(stringReader, settings))
            {
                document = XDocument.Load(reader);
            }
            var builder = new StringBuilder();
            if (document.Root is not null)
            {
                // The document's own declarations answer first and the caller's
                // policy answers for the rest, which is the only order in which
                // a document may be read: a name it declares means what it says
                // it means.
                INodeSetAliasResolver? aliases = resolveAliases
                    ? NodeSetDeclaredAliases.FromDocument(document.Root, fallback)
                    : null;
                WriteElement(builder, document.Root, 1, maxXmlDepth, aliases);
            }
            return builder.ToString();
        }

        private static void WriteElement(
            StringBuilder builder,
            XElement element,
            int depth,
            int maxXmlDepth,
            INodeSetAliasResolver? aliases)
        {
            if (depth > maxXmlDepth)
            {
                throw new FormatException(
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "NodeSet XML exceeds the configured maximum depth of {0}.",
                        maxXmlDepth));
            }

            bool isReference = string.Equals(
                element.Name.LocalName, ReferenceElement, StringComparison.Ordinal);
            if (aliases is not null &&
                string.Equals(element.Name.LocalName, AliasesElement, StringComparison.Ordinal))
            {
                // The table defines the shorthand every other element is read
                // through, so once both sides are resolved it states nothing a
                // comparison of content should see.
                return;
            }

            builder.Append('<').Append(element.Name.ToString());

            var attributes = new List<XAttribute>(element.Attributes());
            attributes.Sort(static (left, right) =>
                string.CompareOrdinal(left.Name.ToString(), right.Name.ToString()));
            foreach (XAttribute attribute in attributes)
            {
                builder.Append(' ')
                    .Append(attribute.Name.ToString())
                    .Append("=\"")
                    .Append(IsAliasedAttribute(attribute.Name.LocalName)
                        ? Resolve(attribute.Value, aliases)
                        : attribute.Value)
                    .Append('"');
            }
            builder.Append('>');

            foreach (XNode node in element.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        WriteElement(builder, child, depth + 1, maxXmlDepth, aliases);
                        break;
                    case XText text:
                        // A Reference's text is its target NodeId, which an
                        // alias may stand for. No other element's text is a
                        // NodeId, so nothing else is rewritten.
                        builder.Append(isReference ? Resolve(text.Value, aliases) : text.Value);
                        break;
                }
            }

            builder.Append("</").Append(element.Name.ToString()).Append('>');
        }

        /// <summary>
        /// Tests whether an attribute is one an alias may legally stand in for.
        /// </summary>
        private static bool IsAliasedAttribute(string localName)
        {
            return string.Equals(localName, "DataType", StringComparison.Ordinal) ||
                string.Equals(localName, "ReferenceType", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves an alias-able value to the identifier it stands for.
        /// </summary>
        /// <remarks>
        /// The resolver reads the document's own table first, so a name it
        /// declares means what the document says it means. A name it does not
        /// declare is answered by the policy the caller injected, and is left
        /// as written when there is none: a NodeSet2 document that writes
        /// <c>HasComponent</c> without declaring it cannot be imported at all,
        /// and reading it here as <c>i=47</c> would let a comparison call an
        /// unloadable document equivalent to a loadable one.
        /// </remarks>
        private static string Resolve(string value, INodeSetAliasResolver? aliases)
        {
            if (aliases is not null && aliases.TryResolve(value, out string resolved))
            {
                return resolved;
            }
            return value;
        }

        private const string AliasesElement = "Aliases";
        private const string ReferenceElement = "Reference";

        private static byte[] Serialize(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static ReadOnlySpan<byte> StripPreamble(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return bytes[3..];
            }
            return bytes;
        }
    }
}
