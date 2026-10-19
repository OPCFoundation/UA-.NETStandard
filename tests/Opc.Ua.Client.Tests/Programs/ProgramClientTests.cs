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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Programs;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Programs
{
    /// <summary>
    /// Focused Part 10 discovery, metadata and lifecycle wire-contract tests.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Parallelizable(ParallelScope.Fixtures)]
    public sealed class ProgramClientTests
    {
        /// <summary>
        /// Creates an isolated strict session and published-model fixtures.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(m_telemetry);
            m_context.NamespaceUris.GetIndexOrAppend("urn:unrelated");
            m_context.NamespaceUris.GetIndexOrAppend(Ua.Programs.Namespaces.OpcUaPrograms);
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(m_context);
            m_browse.Clear();
            m_values.Clear();
            m_browsed.Clear();
            m_session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        Assert.That(requests.Count, Is.EqualTo(1));
                        BrowseDescription request = requests[0];
                        m_browsed.Add(request.NodeId);
                        m_browse.TryGetValue((request.NodeId, request.ReferenceTypeId, request.BrowseDirection),
                            out ArrayOf<ReferenceDescription> references);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { References = references }]
                        });
                    });
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, requests, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ArrayOf.Wrapped(requests.ToArray()!
                                .Select(r => m_values[(r.NodeId, r.AttributeId)]).ToArray())
                        });
                    });
            m_client = new ProgramClient(m_session.Object, m_telemetry);
        }

        /// <summary>
        /// Factories and direct construction retain the caller's session.
        /// </summary>
        [Test]
        public void FactoryAndDirectConstructionUseCallerOwnedSession()
        {
            ProgramClient client = new ProgramClientFactory(m_telemetry).Create(m_session.Object);
            Assert.Multiple(() =>
            {
                Assert.That(client.Session, Is.SameAs(m_session.Object));
                Assert.That(client.GetProgram(s_invocation).Telemetry, Is.SameAs(m_telemetry));
                Assert.That(m_session.Object.GetProgramClient(m_telemetry).Session, Is.SameAs(m_session.Object));
                Assert.That(() => new ProgramClient(null!, m_telemetry), Throws.ArgumentNullException);
                Assert.That(() => new ProgramClient(m_session.Object, null!), Throws.ArgumentNullException);
                Assert.That(() => client.GetProgram(NodeId.Null), Throws.ArgumentException);
            });
        }

        /// <summary>
        /// Repeated fluent registration adds one injectable factory.
        /// </summary>
        [Test]
        public void FluentRegistrationIsIdempotent()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddProgramClient().AddProgramClient();
            Assert.That(services.Count(d => d.ServiceType == typeof(ProgramClientFactory)), Is.EqualTo(1));
            using ServiceProvider provider = services.BuildServiceProvider();
            ProgramClientFactory factory = provider.GetRequiredService<ProgramClientFactory>();
            Assert.That(factory.Create(m_session.Object).Session, Is.SameAs(m_session.Object));
        }

        /// <summary>
        /// Counter overflow remains bad quality, independently of optional-node absence.
        /// </summary>
        [Test]
        public async Task CapabilityReadsPreserveRecycleOverflowAndOptionalAbsenceAsync()
        {
            ConfigureCapabilities();
            DateTimeUtc timestamp = DateTimeUtc.Now;
            DataValue expected = new(Variant.From(int.MaxValue), StatusCodes.BadOutOfRange, timestamp);
            m_values[(s_recycle, Attributes.Value)] = expected;
            ProgramCapabilities result = await m_client.GetProgram(s_invocation)
                .ReadProgramCapabilitiesAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Deletable.DataValue.WrappedValue, Is.EqualTo(Variant.From(true)));
                Assert.That(result.RecycleCount.DataValue, Is.EqualTo(expected));
                Assert.That(result.RecycleCount.IsAvailable, Is.True);
                Assert.That(result.DiagnosticId.IsNull, Is.True);
                Assert.That(result.FinalResultId.IsNull, Is.True);
                Assert.That(result.Methods.IsEmpty, Is.True);
            });
        }

        /// <summary>
        /// A vendor-namespace lookalike does not satisfy a mandatory standard property.
        /// </summary>
        [Test]
        public async Task MissingMandatoryCapabilityIsNotASuccessfulDefault()
        {
            ConfigureCapabilities();
            m_browse[(s_invocation, ReferenceTypeIds.Aggregates, BrowseDirection.Forward)] =
            [
                Child(s_deletable, BrowseNames.Deletable, NodeClass.Variable, 2)
            ];
            await AssertStatus(
                async () => await m_client.ReadCapabilitiesAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadNotFound);
        }

        /// <summary>
        /// Good-quality malformed scalar values cannot become default capabilities.
        /// </summary>
        [Test]
        public async Task ReadableCapabilityWithWrongWireTypeIsRejected()
        {
            ConfigureCapabilities();
            m_values[(s_deletable, Attributes.Value)] = new DataValue(Variant.From("true"));
            await AssertStatus(
                async () => await m_client.ReadCapabilitiesAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Vendor methods retain their published argument metadata and user permissions.
        /// </summary>
        [Test]
        public async Task CapabilitiesExposeVendorMethodSignaturesAndPermissionsAsync()
        {
            ConfigureCapabilities();
            NodeId method = new("vendor-start", 2);
            NodeId input = new("vendor-input", 2);
            var key = (s_invocation, ReferenceTypeIds.Aggregates, BrowseDirection.Forward);
            m_browse[key] = m_browse[key].AddItem(Child(method, "StartBatch", NodeClass.Method, 2));
            SetChildren(method, Child(input, BrowseNames.InputArguments));
            ArrayOf<Argument> arguments =
            [
                new Argument { Name = "Batch", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            m_values[(input, Attributes.Value)] = new DataValue(Variant.FromStructure(arguments));
            m_values[(method, Attributes.Executable)] = new DataValue(Variant.From(true));
            m_values[(method, Attributes.UserExecutable)] = new DataValue(Variant.From(false));

            ProgramCapabilities result = await m_client.ReadCapabilitiesAsync(s_invocation).ConfigureAwait(false);
            Assert.That(result.Methods.Count, Is.EqualTo(1));
            ProgramMethod capability = result.Methods[0];
            Assert.Multiple(() =>
            {
                Assert.That(capability.BrowseName, Is.EqualTo(new QualifiedName("StartBatch", 2)));
                Assert.That(capability.UserExecutable.DataValue.WrappedValue, Is.EqualTo(Variant.From(false)));
                Assert.That(capability.InputArguments.DataValue.WrappedValue
                    .TryGetStructure(out ArrayOf<Argument> signature), Is.True);
                Assert.That(signature.Count, Is.EqualTo(1));
                Assert.That(signature[0].Name, Is.EqualTo("Batch"));
                Assert.That(capability.OutputArguments.IsAvailable, Is.False);
            });
        }

        /// <summary>
        /// Unsigned maxima and denied reads are retained independently of absent properties.
        /// </summary>
        [Test]
        public async Task TypeCapabilitiesKeepSdkUnlimitedValueAndDeniedQualityAsync()
        {
            NodeId type = new("custom-program-type", 2);
            NodeId creatable = new(Guid.NewGuid(), 2);
            NodeId maximum = new(ByteString.From([1, 2]), 2);
            SetChildren(type,
                Child(creatable, BrowseNames.Creatable),
                Child(maximum, BrowseNames.MaxInstanceCount));
            m_values[(creatable, Attributes.Value)] = DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);
            m_values[(maximum, Attributes.Value)] = new DataValue(Variant.From(uint.MaxValue));
            ProgramTypeCapabilities result = await m_client.ReadTypeCapabilitiesAsync(type).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Creatable.IsAvailable, Is.True);
                Assert.That(result.Creatable.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
                Assert.That(result.MaxInstanceCount.DataValue.WrappedValue, Is.EqualTo(Variant.From(uint.MaxValue)));
                Assert.That(result.InstanceCount.IsAvailable, Is.False);
                Assert.That(result.InstanceCount.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            });
        }

        /// <summary>
        /// Typed diagnostic decoding preserves the original read quality and timestamps.
        /// </summary>
        [Test]
        public async Task ModernDiagnosticsDecodeWithoutLosingTimestampsAsync()
        {
            var diagnostic = new ProgramDiagnostic2DataType { CreateClientName = "client", LastMethodCall = "Start" };
            NodeId diagnosticId = new("diagnostic", 2);
            SetChildren(s_invocation, Child(diagnosticId, BrowseNames.ProgramDiagnostic));
            DataValue value = new(Variant.FromStructure(diagnostic), StatusCodes.Uncertain, DateTimeUtc.Now);
            m_values[(diagnosticId, Attributes.Value)] = value;
            ProgramDiagnosticResult result = await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Diagnostic!.CreateClientName, Is.EqualTo("client"));
                Assert.That(result.Value.DataValue, Is.EqualTo(value));
            });
        }

        /// <summary>
        /// An absent optional diagnostic differs from an existing inaccessible diagnostic.
        /// </summary>
        [Test]
        public async Task MissingAndDeniedDiagnosticsAreDistinctAsync()
        {
            ProgramDiagnosticResult absent = await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false);
            NodeId diagnosticId = new("diagnostic", 2);
            SetChildren(s_invocation, Child(diagnosticId, BrowseNames.ProgramDiagnostic));
            m_values[(diagnosticId, Attributes.Value)] =
                DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);
            ProgramDiagnosticResult denied = await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(absent.Value.IsAvailable, Is.False);
                Assert.That(denied.Value.IsAvailable, Is.True);
                Assert.That(denied.Diagnostic, Is.Null);
                Assert.That(denied.Value.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            });
        }

        /// <summary>
        /// Older diagnostic layouts are explicitly rejected rather than padded with missing fields.
        /// </summary>
        [Test]
        public async Task LegacyDiagnosticsAreNotMisrepresentedAsModern()
        {
            NodeId diagnosticId = new("diagnostic", 2);
            SetChildren(s_invocation, Child(diagnosticId, BrowseNames.ProgramDiagnostic));
            m_values[(diagnosticId, Attributes.Value)] =
                new DataValue(Variant.FromStructure(new ProgramDiagnosticDataType()));
            await AssertStatus(
                async () => await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// A failed service envelope cannot be reported as a missing optional capability.
        /// </summary>
        [Test]
        public async Task BadReadServiceResultIsNotOptionalAbsence()
        {
            SetChildren(s_invocation, Child(new NodeId("diagnostic", 2), BrowseNames.ProgramDiagnostic));
            m_session.Setup(s => s.ReadAsync(It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionClosed }
                }));
            await AssertStatus(
                async () => await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadSessionClosed);
        }

        /// <summary>
        /// A response with missing results fails protocol validation.
        /// </summary>
        [Test]
        public async Task InvalidReadCardinalityIsRejected()
        {
            SetChildren(s_invocation, Child(new NodeId("diagnostic", 2), BrowseNames.ProgramDiagnostic));
            m_session.Setup(s => s.ReadAsync(It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse { ResponseHeader = new ResponseHeader() }));
            await AssertStatus(
                async () => await m_client.ReadDiagnosticsAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// Result traversal respects namespaces, cycles, value metadata and original timestamps.
        /// </summary>
        [Test]
        public async Task FinalResultsFollowPublishedHierarchyAndRetainAttributesAsync()
        {
            NodeId root = new("results", 2);
            NodeId nested = new("nested", 2);
            NodeId resultId = new(Guid.NewGuid(), 2);
            SetChildren(s_invocation, Child(root, BrowseNames.FinalResultData, NodeClass.Object));
            SetChildren(root, Child(nested, "Batch", NodeClass.Object, 2));
            SetChildren(nested, Child(resultId, "Count", NodeClass.Variable, 2));
            SetChildren(resultId, Child(root, "Cycle", NodeClass.Object, 2));
            DataValue value = new DataValue(Variant.From(42u)).WithServerTimestamp(DateTimeUtc.Now);
            m_values[(resultId, Attributes.Value)] = value;
            m_values[(resultId, Attributes.DataType)] = new DataValue(Variant.From(DataTypeIds.UInt32));
            m_values[(resultId, Attributes.ValueRank)] = new DataValue(Variant.From(ValueRanks.Scalar));
            ProgramFinalResults results = await m_client.ReadFinalResultsAsync(s_invocation).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(results.IsAvailable, Is.True);
                Assert.That(results.Values.Count, Is.EqualTo(1));
                Assert.That(results.Values[0].Value, Is.EqualTo(value));
                Assert.That(results.Values[0].BrowsePath,
                    Is.EqualTo(new[] { new QualifiedName("Batch", 2), new QualifiedName("Count", 2) }));
                Assert.That(results.Values[0].DataType.WrappedValue, Is.EqualTo(Variant.From(DataTypeIds.UInt32)));
            });
        }

        /// <summary>
        /// Invocation discovery follows type ancestry without escaping the requested hierarchy.
        /// </summary>
        [Test]
        public async Task DiscoveryHandlesSubtypesScopedRootAndHierarchyCyclesAsync()
        {
            NodeId scope = new("my-area", 2);
            NodeId type = new("vendor-program", 2);
            m_browse[(scope, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward)] =
                [Child(s_invocation, "invocation", NodeClass.Object, 2)];
            m_browse[(s_invocation, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward)] =
                [Child(scope, "cycle", NodeClass.Object, 2)];
            m_browse[(s_invocation, ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward)] =
                [Child(type, "VendorType", NodeClass.ObjectType, 2)];
            m_browse[(type, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse)] =
            [
                Child(ObjectTypeIds.ProgramStateMachineType, BrowseNames.ProgramStateMachineType, NodeClass.ObjectType)
            ];
            ArrayOf<NodeId> found = await m_client.DiscoverInvocationsAsync(scope).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(found, Is.EqualTo(new[] { s_invocation }));
                Assert.That(m_browsed, Does.Not.Contain(ObjectIds.ObjectsFolder));
                Assert.That(m_browsed.Count(id => id == scope), Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Type discovery consumes nonempty continuation points even after an empty first page.
        /// </summary>
        [Test]
        public async Task TypeDiscoveryConsumesAllContinuationPagesAsync()
        {
            NodeId subtype = new("type-on-second-page", 2);
            ByteString point = ByteString.From([2, 3, 4]);
            m_session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), 0,
                    It.Is<ArrayOf<BrowseDescription>>(r => r[0].NodeId == ObjectTypeIds.ProgramStateMachineType),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                }));
            m_session.Setup(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                    It.Is<ArrayOf<ByteString>>(p => p[0] == point), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { References = [Child(subtype, "Subtype", NodeClass.ObjectType, 2)] }]
                }));
            ArrayOf<ReferenceDescription> types = await m_client.DiscoverTypesAsync().ConfigureAwait(false);
            Assert.That(types.Count, Is.EqualTo(1));
            Assert.That(types[0].NodeId, Is.EqualTo(new ExpandedNodeId(subtype)));
            m_session.Verify(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Cancellation releases the outstanding server browse resource without retrying discovery.
        /// </summary>
        [Test]
        public async Task DiscoveryReleasesContinuationAfterCancellation()
        {
            ByteString point = ByteString.From([1]);
            m_session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), 0,
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                }));
            m_session.Setup(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());
            m_session.Setup(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                    It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                }));
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await m_client.DiscoverTypesAsync().ConfigureAwait(false));
            m_session.Verify(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(p => p[0] == point), CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// A vendor call preserves all method and argument results and is sent exactly once.
        /// </summary>
        [Test]
        public async Task VendorCallPreservesArgumentResultsWithoutRetryAsync()
        {
            CallMethodResult expected = new()
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                InputArgumentResults = [StatusCodes.BadOutOfRange],
                InputArgumentDiagnosticInfos = [new DiagnosticInfo { AdditionalInfo = "input-limit" }],
                OutputArguments = [Variant.From("vendor-output"), Variant.From(s_invocation)]
            };
            m_session.Setup(s => s.CallAsync(It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                        r[0].ObjectId == s_invocation && r[0].InputArguments.Count == 1),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [expected]
                }));
            CallMethodResult result = await m_client.CallWithResultAsync(
                s_invocation, new ExpandedNodeId(new NodeId("vendor-method", 2)), [Variant.From(42)])
                .ConfigureAwait(false);
            Assert.That(result, Is.SameAs(expected));
            m_session.Verify(s => s.CallAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// SDK creation delegates to the generated type method with remapped namespace identifiers.
        /// </summary>
        [Test]
        public async Task SdkCreateUsesGeneratedSignatureAndRemappedNamespaceAsync()
        {
            NodeId type = new("concrete-sdk-type", 2);
            NodeId parent = new("parent", 2);
            QualifiedName name = new("job", 2);
            NodeId method = ExpandedNodeId.ToNodeId(
                Ua.Programs.MethodIds.ManagedProgramType_Create, m_context.NamespaceUris);
            m_session.Setup(s => s.CallAsync(It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r => r[0].ObjectId == type && r[0].MethodId == method &&
                        r[0].InputArguments[0] == Variant.From(parent) &&
                        r[0].InputArguments[1] == Variant.From(name)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { OutputArguments = [Variant.From(s_invocation)] }]
                }));
            NodeId invocation = await m_client.CreateAsync(type, parent, name).ConfigureAwait(false);
            Assert.That(invocation, Is.EqualTo(s_invocation));
        }

        /// <summary>
        /// Invocation deletion is authorized by the server rather than a racy client-side read.
        /// </summary>
        [Test]
        public async Task DeletePropagatesServerPolicyRejection()
        {
            m_session.Setup(s => s.DeleteNodesAsync(It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<DeleteNodesItem>>(r => r[0].NodeId == s_invocation && r[0].DeleteTargetReferences),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeleteNodesResponse>(new DeleteNodesResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.BadUserAccessDenied]
                }));
            await AssertStatus(async () => await m_client.DeleteAsync(s_invocation).ConfigureAwait(false),
                StatusCodes.BadUserAccessDenied);
        }

        private void ConfigureCapabilities()
        {
            SetChildren(s_invocation, Child(s_deletable, BrowseNames.Deletable),
                Child(s_autoDelete, BrowseNames.AutoDelete), Child(s_recycle, BrowseNames.RecycleCount));
            m_values[(s_deletable, Attributes.Value)] = new DataValue(Variant.From(true));
            m_values[(s_autoDelete, Attributes.Value)] = new DataValue(Variant.From(false));
            m_values[(s_recycle, Attributes.Value)] = new DataValue(Variant.From(0));
        }

        private void SetChildren(NodeId id, params ReferenceDescription[] children)
        {
            m_browse[(id, ReferenceTypeIds.Aggregates, BrowseDirection.Forward)] = children;
        }

        private static ReferenceDescription Child(
            NodeId id, string name, NodeClass nodeClass = NodeClass.Variable, ushort ns = 0)
        {
            return new ReferenceDescription
            {
                NodeId = id,
                BrowseName = new QualifiedName(name, ns),
                NodeClass = nodeClass,
                IsForward = true,
                ReferenceTypeId = nodeClass == NodeClass.Variable
                    ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent
            };
        }

        private static async Task AssertStatus(Func<Task> action, StatusCode expected)
        {
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(action))!;
            Assert.That(exception.StatusCode, Is.EqualTo(expected));
        }

        private static readonly NodeId s_invocation = new("program", 2);
        private static readonly NodeId s_deletable = new("can-delete", 2);
        private static readonly NodeId s_autoDelete = new("auto-delete", 2);
        private static readonly NodeId s_recycle = new("recycle-count", 2);
        private readonly Dictionary<(NodeId, NodeId, BrowseDirection), ArrayOf<ReferenceDescription>> m_browse = [];
        private readonly Dictionary<(NodeId, uint), DataValue> m_values = [];
        private readonly List<NodeId> m_browsed = [];
        private ITelemetryContext m_telemetry = null!;
        private ServiceMessageContext m_context = null!;
        private Mock<ISessionClient> m_session = null!;
        private ProgramClient m_client = null!;
    }
}
