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
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Additional tests for NodeSetComparer covering difference-reporting,
    /// guards, and roundtrip-with-envelope paths.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class NodeSetComparerAdditionalTests
    {
        [Test]
        public void DifferenceDescriptionContainsPositionInfo()
        {
            UANodeSet original = WotTestData.CreateReconstructableNodeSet();
            UANodeSet modified = WotTestData.CreateReconstructableNodeSet();
            modified.Items!.OfType<UAVariable>().First().BrowseName = "1:Changed";

            NodeSetComparisonResult result = NodeSetComparer.Compare(original, modified);

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
            Assert.That(result.Differences[0], Does.Contain("position"));
        }

        [Test]
        public void NodeAddedToRightIsDetected()
        {
            UANodeSet original = WotTestData.CreateReconstructableNodeSet();
            UANodeSet modified = WotTestData.CreateReconstructableNodeSet();
            var extraItems = modified.Items!.ToList();
            extraItems.Add(new UAObjectType { NodeId = "ns=1;i=9999", BrowseName = "1:ExtraType" });
            modified.Items = [.. extraItems];

            NodeSetComparisonResult result = NodeSetComparer.Compare(original, modified);

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
        }

        [Test]
        public void NodeRemovedFromRightIsDetected()
        {
            UANodeSet original = WotTestData.CreateReconstructableNodeSet();
            UANodeSet modified = WotTestData.CreateReconstructableNodeSet();

            // Remove the last node from the right side.
            if (modified.Items is { Length: > 1 })
            {
                modified.Items = modified.Items.Take(modified.Items.Length - 1).ToArray();
            }

            NodeSetComparisonResult result = NodeSetComparer.Compare(original, modified);

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
        }

        [Test]
        public void CompareNullLeftThrows()
        {
            UANodeSet right = WotTestData.CreateReconstructableNodeSet();

            Assert.That(
                () => NodeSetComparer.Compare(null!, right),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void CompareNullRightThrows()
        {
            UANodeSet left = WotTestData.CreateReconstructableNodeSet();

            Assert.That(
                () => NodeSetComparer.Compare(left, null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void CompareXmlDifferentLeftSpanReportsDifference()
        {
            byte[] right = WotTestData.Serialize(WotTestData.CreateReconstructableNodeSet());
            byte[] left = System.Text.Encoding.UTF8.GetBytes("<Different />");

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(left.AsSpan(), right.AsSpan());

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
        }

        [Test]
        public void CompareXmlDifferentRightSpanReportsDifference()
        {
            byte[] left = WotTestData.Serialize(WotTestData.CreateReconstructableNodeSet());
            byte[] right = System.Text.Encoding.UTF8.GetBytes("<Different />");

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(left.AsSpan(), right.AsSpan());

            Assert.That(result.AreEquivalent, Is.False);
            Assert.That(result.Differences, Is.Not.Empty);
        }

        [Test]
        public void RoundtripNullSourceThrows()
        {
            Assert.That(
                () => WotNodeSetRoundtrip.Run(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void RoundtripWithAlwaysPreserveFlagsEnvelopePreserved()
        {
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };

            WotNodeSetRoundtripReport report = WotNodeSetRoundtrip.Run(
                WotTestData.CreateReconstructableNodeSet(),
                options);

            Assert.That(report.UsedPreservationEnvelope, Is.True);
            Assert.That(report.EnvelopePreserved, Is.True);
            Assert.That(report.Comparison.AreEquivalent, Is.True);
        }

        [Test]
        public void CompareXmlHandlesBomPrefixedDocumentCorrectly()
        {
            byte[] serialized = WotTestData.Serialize(WotTestData.CreateReconstructableNodeSet());

            // Strip any existing UTF-8 BOM so that the manually-prepended BOM
            // is the only one and StripPreamble removes exactly it.
            byte[] xmlOnly = serialized.Length >= 3
                && serialized[0] == 0xEF && serialized[1] == 0xBB && serialized[2] == 0xBF
                ? serialized.Skip(3).ToArray()
                : serialized;
            byte[] withBom = [0xEF, 0xBB, 0xBF, .. xmlOnly];

            NodeSetComparisonResult result = NodeSetComparer.CompareXml(xmlOnly, withBom);

            Assert.That(result.AreEquivalent, Is.True);
        }
    }
}
