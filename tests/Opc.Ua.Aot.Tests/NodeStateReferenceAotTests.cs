/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System.Xml;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes reference storage contracts directly without managed reflection or test hooks.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class NodeStateReferenceAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Pins original-object snapshots, indexed browse ordering and removal across all measured degrees.
        /// </summary>
        [Test]
        [Arguments(0)]
        [Arguments(1)]
        [Arguments(2)]
        [Arguments(4)]
        [Arguments(8)]
        [Arguments(16)]
        [Arguments(128)]
        [Arguments(1024)]
        public async Task ReferencePopulationPreservesIdentityIndexesAndSnapshotsAsync(int count)
        {
            var context = new SystemContext(fixture.Telemetry);
            var node = new BaseObjectState(null);
            var input = new List<IReference>();
            var oracle = new ReferenceDictionary<byte>();
            for (int index = 0; index < count; index++)
            {
                ExpandedNodeId target = index % 2 == 0
                    ? new NodeId((uint)(50000 + index), 2)
                    : new ExpandedNodeId(new NodeId((uint)(50000 + index)), "urn:reference:native");
                var reference = new NodeStateReference(ReferenceTypeIds.HasComponent, index % 3 == 0, target);
                input.Add(reference);
                oracle.Add(reference, 0);
            }
            node.AddReferences(input);
            var snapshot = new List<IReference>();
            node.GetReferences(context, snapshot);
            await Assert.That(snapshot.Count).IsEqualTo(count);
            for (int index = 0; index < count; index++)
            {
                IReference reference = input[index];
                await Assert.That(ReferenceEquals(snapshot[index], reference)).IsTrue();
                await Assert.That(node.ReferenceExists(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)).IsTrue();
                await Assert.That(node.AddReferenceIfMissing(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)).IsFalse();
            }
            foreach (bool inverse in new[] { false, true })
            {
                using INodeBrowser browser = node.CreateBrowser(
                    context, null, ReferenceTypeIds.HasComponent, false,
                    inverse ? BrowseDirection.Inverse : BrowseDirection.Forward, default, null, false);
                foreach (IReference expected in oracle.Find(ReferenceTypeIds.HasComponent, inverse))
                {
                    await Assert.That(ReferenceEquals(browser.Next(), expected)).IsTrue();
                }
                await Assert.That(browser.Next()).IsNull();
            }
            var clone = (BaseObjectState)node.Clone();
            for (int index = count - 1; index >= 0; index--)
            {
                IReference reference = input[index];
                await Assert.That(node.RemoveReference(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)).IsTrue();
                await Assert.That(node.ReferenceExists(
                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)).IsFalse();
            }
            var remaining = new List<IReference>();
            node.GetReferences(context, remaining);
            await Assert.That(remaining.Count).IsEqualTo(0);
            clone.GetReferences(context, remaining);
            await Assert.That(remaining.Count).IsEqualTo(count);
            for (int index = 0; index < count; index++)
            {
                await Assert.That(ReferenceEquals(remaining[index], input[index])).IsTrue();
                await Assert.That(ReferenceEquals(snapshot[index], input[index])).IsTrue();
            }
        }

        /// <summary>
        /// Pins mutable insertion keys and node Target identity through copy and reference-stream encoding.
        /// </summary>
        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task MutableReferencesAndTargetsPreserveCopyAndEncodingContractsAsync(bool xml)
        {
            var context = new SystemContext(fixture.Telemetry);
            var messageContext = ServiceMessageContext.CreateEmpty(fixture.Telemetry);
            var node = new BaseObjectState(null);
            var mutable = new ReferenceNode(ReferenceTypeIds.Organizes, false, new NodeId(500u, 2));
            var target = new BaseObjectState(null) { NodeId = new NodeId(700u, 2) };
            var targetReference = new NodeStateReference(ReferenceTypeIds.HasComponent, false, target);
            var remote = new NodeStateReference(ReferenceTypeIds.HasProperty, false,
                new ExpandedNodeId(new NodeId(900u), "urn:reference:remote", 1));
            node.AddReferences([mutable, targetReference, remote]);
            mutable.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            mutable.IsInverse = true;
            mutable.TargetId = new NodeId(501u, 2);
            target.NodeId = new NodeId(701u, 2);
            var clone = (BaseObjectState)node.Clone();
            await Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(500u, 2))).IsTrue();
            await Assert.That(node.ReferenceExists(ReferenceTypeIds.HasProperty, true, new NodeId(501u, 2))).IsFalse();
            await Assert.That(clone.ReferenceExists(ReferenceTypeIds.HasProperty, true, new NodeId(501u, 2))).IsTrue();
            await Assert.That(clone.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(500u, 2))).IsFalse();
            var copied = new List<IReference>();
            clone.GetReferences(context, copied);
            await Assert.That(copied.Count).IsEqualTo(3);
            await Assert.That(ReferenceEquals(copied[0], mutable)).IsTrue();
            await Assert.That(ReferenceEquals(copied[1], targetReference)).IsTrue();
            await Assert.That(ReferenceEquals(targetReference.Target, target)).IsTrue();
            await Assert.That(targetReference.TargetId).IsEqualTo(new ExpandedNodeId(new NodeId(700u, 2)));
            var loaded = new BaseObjectState(null);
            if (xml)
            {
                using var encoder = new XmlEncoder(messageContext);
                encoder.Push("Root", Namespaces.OpcUaXsd);
                node.SaveReferences(context, encoder);
                encoder.Pop();
                using var text = new StringReader(encoder.CloseAndReturnText());
                using var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings());
                using var decoder = new XmlDecoder(null, reader, messageContext);
                loaded.UpdateReferences(context, decoder);
            }
            else
            {
                using var stream = new MemoryStream();
                using (var encoder = new BinaryEncoder(stream, messageContext, true))
                {
                    node.SaveReferences(context, encoder);
                }
                stream.Position = 0;
                using var decoder = new BinaryDecoder(stream, messageContext, true);
                loaded.UpdateReferences(context, decoder);
                await Assert.That(stream.Position).IsEqualTo(stream.Length);
            }
            var decoded = new List<IReference>();
            loaded.GetReferences(context, decoded);
            await Assert.That(decoded.Count).IsEqualTo(3);
            await Assert.That(decoded[0].ReferenceTypeId).IsEqualTo(ReferenceTypeIds.HasProperty);
            await Assert.That(decoded[0].IsInverse).IsTrue();
            await Assert.That(decoded[0].TargetId).IsEqualTo(new ExpandedNodeId(new NodeId(501u, 2)));
            await Assert.That(decoded[1].TargetId).IsEqualTo(new ExpandedNodeId(new NodeId(700u, 2)));
            await Assert.That(((NodeStateReference)decoded[1]).Target).IsNull();
            await Assert.That(decoded[2].TargetId.NamespaceUri).IsEqualTo("urn:reference:remote");
            await Assert.That(decoded[2].TargetId.ServerIndex).IsEqualTo(1u);
            await Assert.That(decoded[2].TargetId.InnerNodeId).IsEqualTo(new NodeId(900u));
            await Assert.That(ReferenceEquals(decoded[0], mutable)).IsFalse();
            await Assert.That(loaded.ReferenceExists(
                ReferenceTypeIds.HasProperty, true, new NodeId(501u, 2))).IsTrue();
        }
    }
}
