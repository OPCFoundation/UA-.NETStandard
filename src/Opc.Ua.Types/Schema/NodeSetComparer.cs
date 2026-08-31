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
using Opc.Ua.Wot;

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
    /// The result of a NodeSet2 to WoT to NodeSet2 round trip.
    /// </summary>
    public sealed class NodeSetRoundtripReport
    {
        internal NodeSetRoundtripReport(
            bool nativeProjectionPreserved,
            bool envelopePreserved,
            bool usedPreservationEnvelope,
            NodeSetComparisonResult comparison,
            IReadOnlyList<WotDiagnostic> diagnostics)
        {
            NativeProjectionPreserved = nativeProjectionPreserved;
            EnvelopePreserved = envelopePreserved;
            UsedPreservationEnvelope = usedPreservationEnvelope;
            Comparison = comparison;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Gets a value indicating whether the structured native projection,
        /// without an envelope, reproduced an equivalent NodeSet2.
        /// </summary>
        public bool NativeProjectionPreserved { get; }

        /// <summary>
        /// Gets a value indicating whether the envelope reproduced a byte-identical NodeSet2.
        /// </summary>
        public bool EnvelopePreserved { get; }

        /// <summary>
        /// Gets a value indicating whether the conversion used a
        /// <c>uav:nodeSet</c> preservation envelope.
        /// </summary>
        public bool UsedPreservationEnvelope { get; }

        /// <summary>Gets the canonical comparison of the source and restored NodeSet2.</summary>
        public NodeSetComparisonResult Comparison { get; }

        /// <summary>Gets the diagnostics produced during the round trip.</summary>
        public IReadOnlyList<WotDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// Compares NodeSet2 documents on a canonical basis and reports round trips.
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
        /// <param name="options">Optional resource limits used during comparison.</param>
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult Compare(
            UANodeSet left,
            UANodeSet right,
            WotNodeSetConverterOptions? options = null)
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
        /// <param name="options">Optional resource limits used during comparison.</param>
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult CompareXml(
            ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right,
            WotNodeSetConverterOptions? options = null)
        {
            options ??= new WotNodeSetConverterOptions();
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
        /// <i>OPC UA — WoT Binding</i> §9.2 asks whether a readable document
        /// reproduces an <b>equivalent</b> UANodeSet, and text equality is
        /// strictly stronger than that. A NodeSet may write a DataType or a
        /// ReferenceType either as an alias its own <c>Aliases</c> table
        /// declares or as the identifier that alias stands for, and the two say
        /// the same thing. Comparing them as text reports a difference in
        /// spelling as a difference in content.
        /// </para>
        /// <para>
        /// This resolves each side through its own table and drops the table
        /// itself, which is only the definition of the shorthand. It is separate
        /// from <see cref="Compare"/> because that comparison is used where
        /// exact reproduction is the question, and loosening it there would stop
        /// it answering that question.
        /// </para>
        /// </remarks>
        /// <param name="left">The first document.</param>
        /// <param name="right">The second document.</param>
        /// <param name="options">Optional resource limits used during comparison.</param>
        /// <returns>The comparison result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="left"/> or <paramref name="right"/> is <c>null</c>.
        /// </exception>
        public static NodeSetComparisonResult CompareEquivalent(
            UANodeSet left,
            UANodeSet right,
            WotNodeSetConverterOptions? options = null)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            string canonicalLeft;
            string canonicalRight;
            try
            {
                canonicalLeft = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(Serialize(left)).ToArray()),
                    options.MaxXmlDepth,
                    resolveAliases: true);
                canonicalRight = Canonicalize(
                    Encoding.UTF8.GetString(StripPreamble(Serialize(right)).ToArray()),
                    options.MaxXmlDepth,
                    resolveAliases: true);
            }
            catch (FormatException ex)
            {
                return new NodeSetComparisonResult(false, [ex.Message]);
            }
            return BuildResult(canonicalLeft, canonicalRight);
        }

        /// <summary>
        /// Converts a NodeSet2 document to a WoT document and back. By default,
        /// the report uses native-only mode so completeness is never proved by
        /// the preservation envelope.
        /// </summary>
        /// <param name="source">The NodeSet2 document to round trip.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The round-trip report.</returns>
        public static NodeSetRoundtripReport Roundtrip(
            UANodeSet source,
            WotNodeSetConverterOptions? options = null)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var diagnostics = new List<WotDiagnostic>();
            byte[] sourceBytes = Serialize(source);
            WotNodeSetConverterOptions effectiveOptions = options ??
                new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                };

            WotConversionResult<WotDocument> forward =
                WotNodeSetConverter.FromNodeSetResult(source, null, effectiveOptions);
            AddRange(diagnostics, forward.Diagnostics);
            if (forward.Value is null)
            {
                return new NodeSetRoundtripReport(
                    false,
                    false,
                    false,
                    new NodeSetComparisonResult(false, ["The NodeSet could not be converted to a WoT document."]),
                    diagnostics);
            }

            using WotDocument document = forward.Value;
            bool usedEnvelope = document.TryGetEnvelope(out _);
            WotConversionResult<UANodeSet> backward =
                WotNodeSetConverter.ToNodeSetResult(document, effectiveOptions);
            AddRange(diagnostics, backward.Diagnostics);
            if (backward.Value is null)
            {
                return new NodeSetRoundtripReport(
                    false,
                    false,
                    usedEnvelope,
                    new NodeSetComparisonResult(false, ["The WoT document could not be converted back to a NodeSet."]),
                    diagnostics);
            }

            byte[] restoredBytes = Serialize(backward.Value);
            NodeSetComparisonResult comparison = CompareXml(sourceBytes, restoredBytes, effectiveOptions);
            bool byteIdentical = ByteEquals(sourceBytes, restoredBytes);
            return new NodeSetRoundtripReport(
                !usedEnvelope && comparison.AreEquivalent,
                usedEnvelope && byteIdentical,
                usedEnvelope,
                comparison,
                diagnostics);
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
            string xml, int maxXmlDepth, bool resolveAliases = false)
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
                Dictionary<string, string>? aliases = resolveAliases
                    ? ReadAliases(document.Root)
                    : null;
                WriteElement(builder, document.Root, 1, maxXmlDepth, aliases);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Reads a NodeSet's own <c>Aliases</c> table.
        /// </summary>
        private static Dictionary<string, string> ReadAliases(XElement root)
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement table in root.Elements())
            {
                if (!string.Equals(table.Name.LocalName, AliasesElement, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (XElement alias in table.Elements())
                {
                    string? name = alias.Attribute("Alias")?.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        aliases[name!] = alias.Value;
                    }
                }
            }
            return aliases;
        }

        private static void WriteElement(
            StringBuilder builder,
            XElement element,
            int depth,
            int maxXmlDepth,
            Dictionary<string, string>? aliases)
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
        /// The document's own table is consulted first, then the standard
        /// base-namespace names. Declaring a standard name changes nothing
        /// about what a document means - a <c>ReferenceType</c> of
        /// <c>HasComponent</c> denotes <c>i=47</c> whether or not the table
        /// spells that out - so a comparison that saw the difference would
        /// report two equivalent documents as different purely because one
        /// completed its table and the other did not.
        /// </remarks>
        private static string Resolve(string value, Dictionary<string, string>? aliases)
        {
            if (aliases is null)
            {
                return value;
            }
            if (aliases.TryGetValue(value, out string? resolved))
            {
                return resolved;
            }
            return WotNodeSetAliases.TryResolveStandardName(value, out string standard)
                ? standard
                : value;
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

        private static bool ByteEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int ii = 0; ii < left.Length; ii++)
            {
                if (left[ii] != right[ii])
                {
                    return false;
                }
            }
            return true;
        }

        private static void AddRange(List<WotDiagnostic> target, IReadOnlyList<WotDiagnostic> source)
        {
            for (int ii = 0; ii < source.Count; ii++)
            {
                target.Add(source[ii]);
            }
        }
    }
}
