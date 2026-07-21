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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
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
            bool envelopePreserved,
            NodeSetComparisonResult comparison,
            IReadOnlyList<WotDiagnostic> diagnostics)
        {
            EnvelopePreserved = envelopePreserved;
            Comparison = comparison;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Gets a value indicating whether the envelope reproduced a byte-identical NodeSet2.
        /// </summary>
        public bool EnvelopePreserved { get; }

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
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult Compare(UANodeSet left, UANodeSet right)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            return CompareXml(Serialize(left), Serialize(right));
        }

        /// <summary>
        /// Compares two serialized NodeSet2 documents on a canonical basis.
        /// </summary>
        /// <param name="left">The first serialized document.</param>
        /// <param name="right">The second serialized document.</param>
        /// <returns>The comparison result.</returns>
        public static NodeSetComparisonResult CompareXml(byte[] left, byte[] right)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            string canonicalLeft = Canonicalize(Encoding.UTF8.GetString(StripPreamble(left)));
            string canonicalRight = Canonicalize(Encoding.UTF8.GetString(StripPreamble(right)));
            return BuildResult(canonicalLeft, canonicalRight);
        }

        /// <summary>
        /// Converts a NodeSet2 document to a WoT document carrying a preservation
        /// envelope and back, then reports whether the round trip preserved it.
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

            WotConversionResult<WotDocument> forward =
                WotNodeSetConverter.FromNodeSetResult(source, null, options);
            AddRange(diagnostics, forward.Diagnostics);
            if (forward.Value is null)
            {
                return new NodeSetRoundtripReport(
                    false,
                    new NodeSetComparisonResult(false, ["The NodeSet could not be converted to a WoT document."]),
                    diagnostics);
            }

            using WotDocument document = forward.Value;
            WotConversionResult<UANodeSet> backward =
                WotNodeSetConverter.ToNodeSetResult(document, options);
            AddRange(diagnostics, backward.Diagnostics);
            if (backward.Value is null)
            {
                return new NodeSetRoundtripReport(
                    false,
                    new NodeSetComparisonResult(false, ["The WoT document could not be converted back to a NodeSet."]),
                    diagnostics);
            }

            byte[] restoredBytes = Serialize(backward.Value);
            bool envelopePreserved = ByteEquals(sourceBytes, restoredBytes);
            NodeSetComparisonResult comparison = CompareXml(sourceBytes, restoredBytes);
            return new NodeSetRoundtripReport(envelopePreserved, comparison, diagnostics);
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

        private static string Canonicalize(string xml)
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
                WriteElement(builder, document.Root);
            }
            return builder.ToString();
        }

        private static void WriteElement(StringBuilder builder, XElement element)
        {
            builder.Append('<').Append(element.Name.ToString());

            var attributes = new List<XAttribute>(element.Attributes());
            attributes.Sort(static (left, right) =>
                string.CompareOrdinal(left.Name.ToString(), right.Name.ToString()));
            foreach (XAttribute attribute in attributes)
            {
                builder.Append(' ')
                    .Append(attribute.Name.ToString())
                    .Append("=\"")
                    .Append(attribute.Value)
                    .Append('"');
            }
            builder.Append('>');

            foreach (XNode node in element.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        WriteElement(builder, child);
                        break;
                    case XText text:
                        builder.Append(text.Value);
                        break;
                }
            }

            builder.Append("</").Append(element.Name.ToString()).Append('>');
        }

        private static byte[] Serialize(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static byte[] StripPreamble(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                var trimmed = new byte[bytes.Length - 3];
                Array.Copy(bytes, 3, trimmed, 0, trimmed.Length);
                return trimmed;
            }
            return bytes;
        }

        private static bool ByteEquals(byte[] left, byte[] right)
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
