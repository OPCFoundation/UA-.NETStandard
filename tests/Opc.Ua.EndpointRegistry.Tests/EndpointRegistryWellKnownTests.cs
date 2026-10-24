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

using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// The well-known roots carry every Mandatory member and the published fixed NodeIds.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    public sealed class EndpointRegistryWellKnownTests
    {
        [Test]
        public void WellKnownRootsAreCompleteAndMatchTheirPublishedNodeIds()
        {
            var context = new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(SchemaRegistry.Namespaces.SchemaRegistry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaEndpointRegistry(context);
            var generic = (EndpointRegistryState)nodes.Single(node =>
                node.NodeId == ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, context.NamespaceUris));
            var media = (MediaEndpointRegistryState)nodes.Single(node =>
                node.NodeId == ExpandedNodeId.ToNodeId(ObjectIds.MediaEndpointRegistry, context.NamespaceUris));

            Assert.Multiple(() =>
            {
                Assert.That(generic.RegistryId, Is.Not.Null);
                Assert.That(generic.Snapshot, Is.Not.Null);
                Assert.That(generic.ProfileUris, Is.Not.Null);
                Assert.That(media.RegistryId, Is.Not.Null);
                Assert.That(media.Snapshot, Is.Not.Null);
                Assert.That(media.Endpoints, Is.Not.Null);
                Assert.That(Id(generic.TypedAccess), Is.EqualTo(Local(EndpointRegistryWellKnown.EndpointRegistryTypedAccess)));
                Assert.That(Id(generic.ProfileUris), Is.EqualTo(Local(EndpointRegistryWellKnown.EndpointRegistryProfileUris)));
                Assert.That(Id(generic.Endpoints), Is.EqualTo(Local(EndpointRegistryWellKnown.EndpointRegistryEndpoints)));
                Assert.That(Id(generic.MessageGroups),
                    Is.EqualTo(Local(EndpointRegistryWellKnown.EndpointRegistryMessageGroups)));
                Assert.That(Id(media.TypedAccess), Is.EqualTo(Local(EndpointRegistryWellKnown.MediaEndpointRegistryTypedAccess)));
                Assert.That(Id(media.ProfileUris), Is.EqualTo(Local(EndpointRegistryWellKnown.MediaEndpointRegistryProfileUris)));
                Assert.That(Id(media.Endpoints), Is.EqualTo(Local(EndpointRegistryWellKnown.MediaEndpointRegistryEndpoints)));
            });

            NodeId Id(NodeState? node)
            {
                return node?.NodeId ?? NodeId.Null;
            }

            NodeId Local(ExpandedNodeId id)
            {
                return ExpandedNodeId.ToNodeId(id, context.NamespaceUris);
            }
        }
    }
}
