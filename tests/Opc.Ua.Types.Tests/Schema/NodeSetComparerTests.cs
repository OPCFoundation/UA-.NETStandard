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
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Types.Tests.Wot;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Schema
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class NodeSetComparerTests
    {
        [Test]
        public void IdenticalNodeSetsAreEquivalent()
        {
            NodeSetComparisonResult result = NodeSetComparer.Compare(
                WotTestData.CreateReconstructableNodeSet(),
                WotTestData.CreateReconstructableNodeSet());

            Assert.That(result.AreEquivalent, Is.True);
            Assert.That(result.Differences, Is.Empty);
        }

        /// <summary>
        /// An alias is a shorthand a NodeSet declares for itself, so a DataType
        /// written as <c>Double</c> beside a declared alias and one written as
        /// <c>i=11</c> state the same fact. <c>Compare</c> answers whether a
        /// document was reproduced exactly and reports the difference;
        /// <c>CompareEquivalent</c> answers the §9.2 question and does not.
        /// </summary>
        [Test]
        public void AnAliasAndTheIdentifierItStandsForAreEquivalentButNotIdentical()
        {
            // The fixture declares "Double", so both spellings are legal here;
            // a document that used the name without declaring it could not be
            // imported and is not the same document at all.
            UANodeSet aliased = WotTestData.CreateReconstructableNodeSet();
            aliased.Items!.OfType<UAVariable>().Single().DataType = "Double";
            UANodeSet expanded = WotTestData.CreateReconstructableNodeSet();
            expanded.Items!.OfType<UAVariable>().Single().DataType = "i=11";

            Assert.That(
                NodeSetComparer.Compare(aliased, expanded).AreEquivalent,
                Is.False,
                "Compare answers whether the document was reproduced as written.");
            Assert.That(
                NodeSetComparer.CompareEquivalent(aliased, expanded).AreEquivalent,
                Is.True,
                "CompareEquivalent must read each side through its own alias table.");
        }

        /// <summary>
        /// Resolving aliases must not blunt the comparison: a DataType that
        /// genuinely differs is still a difference.
        /// </summary>
        [Test]
        public void CompareEquivalentStillDetectsAChangedDataType()
        {
            UANodeSet aliased = WotTestData.CreateReconstructableNodeSet();
            aliased.Aliases = [new NodeIdAlias { Alias = "Double", Value = "i=11" }];
            aliased.Items!.OfType<UAVariable>().Single().DataType = "Double";
            UANodeSet other = WotTestData.CreateReconstructableNodeSet();
            other.Items!.OfType<UAVariable>().Single().DataType = "i=12";

            Assert.That(
                NodeSetComparer.CompareEquivalent(aliased, other).AreEquivalent,
                Is.False);
        }

        /// <summary>
        /// An alias name is only a shorthand where an alias is legal. A
        /// BrowseName that happens to read like one is ordinary text.
        /// </summary>
        [Test]
        public void CompareEquivalentDoesNotRewriteTextThatMerelyMatchesAnAliasName()
        {
            // The BrowseName is exactly the alias name, so a resolver that did
            // not check where an alias is legal would rewrite it to i=11 and
            // call these two documents the same.
            UANodeSet left = WotTestData.CreateReconstructableNodeSet();
            left.Aliases = [new NodeIdAlias { Alias = "Double", Value = "i=11" }];
            left.Items!.OfType<UAVariable>().Single().BrowseName = "Double";
            UANodeSet right = WotTestData.CreateReconstructableNodeSet();
            right.Aliases = [new NodeIdAlias { Alias = "Double", Value = "i=11" }];
            right.Items!.OfType<UAVariable>().Single().BrowseName = "i=11";

            Assert.That(
                NodeSetComparer.CompareEquivalent(left, right).AreEquivalent,
                Is.False,
                "Only a DataType, a ReferenceType and a Reference's target may be an alias.");
        }

        /// <summary>
        /// An alias name is the document's own choice of shorthand, so two
        /// documents that declare different names for one identifier state the
        /// same fact and only spell it differently.
        /// </summary>
        [Test]
        public void TwoDifferentlyNamedDeclaredAliasesForOneIdentifierAreEquivalent()
        {
            UANodeSet left = WotTestData.CreateReconstructableNodeSet();
            left.Aliases = [new NodeIdAlias { Alias = "Double", Value = "i=11" }];
            left.Items!.OfType<UAVariable>().Single().DataType = "Double";
            UANodeSet right = WotTestData.CreateReconstructableNodeSet();
            right.Aliases = [new NodeIdAlias { Alias = "TheDoubleType", Value = "i=11" }];
            right.Items!.OfType<UAVariable>().Single().DataType = "TheDoubleType";

            Assert.That(
                NodeSetComparer.Compare(left, right).AreEquivalent,
                Is.False,
                "Compare answers whether the document was reproduced as written.");
            Assert.That(
                NodeSetComparer.CompareEquivalent(left, right).AreEquivalent,
                Is.True,
                "Each side resolves through its own table, so the names need not agree.");
        }

        /// <summary>
        /// A name a document does not declare is not an alias, however
        /// standard it reads. A NodeSet2 that writes <c>Double</c> without
        /// declaring it cannot be imported at all, so resolving it here would
        /// report an unloadable document as equivalent to a loadable one.
        /// </summary>
        [Test]
        public void CompareEquivalentDoesNotResolveAnUndeclaredStandardName()
        {
            UANodeSet undeclared = WotTestData.CreateReconstructableNodeSet();
            undeclared.Aliases = null;
            undeclared.Items!.OfType<UAVariable>().Single().DataType = "Double";
            UANodeSet expanded = WotTestData.CreateReconstructableNodeSet();
            expanded.Aliases = null;
            expanded.Items!.OfType<UAVariable>().Single().DataType = "i=11";

            Assert.That(
                NodeSetComparer.CompareEquivalent(undeclared, expanded).AreEquivalent,
                Is.False,
                "Comparison states what the documents say, not what they could have declared.");
        }

        [Test]
        public void SemanticChangeIsDetected()
        {
            UANodeSet modified = WotTestData.CreateReconstructableNodeSet();
            modified.Items!.OfType<UAVariable>().Single().BrowseName = "1:Changed";

            NodeSetComparisonResult result = NodeSetComparer.Compare(
                WotTestData.CreateReconstructableNodeSet(),
                modified);

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
        }

        [Test]
        public void FormattingDifferencesAreNormalized()
        {
            byte[] indented = WotTestData.Serialize(WotTestData.CreateReconstructableNodeSet());

            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using (var stream = new System.IO.MemoryStream(indented))
            using (var reader = XmlReader.Create(stream, settings))
            {
                document.Load(reader);
            }
            byte[] compact = Encoding.UTF8.GetBytes(document.OuterXml);

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(indented.AsSpan(), compact.AsSpan());

            Assert.That(result.AreEquivalent, Is.True);
        }

        [Test]
        public void CompareXmlAcceptsReadOnlySpanSlices()
        {
            byte[] xml = CreateNestedXml(2);
            byte[] paddedLeft = [0, .. xml, 0];
            byte[] paddedRight = [1, .. xml, 1];

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(
                paddedLeft.AsSpan(1, xml.Length),
                paddedRight.AsSpan(1, xml.Length));

            Assert.That(result.AreEquivalent, Is.True);
        }

        [Test]
        public void MaxXmlDepthAllowsDocumentAtConfiguredLimit()
        {
            byte[] xml = CreateNestedXml(4);
            var options = new NodeSetComparisonOptions
            {
                MaxXmlDepth = 4
            };

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(xml, xml, options);

            Assert.That(result.AreEquivalent, Is.True);
            Assert.That(result.Differences, Is.Empty);
        }

        [Test]
        public void MaxXmlDepthRejectsDocumentPastConfiguredLimit()
        {
            byte[] xml = CreateNestedXml(5);
            var options = new NodeSetComparisonOptions
            {
                MaxXmlDepth = 4
            };

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(xml, xml, options);

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(
                result.Differences,
                Has.Some.Contains("NodeSet XML exceeds the configured maximum depth of 4."));
        }

        [Test]
        public void RoundtripReportConfirmsNativePreservationWithoutEnvelope()
        {
            WotNodeSetRoundtripReport report = WotNodeSetRoundtrip.Run(
                WotTestData.CreateRichNodeSet());

            Assert.That(report.NativeProjectionPreserved, Is.True);
            Assert.That(report.UsedPreservationEnvelope, Is.False);
            Assert.That(report.EnvelopePreserved, Is.False);
            Assert.That(report.Comparison.AreEquivalent, Is.True);
            Assert.That(
                report.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False);
        }

        private static byte[] CreateNestedXml(int depth)
        {
            var builder = new StringBuilder();
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("<n").Append(ii).Append('>');
            }
            for (int ii = depth - 1; ii >= 0; ii--)
            {
                builder.Append("</n").Append(ii).Append('>');
            }
            return Encoding.UTF8.GetBytes(builder.ToString());
        }
    }
}
