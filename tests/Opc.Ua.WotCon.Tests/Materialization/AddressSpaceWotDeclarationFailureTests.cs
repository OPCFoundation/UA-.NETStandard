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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// A browse or a read the Server would not answer says nothing about what a
    /// type declares.
    /// </summary>
    /// <remarks>
    /// Reporting a refusal as an empty, complete declaration set is the one
    /// answer that cannot be distinguished from the truth: every member of the
    /// document then looks undeclared, so each becomes a second Node beside the
    /// declaration it should have populated, and
    /// <c>uav:additionalProperties: false</c> passes on the strength of nothing
    /// having been consulted.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    public sealed class AddressSpaceWotDeclarationFailureTests
    {
        private const string CompanionNamespace = "http://example.com/demo/pump";
        private const string TankTypeId = "nsu=http://example.com/demo/pump;i=1042";

        /// <summary>
        /// One more child than a detail names individually, so the count of the
        /// rest is exercised.
        /// </summary>
        private const int MoreThanADetailNames = 6;

        /// <summary>
        /// A node manager that throws on browse is the case that used to be
        /// swallowed most completely: the walk produced no declaration and said
        /// the closure was whole.
        /// </summary>
        [Test]
        public async Task ABrowseThatThrowsMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(BrowseBehaviour.Throw);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Declarations.Count, Is.Zero);
                Assert.That(set.Detail, Does.Contain("Browsing the children"));
                Assert.That(set.Detail, Does.Contain("the node manager refused"));
            });
        }

        /// <summary>
        /// A bad <c>BrowseResult.StatusCode</c> is the Server saying it did not
        /// answer, which is not the same as answering "nothing".
        /// </summary>
        [Test]
        public async Task ABadBrowseStatusMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(BrowseBehaviour.BadStatus);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("BadUserAccessDenied"));
            });
        }

        /// <summary>
        /// A Server that answers no browse result at all is the same fault
        /// wearing a different shape.
        /// </summary>
        [Test]
        public async Task AnEmptyBrowseResponseMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(BrowseBehaviour.NoResults);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("no browse result"));
            });
        }

        /// <summary>
        /// The child browse answers, so the declaration exists - but the
        /// ModellingRule browse does not, and the ModellingRule is what says
        /// whether an instance has to carry the member at all. Defaulting it to
        /// "optional" would turn a mandatory declaration into an absent one.
        /// </summary>
        [Test]
        public async Task AModellingRuleThatCannotBeReadMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenOnly);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.Declarations.Count, Is.EqualTo(1));
                Assert.That(set.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("ModellingRule"));
                Assert.That(set.Detail, Does.Contain("Speed"));
            });
        }

        /// <summary>
        /// An Attribute read the Server refuses leaves the declaration's
        /// DataType, ValueRank and ArrayDimensions unknown, which a caller
        /// cannot tell from "the declaration states none".
        /// </summary>
        [Test]
        public async Task AnAttributeReadThatThrowsMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenAndRule, failAttributeRead: true);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("Reading the Attributes"));
            });
        }

        /// <summary>
        /// The whole point: a Server that answers everything reports a whole
        /// closure, so the incomplete answers above are a distinction the code
        /// draws rather than a fault it always reports.
        /// </summary>
        [Test]
        public async Task AServerThatAnswersReportsAWholeClosureAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenAndRule);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.True);
                Assert.That(set.Detail, Is.Null);
                Assert.That(set.Declarations.Count, Is.EqualTo(1));
                Assert.That(
                    set.Declarations[0].ModellingRule,
                    Is.EqualTo(WotModellingRule.Mandatory));
            });
        }

        /// <summary>
        /// The failure is visible where it matters: a document that closes its
        /// content against a type whose declarations could not be browsed is
        /// refused rather than admitted because nothing contradicted it.
        /// </summary>
        [Test]
        public async Task AClosedDocumentBoundToAnUnbrowsableTypeFailsVisiblyAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(BrowseBehaviour.Throw);
            using WotDocument document = WotDocument.Parse(InstanceJson(closed: true));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            WotDiagnostic? unavailable = result.Diagnostics.FirstOrDefault(
                d => d.Code == WotDiagnosticCode.DeclarationsUnavailable);
            Assert.Multiple(() =>
            {
                Assert.That(unavailable, Is.Not.Null);
                Assert.That(unavailable!.Severity, Is.EqualTo(WotDiagnosticSeverity.Error));
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UndeclaredMember),
                    Is.False,
                    "Nothing established that the type does not declare 'Speed'.");
            });
        }

        /// <summary>
        /// An open document is not refused, but the gap is still reported: the
        /// members that could not be checked against a declaration are exactly
        /// the ones a duplicate sibling would appear under.
        /// </summary>
        [Test]
        public async Task AnOpenDocumentBoundToAnUnbrowsableTypeIsWarnedAboutAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(BrowseBehaviour.Throw);
            using WotDocument document = WotDocument.Parse(InstanceJson(closed: false));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            WotDiagnostic? unavailable = result.Diagnostics.FirstOrDefault(
                d => d.Code == WotDiagnosticCode.DeclarationsUnavailable);
            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Not.Null);
                Assert.That(unavailable, Is.Not.Null);
                Assert.That(unavailable!.Severity, Is.EqualTo(WotDiagnosticSeverity.Warning));
            });
        }

        /// <summary>
        /// A Server that answers the browse merges the declaration, so the
        /// member becomes the declared Node rather than a sibling of it.
        /// </summary>
        [Test]
        public async Task AnAnsweringServerMergesRatherThanDuplicatingAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenAndRule);
            using WotDocument document = WotDocument.Parse(InstanceJson(closed: true));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationsUnavailable),
                    Is.False);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
                Assert.That(
                    result.Value!.Items!.OfType<UAVariable>().Count(
                        v => string.Equals(v.BrowseName, "1:Speed", StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });
        }

        /// <summary>
        /// A child whose BrowseName names a namespace index the Server's own
        /// table does not hold cannot be qualified, so it is a fault rather
        /// than a declaration quietly left out.
        /// </summary>
        [Test]
        public async Task AChildInAnUnknownNamespaceIsAFaultAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildInUnknownNamespace);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Declarations.Count, Is.Zero);
                Assert.That(set.Detail, Does.Contain("namespace table does not hold"));
            });
        }

        /// <summary>
        /// A child whose NodeId names a namespace this Server has not loaded
        /// cannot be translated to a local NodeId, so it cannot be read either.
        /// </summary>
        [Test]
        public async Task AChildWithAnUntranslatableNodeIdIsAFaultAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildWithForeignNodeId);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("cannot translate"));
            });
        }

        /// <summary>
        /// A Server that answers fewer Attributes than were asked for has not
        /// said what the declaration's DataType is, which a caller cannot tell
        /// from "it states none".
        /// </summary>
        [Test]
        public async Task AShortAttributeAnswerIsAFaultAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenAndRule, shortAttributeRead: true);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("of the 3 Attributes"));
            });
        }

        /// <summary>
        /// A declaration the Server answers for with no ModellingRule at all
        /// states none, which is a complete answer rather than a fault. A rule
        /// in another namespace, or one OPC 10000-3 does not define, is not a
        /// ModellingRule either.
        /// </summary>
        [Test]
        public async Task ADeclarationWithNoRecognizedModellingRuleStatesNoneAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.UnrecognizedModellingRule);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.True);
                Assert.That(set.Declarations.Count, Is.EqualTo(1));
                Assert.That(
                    set.Declarations[0].ModellingRule,
                    Is.EqualTo(WotModellingRule.None));
                Assert.That(
                    set.Declarations[0].TypeDefinitionNodeId,
                    Is.Empty,
                    "A child with no type definition states none rather than an empty one.");
            });
        }

        /// <summary>
        /// A Server failing every read would otherwise grow the detail without
        /// limit, so it names the first few and counts the rest.
        /// </summary>
        [Test]
        public async Task ManyFaultsAreBoundedAndCountedAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ManyChildrenNoRule);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("further failure(s) not listed."));
            });
        }

        /// <summary>
        /// A walk that stopped for a reason of its own <em>and</em> collected
        /// faults reports both, in that order, rather than one hiding the
        /// other.
        /// </summary>
        [Test]
        public async Task ACutShortWalkReportsItsReasonAndItsFaultsAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                BrowseBehaviour.ChildrenOnly, cyclicHierarchy: true);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("cycle rather than a hierarchy"));
                Assert.That(set.Detail, Does.Contain("ModellingRule"));
            });
        }

        /// <summary>
        /// What the mocked node manager does when a browse arrives.
        /// </summary>
        private enum BrowseBehaviour
        {
            /// <summary>
            /// Refuse every browse with an exception.
            /// </summary>
            Throw,

            /// <summary>
            /// Answer every browse with a bad status code.
            /// </summary>
            BadStatus,

            /// <summary>
            /// Answer with no browse result at all.
            /// </summary>
            NoResults,

            /// <summary>
            /// Answer the hierarchical browse and refuse the ModellingRule one.
            /// </summary>
            ChildrenOnly,

            /// <summary>
            /// Answer both browses.
            /// </summary>
            ChildrenAndRule,

            /// <summary>
            /// Answer with a child whose BrowseName names an unknown namespace
            /// index.
            /// </summary>
            ChildInUnknownNamespace,

            /// <summary>
            /// Answer with a child whose NodeId names a namespace this Server
            /// has not loaded.
            /// </summary>
            ChildWithForeignNodeId,

            /// <summary>
            /// Answer with a ModellingRule reference that is not one.
            /// </summary>
            UnrecognizedModellingRule,

            /// <summary>
            /// Answer with more children than a detail names individually, each
            /// of whose ModellingRule browses fails.
            /// </summary>
            ManyChildrenNoRule
        }

        private static byte[] InstanceJson(bool closed)
        {
            var builder = new StringBuilder();
            builder
                .Append("{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\",")
                .Append("\"ua\":\"http://opcfoundation.org/UA/\"}],")
                .Append("\"@type\":\"uav:object\",")
                .Append("\"id\":\"")
                .Append(CompanionNamespace)
                .Append("\",")
                .Append("\"title\":\"Tank\",")
                .Append("\"security\":\"nosec_sc\",")
                .Append("\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}},")
                .Append("\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"")
                .Append(TankTypeId)
                .Append("\"}],");
            if (closed)
            {
                builder.Append("\"uav:additionalProperties\":false,");
            }
            builder.Append("\"properties\":{\"Speed\":{\"type\":\"number\"}}}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>
        /// Builds a resolver over a Server holding one companion ObjectType,
        /// <c>TankType</c>, whose node manager answers browses and reads the way
        /// <paramref name="browse"/> says.
        /// </summary>
        private static AddressSpaceWotNodeResolver CreateResolver(
            BrowseBehaviour browse,
            bool failAttributeRead = false,
            bool shortAttributeRead = false,
            bool cyclicHierarchy = false)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(CompanionNamespace);
            ushort index = (ushort)namespaceUris.GetIndex(CompanionNamespace);
            var tankTypeId = new NodeId(1042u, index);
            var speedId = new NodeId(2042u, index);
            var baseTypeId = new NodeId(1000u, index);

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ReferenceTypeIds.References, NodeId.Null);
            if (cyclicHierarchy)
            {
                // A hierarchy that points back at itself: the walk has to stop
                // and say why rather than looping. Each type has to exist
                // before anything is re-parented onto it.
                typeTable.AddSubtype(baseTypeId, Opc.Ua.ObjectTypeIds.BaseObjectType);
                typeTable.AddSubtype(tankTypeId, baseTypeId);
                typeTable.AddSubtype(baseTypeId, tankTypeId);
            }
            else
            {
                typeTable.AddSubtype(tankTypeId, Opc.Ua.ObjectTypeIds.BaseObjectType);
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
                            NodeClass = NodeClass.ObjectType,
                            BrowseName = new QualifiedName("TankType", index)
                        }));

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
                    Browse(browse, nodesToBrowse, speedId, index));
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
                    if (failAttributeRead)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUserAccessDenied, "the node manager refused the read.");
                    }
                    int answered = shortAttributeRead
                        ? Math.Max(nodesToRead.Count - 1, 0)
                        : nodesToRead.Count;
                    var values = new DataValue[answered];
                    for (int ii = 0; ii < answered; ii++)
                    {
                        values[ii] = ReadAttribute(nodesToRead[ii]);
                    }
                    return new ValueTask<(ArrayOf<DataValue>, ArrayOf<DiagnosticInfo>)>((
                        new ArrayOf<DataValue>(values), ArrayOf<DiagnosticInfo>.Empty));
                });
            server.Setup(s => s.NodeManager).Returns(master.Object);

            return new AddressSpaceWotNodeResolver(server.Object);
        }

        private static ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)> Browse(
            BrowseBehaviour behaviour,
            ArrayOf<BrowseDescription> nodesToBrowse,
            NodeId speedId,
            ushort index)
        {
            if (behaviour == BrowseBehaviour.Throw)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "the node manager refused the browse.");
            }
            if (behaviour == BrowseBehaviour.NoResults)
            {
                return new ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)>((
                    ArrayOf<BrowseResult>.Empty, ArrayOf<DiagnosticInfo>.Empty));
            }
            if (behaviour == BrowseBehaviour.BadStatus)
            {
                return Result(new BrowseResult
                {
                    StatusCode = StatusCodes.BadUserAccessDenied
                });
            }

            bool wantsModellingRule = nodesToBrowse.Count != 0 &&
                nodesToBrowse[0].ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasModellingRule;
            if (wantsModellingRule)
            {
                if (behaviour is BrowseBehaviour.ChildrenOnly or
                    BrowseBehaviour.ManyChildrenNoRule)
                {
                    return Result(new BrowseResult
                    {
                        StatusCode = StatusCodes.BadUserAccessDenied
                    });
                }
                if (behaviour == BrowseBehaviour.UnrecognizedModellingRule)
                {
                    return Result(new BrowseResult
                    {
                        References = new ArrayOf<ReferenceDescription>(
                            new ReferenceDescription[]
                            {
                                new()
                                {
                                    NodeId = new NodeId(9999u, index),
                                    NodeClass = NodeClass.Object,
                                    BrowseName = new QualifiedName("NotARule", index),
                                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasModellingRule,
                                    IsForward = true
                                },
                                new()
                                {
                                    NodeId = new NodeId(4242u),
                                    NodeClass = NodeClass.Object,
                                    BrowseName = new QualifiedName("AlsoNotARule"),
                                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasModellingRule,
                                    IsForward = true
                                }
                            })
                    });
                }
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
            if (behaviour == BrowseBehaviour.ChildInUnknownNamespace)
            {
                return Result(new BrowseResult
                {
                    References = new ArrayOf<ReferenceDescription>(
                        new ReferenceDescription[]
                        {
                            new()
                            {
                                NodeId = speedId,
                                NodeClass = NodeClass.Variable,
                                BrowseName = new QualifiedName("Speed", 42),
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                                IsForward = true
                            },
                            new()
                            {
                                NodeId = speedId,
                                NodeClass = NodeClass.ReferenceType,
                                BrowseName = new QualifiedName("NotADeclaration", index),
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                                IsForward = true
                            }
                        })
                });
            }
            if (behaviour == BrowseBehaviour.ChildWithForeignNodeId)
            {
                return Result(new BrowseResult
                {
                    References = new ArrayOf<ReferenceDescription>(
                        new ReferenceDescription[]
                        {
                            new()
                            {
                                NodeId = new ExpandedNodeId(
                                    "Speed", 0, "http://example.com/not-loaded", 0),
                                NodeClass = NodeClass.Variable,
                                BrowseName = new QualifiedName("Speed", index),
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                                IsForward = true
                            }
                        })
                });
            }
            if (behaviour == BrowseBehaviour.ManyChildrenNoRule)
            {
                var many = new ReferenceDescription[MoreThanADetailNames];
                for (int ii = 0; ii < many.Length; ii++)
                {
                    many[ii] = new ReferenceDescription
                    {
                        NodeId = new NodeId((uint)(3000 + ii), index),
                        NodeClass = ii == 0 ? NodeClass.Method : NodeClass.Variable,
                        BrowseName = new QualifiedName(
                            "Member" + ii.ToString(CultureInfo.InvariantCulture), index),
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                        TypeDefinition = Opc.Ua.VariableTypeIds.PropertyType,
                        IsForward = true
                    };
                }
                return Result(new BrowseResult
                {
                    References = new ArrayOf<ReferenceDescription>(many)
                });
            }
            return Result(new BrowseResult
            {
                References = new ArrayOf<ReferenceDescription>(
                    new ReferenceDescription[]
                    {
                        new()
                        {
                            NodeId = speedId,
                            NodeClass = NodeClass.Variable,
                            BrowseName = new QualifiedName("Speed", index),
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                            TypeDefinition = behaviour ==
                                BrowseBehaviour.UnrecognizedModellingRule
                                    ? ExpandedNodeId.Null
                                    : Opc.Ua.VariableTypeIds.PropertyType,
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

        private static DataValue ReadAttribute(ReadValueId nodeToRead)
        {
            return nodeToRead.AttributeId switch
            {
                Opc.Ua.Attributes.DataType => new DataValue(
                    new Variant(Opc.Ua.DataTypeIds.Double)),
                Opc.Ua.Attributes.ValueRank => new DataValue(
                    new Variant(ValueRanks.Scalar)),
                Opc.Ua.Attributes.ArrayDimensions => new DataValue(
                    new Variant(ArrayOf<uint>.Empty)),
                Opc.Ua.Attributes.NodeClass => new DataValue(
                    new Variant((int)NodeClass.Variable)),
                _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
            };
        }
    }
}
