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
    /// A Read answers per value, so a Server that will not state an Attribute
    /// says so in that value's StatusCode rather than by failing the call.
    /// </summary>
    /// <remarks>
    /// The value handed back with a Bad status is the default of its type - a
    /// null NodeId, a zero ValueRank - and nothing distinguishes it from an
    /// Attribute the Node really carries with that value. Reading it as an
    /// answer produces a declaration that states no DataType and the scalar
    /// rank, which the merge then writes onto the member: the member's own
    /// DataType and ValueRank are replaced by values the Server never gave, and
    /// a member that states a rank is reported as contradicting a declaration
    /// that was never read.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    public sealed class AddressSpaceWotDeclarationStatusTests
    {
        private const string CompanionNamespace = "http://example.com/demo/pump";
        private const string TankTypeId = "nsu=http://example.com/demo/pump;i=1042";

        /// <summary>
        /// A declaration whose Attributes the Server answers with a Bad status
        /// is not reported as a declaration, and the closure says why. The
        /// declaration the Server did answer for is still reported, because it
        /// was read.
        /// </summary>
        [TestCase(nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase(nameof(StatusCodes.BadAttributeIdInvalid))]
        public async Task ABadAttributeStatusLeavesThatDeclarationUnreadAsync(string status)
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(StatusOf(status));

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(Names(set!), Is.EqualTo(s_speedOnly).AsCollection);
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set!.Detail, Does.Contain("Serial"));
                Assert.That(set!.Detail, Does.Contain("DataType"));
                Assert.That(set!.Detail, Does.Contain(status));
            });
        }

        /// <summary>
        /// A Server that answers every Attribute reports a whole closure, so
        /// the incomplete answer above is a distinction the code draws rather
        /// than one it always reports.
        /// </summary>
        [Test]
        public async Task AServerThatStatesEveryAttributeReportsAWholeClosureAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(badStatus: null);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(Names(set!), Is.EqualTo(s_serialThenSpeed).AsCollection);
                Assert.That(set!.IsComplete, Is.True);
                Assert.That(set!.Detail, Is.Null);
            });
        }

        /// <summary>
        /// ArrayDimensions is optional. A Server that answers
        /// <c>BadAttributeIdInvalid</c> for it has stated that the Attribute is
        /// absent, not that the required declaration Attributes were unread.
        /// </summary>
        [Test]
        public async Task AnAbsentOptionalArrayDimensionsKeepsTheDeclarationAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                StatusCodes.BadAttributeIdInvalid,
                badAttributeId: Opc.Ua.Attributes.ArrayDimensions);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.That(set, Is.Not.Null);
            WotTypeDeclaration serial = set!.Declarations.ToArray()!
                .Single(d => string.Equals(d.BrowseName, "Serial", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(set.IsComplete, Is.True);
                Assert.That(Names(set), Is.EqualTo(s_serialThenSpeed).AsCollection);
                Assert.That(serial.ArrayDimensions, Is.Empty);
            });
        }

        /// <summary>
        /// A refusal to read the optional Attribute is still an incomplete
        /// answer. Only <c>BadAttributeIdInvalid</c> means the Attribute is
        /// conformantly absent.
        /// </summary>
        [Test]
        public async Task ARefusedOptionalArrayDimensionsMakesTheClosureIncompleteAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                StatusCodes.BadUserAccessDenied,
                badAttributeId: Opc.Ua.Attributes.ArrayDimensions);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(Names(set), Is.EqualTo(s_speedOnly).AsCollection);
                Assert.That(set.Detail, Does.Contain("ArrayDimensions"));
                Assert.That(set.Detail, Does.Contain(nameof(StatusCodes.BadUserAccessDenied)));
            });
        }

        /// <summary>
        /// The declaration that was read still merges, and the one that was not
        /// leaves the member exactly as the document wrote it. Adopting the
        /// unread declaration would replace the member's own DataType and
        /// ValueRank with the values a refused read leaves behind, and would
        /// report the member as contradicting a rank the type never stated.
        /// </summary>
        [Test]
        public async Task AKnownDeclarationMergesAndAnUnreadOneChangesNothingAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                StatusCodes.BadUserAccessDenied);
            using WotDocument document = WotDocument.Parse(InstanceJson(closed: false));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            UAVariable serial = Variable(result, "1:Serial");

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                            d.Message.Contains("Speed", StringComparison.Ordinal)),
                    Is.True,
                    "The declaration the Server did state is still populated.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationMismatch),
                    Is.False,
                    "Nothing established a DataType or ValueRank for 'Serial' to contradict.");
                Assert.That(
                    serial.DataType,
                    Is.EqualTo("i=12"),
                    "The member keeps the DataType it stated.");
                Assert.That(
                    serial.ValueRank,
                    Is.EqualTo(1),
                    "The member keeps the ValueRank it stated.");
            });
        }

        /// <summary>
        /// A document that closes its content against a type one of whose
        /// declarations could not be read is refused: the closed-content rule
        /// is decidable only against a whole closure, and a member the unread
        /// part declares cannot be told from one the type never declared.
        /// </summary>
        [Test]
        public async Task AClosedDocumentStaysUnevaluableAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                StatusCodes.BadAttributeIdInvalid);
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
                    "Nothing established that the type does not declare 'Serial'.");
            });
        }

        /// <summary>
        /// An open document is not refused, but the gap is still reported: the
        /// member that could not be checked against a declaration is exactly
        /// the one a duplicate sibling would appear under.
        /// </summary>
        [Test]
        public async Task AnOpenDocumentIsWarnedAboutAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                StatusCodes.BadAttributeIdInvalid);
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
        /// A Method declares no DataType, no ValueRank and no ArrayDimensions,
        /// so a Server answering <c>BadAttributeIdInvalid</c> for them is
        /// answering correctly. Asking for them at all is what would turn every
        /// declared Method into an unreadable one.
        /// </summary>
        [Test]
        public async Task AMethodIsNotAskedForAttributesItCannotHaveAsync()
        {
            AddressSpaceWotNodeResolver resolver = CreateResolver(
                badStatus: null, methodChild: true);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.IsComplete, Is.True);
                Assert.That(
                    set.Declarations.ToArray()!
                        .Single(d => string.Equals(
                            d.BrowseName, "Serial", StringComparison.Ordinal))
                        .Kind,
                    Is.EqualTo(WotDeclarationKind.Method));
            });
        }

        private static readonly string[] s_speedOnly = ["Speed"];

        private static readonly string[] s_serialThenSpeed = ["Serial", "Speed"];

        private static StatusCode StatusOf(string name)
        {
            return name switch
            {
                nameof(StatusCodes.BadUserAccessDenied) => StatusCodes.BadUserAccessDenied,
                nameof(StatusCodes.BadAttributeIdInvalid) => StatusCodes.BadAttributeIdInvalid,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        private static string[] Names(WotTypeDeclarationSet set)
        {
            return [.. set.Declarations.ToArray()!.Select(d => d.BrowseName)];
        }

        private static UAVariable Variable(
            WotConversionResult<UANodeSet> result, string browseName)
        {
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, browseName, StringComparison.Ordinal));
        }

        private static byte[] InstanceJson(bool closed)
        {
            var builder = new StringBuilder();
            builder
                .Append("{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\",")
                .Append("\"ua\":\"http://opcfoundation.org/UA/\"}],")
                .Append("\"@type\":\"uav:object\",")
                .Append("\"id\":\"").Append(CompanionNamespace).Append("\",")
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
            builder.Append(
                "\"properties\":{\"Speed\":{\"type\":\"number\"}," +
                "\"Serial\":{\"type\":\"string\",\"uav:mapToType\":\"i=12\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":[0]}}}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>
        /// Builds a resolver over a Server whose <c>TankType</c> declares
        /// <c>Speed</c> and <c>Serial</c>, and which answers the Attribute read
        /// of <c>Serial</c> with the given Bad status.
        /// </summary>
        private static AddressSpaceWotNodeResolver CreateResolver(
            StatusCode? badStatus,
            bool methodChild = false,
            uint badAttributeId = Opc.Ua.Attributes.DataType)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(CompanionNamespace);
            ushort index = (ushort)namespaceUris.GetIndex(CompanionNamespace);
            var tankTypeId = new NodeId(1042u, index);
            var speedId = new NodeId(2042u, index);
            var serialId = new NodeId(2043u, index);

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ReferenceTypeIds.References, NodeId.Null);
            typeTable.AddSubtype(tankTypeId, Opc.Ua.ObjectTypeIds.BaseObjectType);

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
                    Browse(nodesToBrowse, tankTypeId, speedId, serialId, index, methodChild));
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
                        values[ii] =
                            badStatus is { } bad &&
                            nodesToRead[ii].NodeId == serialId &&
                            nodesToRead[ii].AttributeId == badAttributeId
                                ? DataValue.FromStatusCode(bad)
                                : ReadAttribute(nodesToRead[ii]);
                    }
                    return new ValueTask<(ArrayOf<DataValue>, ArrayOf<DiagnosticInfo>)>((
                        new ArrayOf<DataValue>(values), ArrayOf<DiagnosticInfo>.Empty));
                });
            server.Setup(s => s.NodeManager).Returns(master.Object);

            return new AddressSpaceWotNodeResolver(server.Object);
        }

        private static ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)> Browse(
            ArrayOf<BrowseDescription> nodesToBrowse,
            NodeId tankTypeId,
            NodeId speedId,
            NodeId serialId,
            ushort index,
            bool methodChild)
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
            if (nodesToBrowse.Count == 0 || nodesToBrowse[0].NodeId != tankTypeId)
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
                            NodeClass = NodeClass.Variable,
                            BrowseName = new QualifiedName("Speed", index),
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                            TypeDefinition = Opc.Ua.VariableTypeIds.PropertyType,
                            IsForward = true
                        },
                        new()
                        {
                            NodeId = serialId,
                            NodeClass = methodChild ? NodeClass.Method : NodeClass.Variable,
                            BrowseName = new QualifiedName("Serial", index),
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                            TypeDefinition = methodChild
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
                Opc.Ua.Attributes.ValueRank => new DataValue(new Variant(ValueRanks.Scalar)),
                Opc.Ua.Attributes.ArrayDimensions => new DataValue(
                    new Variant(ArrayOf<uint>.Empty)),
                Opc.Ua.Attributes.NodeClass => new DataValue(
                    new Variant((int)NodeClass.Variable)),
                _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
            };
        }
    }
}
