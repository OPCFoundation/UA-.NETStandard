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
    /// What the AddressSpace declaration capability answers for the questions
    /// it is not asked in the ordinary case: a type named by nothing, a Node
    /// that is not a type, a hierarchy deeper than the walk will follow, and a
    /// declaration reached by a ReferenceType the Server does not name.
    /// </summary>
    /// <remarks>
    /// Every one of these has to answer "not this" rather than "nothing is
    /// declared". An empty, complete declaration set is the answer that cannot
    /// be told apart from a type that really declares nothing, and Section
    /// 6.8's closed-content rule then passes on the strength of nothing having
    /// been consulted.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    public sealed class AddressSpaceWotDeclarationBoundaryTests
    {
        private const string CompanionNamespace = "http://example.com/demo/pump";
        private const string TankTypeId = "nsu=http://example.com/demo/pump;i=1042";

        /// <summary>
        /// A type named by nothing, or by something that is not a NodeId this
        /// Server can resolve, is not a type this capability holds. It answers
        /// nothing, which is different from answering an empty declaration set.
        /// </summary>
        [TestCase("", TestName = "NoName")]
        [TestCase("not a node id", TestName = "NotANodeId")]
        [TestCase("nsu=http://example.com/not-loaded;i=1", TestName = "NamespaceNotLoaded")]
        public async Task ATypeNamedByNothingResolvableIsNotHeldAsync(string typeNodeId)
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver();

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(typeNodeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.That(set, Is.Null);
        }

        /// <summary>
        /// Instance declarations belong to a type, so a Node that is not one
        /// declares nothing. Answering with the Object's own children would
        /// report an instance's members as the declarations every instance of
        /// something has to carry.
        /// </summary>
        [Test]
        public async Task ANodeThatIsNotATypeDeclaresNothingAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                nodeClass: NodeClass.Object);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.That(set, Is.Null);
        }

        /// <summary>
        /// A VariableType declares instance declarations just as an ObjectType
        /// does, so both are answered for.
        /// </summary>
        [Test]
        public async Task AVariableTypeDeclaresJustAsAnObjectTypeDoesAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                nodeClass: NodeClass.VariableType);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.Declarations, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// The NodeClass of a declaration decides what a member of that name
        /// may be: an Object child declares an Object, a Variable child a
        /// Variable and a Method child a Method.
        /// </summary>
        [TestCase(NodeClass.Object, WotDeclarationKind.Object)]
        [TestCase(NodeClass.Variable, WotDeclarationKind.Variable)]
        [TestCase(NodeClass.Method, WotDeclarationKind.Method)]
        public async Task AChildDeclaresWhatItsNodeClassMakesItAsync(
            NodeClass childClass, WotDeclarationKind kind)
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(childClass: childClass);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.Declarations, Has.Count.EqualTo(1));
                Assert.That(Only(set).Kind, Is.EqualTo(kind));
            });
        }

        /// <summary>
        /// A declaration carries the name of the ReferenceType it is reached
        /// through, so a populated member is reached the same way. A Server
        /// that does not name the ReferenceType leaves the merge with nothing
        /// to write, and <c>HasComponent</c> - the ReferenceType a component is
        /// reached by by default - is what it falls back to; a name that is
        /// present but empty is no name either.
        /// </summary>
        [TestCase(ReferenceNaming.Named, "HasProperty")]
        [TestCase(ReferenceNaming.EmptyName, "HasComponent")]
        [TestCase(ReferenceNaming.Unnamed, "HasComponent")]
        public async Task ADeclarationIsReachedByTheNameTheServerGivesItAsync(
            ReferenceNaming naming, string expected)
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(naming: naming);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(Only(set!).ReferenceTypeName, Is.EqualTo(expected));
            });
        }

        /// <summary>
        /// The upward walk is bounded. A hierarchy deeper than the bound stops
        /// with the declarations it did read and says why, because a caller
        /// that read a truncated closure as whole would call a declared member
        /// undeclared.
        /// </summary>
        [Test]
        public async Task AHierarchyDeeperThanTheBoundStopsAndSaysSoAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                supertypeDepth: WotTypeDeclarations.MaxSupertypeDepth + 2);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("exceeded the maximum"));
                Assert.That(
                    set.Supertypes,
                    Has.Count.EqualTo(WotTypeDeclarations.MaxSupertypeDepth));
            });
        }

        /// <summary>
        /// How the Server names the ReferenceType a declaration is reached by.
        /// </summary>
        public enum ReferenceNaming
        {
            /// <summary>The type table holds no name for it.</summary>
            Unnamed,

            /// <summary>The type table holds an empty name for it.</summary>
            EmptyName,

            /// <summary>The type table names it.</summary>
            Named
        }

        private static WotTypeDeclaration Only(WotTypeDeclarationSet set)
        {
            foreach (WotTypeDeclaration declaration in set.Declarations)
            {
                return declaration;
            }
            throw new InvalidOperationException("the set is empty");
        }

        /// <summary>
        /// Builds a resolver over a Server holding one companion type whose
        /// single child, supertype chain and ReferenceType naming the caller
        /// chooses.
        /// </summary>
        private static AddressSpaceWotNodeResolver CreateResolver(
            NodeClass nodeClass = NodeClass.ObjectType,
            NodeClass childClass = NodeClass.Variable,
            ReferenceNaming naming = ReferenceNaming.Unnamed,
            int supertypeDepth = 0)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(CompanionNamespace);
            ushort index = (ushort)namespaceUris.GetIndex(CompanionNamespace);
            var tankTypeId = new NodeId(1042u, index);
            var speedId = new NodeId(2042u, index);

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ReferenceTypeIds.References, NodeId.Null);
            if (naming == ReferenceNaming.Named)
            {
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasProperty,
                    Opc.Ua.ReferenceTypeIds.References,
                    new QualifiedName("HasProperty"));
            }
            else if (naming == ReferenceNaming.EmptyName)
            {
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasProperty,
                    Opc.Ua.ReferenceTypeIds.References,
                    new QualifiedName(string.Empty, index));
            }

            NodeId root = nodeClass == NodeClass.VariableType
                ? Opc.Ua.VariableTypeIds.BaseVariableType
                : Opc.Ua.ObjectTypeIds.BaseObjectType;
            if (supertypeDepth == 0)
            {
                typeTable.AddSubtype(tankTypeId, root);
            }
            else
            {
                NodeId child = tankTypeId;
                for (int level = 0; level < supertypeDepth; level++)
                {
                    var parent = new NodeId((uint)(3000 + level), index);
                    typeTable.AddSubtype(parent, root);
                    typeTable.AddSubtype(child, parent);
                    child = parent;
                }
            }

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
                    BrowseResultMask __,
                    CancellationToken ___) =>
                    new ValueTask<NodeMetadata>(
                        new NodeMetadata(handle, (NodeId)handle)
                        {
                            NodeClass = nodeClass,
                            BrowseName = new QualifiedName("TankType", index)
                        }));

            var master = new Mock<IMasterNodeManager>();
            master
                .Setup(m => m.GetManagerHandleAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken _) =>
                    new ValueTask<(object?, IAsyncNodeManager?)>(
                        (nodeId, nodeManager.Object)));
            master
                .Setup(m => m.BrowseAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    OperationContext _,
                    ViewDescription __,
                    uint ___,
                    ArrayOf<BrowseDescription> nodesToBrowse,
                    CancellationToken ____) =>
                    Browse(nodesToBrowse, speedId, index, childClass));
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
                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadAttribute(nodesToRead[ii], childClass);
                    }
                    return new ValueTask<(ArrayOf<DataValue>, ArrayOf<DiagnosticInfo>)>((
                        new ArrayOf<DataValue>(values), ArrayOf<DiagnosticInfo>.Empty));
                });
            server.Setup(s => s.NodeManager).Returns(master.Object);

            return new AddressSpaceWotNodeResolver(server.Object);
        }

        private static ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)> Browse(
            ArrayOf<BrowseDescription> nodesToBrowse,
            NodeId speedId,
            ushort index,
            NodeClass childClass)
        {
            bool wantsModellingRule = nodesToBrowse.Count != 0 &&
                nodesToBrowse[0].ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasModellingRule;
            if (wantsModellingRule)
            {
                return Result(new BrowseResult
                {
                    References = new ArrayOf<ReferenceDescription>(
                        new ReferenceDescription[]
                        {
                            new()
                            {
                                NodeId = Opc.Ua.ObjectIds.ModellingRule_Mandatory,
                                NodeClass = NodeClass.Object,
                                BrowseName = new QualifiedName("Mandatory"),
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasModellingRule,
                                IsForward = true
                            }
                        })
                });
            }

            // Only the type itself declares a child; a supertype the walk
            // reaches declares none, so a deep hierarchy stays readable.
            bool isRoot = nodesToBrowse.Count != 0 &&
                nodesToBrowse[0].NodeId == new NodeId(1042u, index);
            if (!isRoot)
            {
                return Result(new BrowseResult());
            }
            return Result(new BrowseResult
            {
                References = new ArrayOf<ReferenceDescription>(
                    new ReferenceDescription[]
                    {
                        new()
                        {
                            NodeId = speedId,
                            NodeClass = childClass,
                            BrowseName = new QualifiedName("Speed", index),
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                            TypeDefinition = Opc.Ua.VariableTypeIds.PropertyType,
                            IsForward = true
                        }
                    })
            });
        }

        private static ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)> Result(
            BrowseResult result)
        {
            return new ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)>((
                new ArrayOf<BrowseResult>(new[] { result }), ArrayOf<DiagnosticInfo>.Empty));
        }

        private static DataValue ReadAttribute(ReadValueId nodeToRead, NodeClass childClass)
        {
            return nodeToRead.AttributeId switch
            {
                Opc.Ua.Attributes.DataType => new DataValue(new Variant(Opc.Ua.DataTypeIds.Double)),
                Opc.Ua.Attributes.ValueRank => new DataValue(new Variant(ValueRanks.Scalar)),
                Opc.Ua.Attributes.ArrayDimensions => new DataValue(new Variant(ArrayOf<uint>.Empty)),
                Opc.Ua.Attributes.NodeClass => new DataValue(new Variant((int)childClass)),
                _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
            };
        }
    }
}
