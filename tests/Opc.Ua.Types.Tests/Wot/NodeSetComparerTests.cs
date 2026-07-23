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

using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
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
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                document.Load(reader);
            }
            byte[] compact = Encoding.UTF8.GetBytes(document.OuterXml);

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(indented, compact);

            Assert.That(result.AreEquivalent, Is.True);
        }

        [Test]
        public void RoundtripReportConfirmsNativePreservationWithoutEnvelope()
        {
            NodeSetRoundtripReport report = NodeSetComparer.Roundtrip(
                WotTestData.CreateRichNodeSet());

            Assert.That(report.NativeProjectionPreserved, Is.True);
            Assert.That(report.UsedPreservationEnvelope, Is.False);
            Assert.That(report.EnvelopePreserved, Is.False);
            Assert.That(report.Comparison.AreEquivalent, Is.True);
            Assert.That(
                report.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False);
        }
    }
}
