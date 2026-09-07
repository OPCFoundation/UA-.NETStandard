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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the loaded-AddressSpace half of the WoT Binding Section 5.1.5
    /// local context: the types a Server already holds, which is what a
    /// Section 5.2.1 binding to a companion model names.
    /// </summary>
    [TestFixture]
    public sealed class AddressSpaceWotNodeResolverTests
    {
        private const string CompanionNamespace = "http://example.com/demo/pump";
        private const string TankTypeId = "nsu=http://example.com/demo/pump;i=1042";
        private const string FlowsIntoId = "nsu=http://example.com/demo/pump;i=5001";

        /// <summary>
        /// Section 5.2.1 tells a binding from an annotation by namespace, and a
        /// namespace in the Server's table is one it has loaded.
        /// </summary>
        [Test]
        public async Task HoldsANamespaceTheServerHasLoadedAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            Assert.That(
                await resolver.HoldsNamespaceAsync(CompanionNamespace).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await resolver.HoldsNamespaceAsync("urn:never:loaded").ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// The definitive form of Section 5.2.1: an ExpandedNodeId naming a
        /// type the Server holds resolves to it.
        /// </summary>
        [Test]
        public async Task ResolvesACompanionTypeByNodeIdAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            WotResolvedNode? match = await resolver
                .ResolveByNodeIdAsync(TankTypeId).ConfigureAwait(false);

            Assert.That(match, Is.Not.Null);
            Assert.That(match!.Value.NodeClass, Is.EqualTo(WotExpectedNodeClass.ObjectType));
        }

        /// <summary>
        /// A NodeId the Server does not hold resolves to nothing, which
        /// Section 5.2.1 reports rather than guessing at.
        /// </summary>
        [Test]
        public async Task DoesNotResolveANodeIdTheServerDoesNotHoldAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            Assert.That(
                await resolver
                    .ResolveByNodeIdAsync("nsu=http://example.com/demo/pump;i=9999")
                    .ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// A malformed identifier is not a resolution failure to guess at
        /// either; it simply resolves to nothing.
        /// </summary>
        [Test]
        public async Task DoesNotResolveAMalformedNodeIdAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            Assert.That(
                await resolver.ResolveByNodeIdAsync("not a node id").ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// The readable form of Section 5.2.1: a NamespaceUri-qualified
        /// BrowseName resolves through the type hierarchy the Server holds.
        /// </summary>
        [Test]
        public async Task ResolvesACompanionTypeByBrowseNameAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    CompanionNamespace, "TankType", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].NodeId, Is.EqualTo(TankTypeId));
        }

        /// <summary>
        /// A caller requiring a VariableType is not satisfied by an ObjectType.
        /// Section 5.2.1 makes a resolved type of the wrong NodeClass an
        /// invalid document, so the resolver must not offer it.
        /// </summary>
        [Test]
        public async Task DoesNotOfferAnObjectTypeWhenAVariableTypeIsRequiredAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    CompanionNamespace, "TankType", WotExpectedNodeClass.VariableType)
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// A name the Server's type hierarchy does not carry resolves to
        /// nothing.
        /// </summary>
        [Test]
        public async Task DoesNotResolveAnUnknownBrowseNameAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    CompanionNamespace, "NeverDefinedType", WotExpectedNodeClass.Any)
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// Section 5.1.2 lets a link <c>rel</c> name a ReferenceType by its
        /// BrowseName, and a companion model's own ReferenceType is resolved by
        /// the same rules as a base-namespace one — the resolver holds no
        /// table of its own.
        /// </summary>
        [Test]
        public async Task ResolvesACompanionReferenceTypeByBrowseNameAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "FlowsInto")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(matches[0].NodeId, Is.EqualTo(FlowsIntoId));
                Assert.That(matches[0].Name, Is.EqualTo("FlowsInto"));
                Assert.That(matches[0].IsForward, Is.True);
            });
        }

        /// <summary>
        /// OPC 10000-3 gives a ReferenceType a second name, and a relation
        /// named by it reads the reference backwards.
        /// </summary>
        [Test]
        public async Task ResolvesACompanionReferenceTypeByInverseNameAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "FedFrom")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(matches[0].NodeId, Is.EqualTo(FlowsIntoId));
                Assert.That(matches[0].Name, Is.EqualTo("FedFrom"));
                Assert.That(matches[0].IsForward, Is.False);
            });
        }

        /// <summary>
        /// A symmetric ReferenceType has one name for both directions, so it is
        /// offered once and reads forward. Offering it twice would make every
        /// use of the name ambiguous and demand a <c>uav:refId</c> to settle
        /// what was never in doubt.
        /// </summary>
        [Test]
        public async Task OffersASymmetricReferenceTypeOnceAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "ConnectedTo")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].IsForward, Is.True);
        }

        /// <summary>
        /// The BrowseName of an ObjectType is not a relation, so it resolves to
        /// no ReferenceType at all — which is what lets the converter report it
        /// as the wrong NodeClass rather than as unresolvable.
        /// </summary>
        [Test]
        public async Task DoesNotOfferAnObjectTypeAsARelationAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "TankType")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// A name the Server holds in one namespace is not held in another: the
        /// prefix of a compact model name binds the namespace, and that is what
        /// tells two same-named ReferenceTypes apart.
        /// </summary>
        [Test]
        public async Task DoesNotResolveAKnownNameInAnotherNamespaceAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync("urn:never:loaded", "FlowsInto")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// A name no ReferenceType answers to resolves to nothing.
        /// </summary>
        [Test]
        public async Task DoesNotResolveAnUnknownRelationAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "NeverDefined")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// A Node the type hierarchy lists under References but whose
        /// Attributes say it is not a ReferenceType contributes no name: the
        /// NodeClass the Server reports is what settles it.
        /// </summary>
        [Test]
        public async Task IgnoresANodeThatIsNotAReferenceTypeAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(CompanionNamespace, "PretendReference")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// Builds a resolver over a Server holding one companion ObjectType,
        /// <c>TankType</c>, derived from BaseObjectType, and the companion
        /// ReferenceTypes <c>FlowsInto</c> (InverseName <c>FedFrom</c>) and the
        /// symmetric <c>ConnectedTo</c>, plus one Node the type hierarchy lists
        /// as a ReferenceType but whose Attributes say otherwise.
        /// </summary>
        private static AddressSpaceWotNodeResolver CreateResolver()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(CompanionNamespace);
            ushort index = (ushort)namespaceUris.GetIndex(CompanionNamespace);
            var tankTypeId = new NodeId(1042u, index);
            var flowsIntoId = new NodeId(5001u, index);
            var connectedToId = new NodeId(5002u, index);
            var pretendId = new NodeId(6001u, index);

            var typeTable = new TypeTable(namespaceUris);

            // The roots have to exist before anything derives from them.
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ReferenceTypeIds.References, NodeId.Null);
            typeTable.AddSubtype(tankTypeId, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(flowsIntoId, Opc.Ua.ReferenceTypeIds.References);
            typeTable.AddSubtype(connectedToId, Opc.Ua.ReferenceTypeIds.References);
            typeTable.AddSubtype(pretendId, Opc.Ua.ReferenceTypeIds.References);

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.TypeTree).Returns(typeTable);

            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .Setup(m => m.GetNodeMetadataAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    OperationContext _,
                    object handle,
                    BrowseResultMask _,
                    CancellationToken _) =>
                {
                    var metadata = new NodeMetadata(handle, (NodeId)handle)
                    {
                        NodeClass = NodeClass.ObjectType,
                        BrowseName = new QualifiedName("TankType", index)
                    };
                    return new ValueTask<NodeMetadata>(metadata);
                });

            var master = new Mock<IMasterNodeManager>();
            master
                .Setup(m => m.GetManagerHandleAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken _) =>
                    new ValueTask<(object?, IAsyncNodeManager?)>(
                        nodeId == tankTypeId
                            ? (nodeId, nodeManager.Object)
                            : ((object?)null, (IAsyncNodeManager?)null)));
            master
                .Setup(m => m.ReadAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    OperationContext _,
                    double __,
                    TimestampsToReturn ___,
                    ArrayOf<ReadValueId> nodesToRead,
                    CancellationToken ____) =>
                {
                    var values = new DataValue[nodesToRead.Count];
                    for (int ii = 0; ii < nodesToRead.Count; ii++)
                    {
                        values[ii] = ReadAttribute(nodesToRead[ii], index);
                    }
                    return new ValueTask<(
                        ArrayOf<DataValue>, ArrayOf<DiagnosticInfo>)>((
                            new ArrayOf<DataValue>(values),
                            ArrayOf<DiagnosticInfo>.Empty));
                });
            server.Setup(s => s.NodeManager).Returns(master.Object);

            return new AddressSpaceWotNodeResolver(server.Object);
        }

        /// <summary>
        /// Answers one Attribute read the way a Server holding the companion
        /// ReferenceTypes would.
        /// </summary>
        private static DataValue ReadAttribute(ReadValueId nodeToRead, ushort index)
        {
            uint identifier = nodeToRead.NodeId.TryGetValue(out uint value) ? value : 0;
            (NodeClass nodeClass, string browseName, string inverseName, bool symmetric) node =
                identifier switch
                {
                    5001u => (NodeClass.ReferenceType, "FlowsInto", "FedFrom", false),
                    5002u => (NodeClass.ReferenceType, "ConnectedTo", string.Empty, true),
                    6001u => (NodeClass.ObjectType, "PretendReference", string.Empty, false),
                    _ => (NodeClass.Unspecified, string.Empty, string.Empty, false)
                };

            if (node.nodeClass == NodeClass.Unspecified)
            {
                return DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
            }
            return nodeToRead.AttributeId switch
            {
                Opc.Ua.Attributes.NodeClass => new DataValue(new Variant((int)node.nodeClass)),
                Opc.Ua.Attributes.BrowseName => new DataValue(
                    new Variant(new QualifiedName(node.browseName, index))),
                Opc.Ua.Attributes.InverseName => node.inverseName.Length == 0
                    ? DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
                    : new DataValue(new Variant(new LocalizedText(node.inverseName))),
                Opc.Ua.Attributes.Symmetric => new DataValue(new Variant(node.symmetric)),
                _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
            };
        }
    }
}
