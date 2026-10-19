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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Tests client boundaries and negative wire responses independently of the local server.
    /// </summary>
    [TestFixture]
    public sealed class CraneClientTests
    {
        /// <summary>
        /// Continuation pages and vendor types are resolved without duplicates or hard-coded namespaces.
        /// </summary>
        [Test]
        public async Task DiscoveryFollowsContinuationsAndVendorSubtypes()
        {
            var harness = new Harness();
            ByteString continuation = ByteString.From(new byte[] { 1 });
            ReferenceDescription first = harness.Reference("First", harness.StandardType);
            ReferenceDescription second = harness.Reference("Second", harness.VendorType);
            harness.Browse(new BrowseResult { References = [first], ContinuationPoint = continuation });
            harness.Session.Setup(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { References = [second, first] }]
                }));
            ArrayOf<CraneEntry> result = await harness.Client().DiscoverAsync().ConfigureAwait(false);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[1].TypeDefinition, Is.EqualTo(harness.VendorType));
            harness.Session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == continuation),
                It.IsAny<CancellationToken>()), Times.Once);
            harness.Session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A result bound is an explicit error and the returned continuation is released.
        /// </summary>
        [Test]
        public async Task DiscoveryOverflowReleasesTheOutstandingContinuation()
        {
            var harness = new Harness();
            ByteString continuation = ByteString.From(new byte[] { 2 });
            harness.Browse(new BrowseResult
            {
                References =
                [
                    harness.Reference("One", harness.StandardType),
                    harness.Reference("Two", harness.StandardType)
                ],
                ContinuationPoint = continuation
            });
            harness.ReleaseContinuations();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client(new CranesHoistsClientOptions { MaximumNodes = 1 }).DiscoverAsync()
                    .ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            harness.VerifyRelease(continuation);
        }

        /// <summary>
        /// Access denial cannot be mistaken for an empty crane inventory.
        /// </summary>
        [Test]
        public async Task DiscoveryPreservesBadOperationStatuses()
        {
            var harness = new Harness();
            harness.Browse(new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied });
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client().DiscoverAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Cancellation releases the continuation using a cleanup token that is not already canceled.
        /// </summary>
        [Test]
        public async Task CancelledBrowseReleasesOnlyItsOwnedContinuation()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            ByteString continuation = ByteString.From(new byte[] { 3 });
            harness.Browse(new BrowseResult { References = [], ContinuationPoint = continuation });
            harness.Session.Setup(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return new ValueTask<BrowseNextResponse>(Task.FromCanceled<BrowseNextResponse>(cancellation.Token));
                });
            harness.ReleaseContinuations();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await harness.Client().DiscoverAsync(cancellation.Token).ConfigureAwait(false));
            harness.VerifyRelease(continuation);
        }

        /// <summary>
        /// Cyclic/empty continuation pages cannot make discovery unbounded.
        /// </summary>
        [Test]
        public async Task EmptyContinuationPagesRespectThePageBound()
        {
            var harness = new Harness();
            ByteString continuation = ByteString.From(new byte[] { 4 });
            var page = new BrowseResult { References = [], ContinuationPoint = continuation };
            harness.Browse(page);
            harness.Session.Setup(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), false,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse { Results = [page] }));
            harness.ReleaseContinuations();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client(new CranesHoistsClientOptions { MaximumBrowsePages = 1 }).DiscoverAsync()
                    .ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            harness.VerifyRelease(continuation);
        }

        /// <summary>
        /// A present bad-quality variable differs from a genuinely absent optional member.
        /// </summary>
        [Test]
        public async Task SnapshotDistinguishesOptionalAbsenceFromBadData()
        {
            var harness = new Harness();
            harness.SystemPaths();
            harness.Session.Setup(session => session.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                    CancellationToken _) => new ValueTask<ReadResponse>(new ReadResponse
                {
                    Results = reads.Count == 5
                    ?
                    [
                        new DataValue(Variant.From(new LocalizedText("Manufacturer"))),
                        new DataValue(Variant.From("Serial")),
                        new DataValue(Variant.From("urn:test:serial")),
                        new DataValue(Variant.From(new LocalizedText("Model"))),
                        new DataValue(Variant.From("Product"))
                    ]
                    :
                    [
                        new DataValue(Variant.From(1), StatusCodes.Good, CraneRuntimeTestData.SourceTime),
                        new DataValue((byte)AccessLevels.CurrentRead),
                        new DataValue(Variant.Null, StatusCodes.BadSensorFailure, CraneRuntimeTestData.SourceTime),
                        new DataValue((byte)AccessLevels.CurrentRead)
                    ]
                }));
            CraneElementSnapshot result = await harness.Client().ReadElementAsync(harness.Root).ConfigureAwait(false);
            CraneSignalValue on = result.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.SystemOn);
            CraneSignalValue external = result.Signals.ToArray()!.Single(
                value => value.Signal == CraneSignal.ExternalControlRequest);
            Assert.That(on.NodeId.IsNull, Is.False);
            Assert.That(on.Value.StatusCode, Is.EqualTo(StatusCodes.BadSensorFailure));
            Assert.That(on.Value.SourceTimestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
            Assert.That(external.NodeId.IsNull, Is.True);
            Assert.That(external.Value.IsNull, Is.True);
            Assert.That(external.Writable, Is.False);
        }

        /// <summary>
        /// A failed generated child lookup is checked strictly rather than treated as optional absence.
        /// </summary>
        [Test]
        public async Task RequiredProxyChildAccessFailureIsNotHidden()
        {
            var harness = new Harness();
            harness.Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = [new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied }]
                }));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client().ReadElementAsync(harness.Root).ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Incomplete and ambiguous browse targets cannot fabricate a resolved scalar node.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidPathTargetsAreRejected(bool partial)
        {
            var harness = new Harness();
            harness.SystemPaths(partial);
            if (!partial)
            {
                harness.Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<BrowsePath>>(paths => paths.Count > 1), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = []
                    }));
            }
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client().ReadElementAsync(harness.Root).ConfigureAwait(false)))!;
            Assert.That(error.StatusCode,
                Is.EqualTo(partial ? StatusCodes.BadNoMatch : StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// Missing namespace is legitimate lack of support, while invalid arguments fail before any service call.
        /// </summary>
        [Test]
        public async Task UnsupportedModelAndInvalidArgumentsDoNotPerformHiddenOperations()
        {
            var harness = new Harness(includeCrane: false);
            CranesHoistsClient client = harness.Client();
            Assert.That(client.SupportsModel, Is.False);
            Assert.That(await client.DiscoverAsync().ConfigureAwait(false), Is.Empty);
            harness.Session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Throws<ArgumentNullException>(() => client.System(NodeId.Null));
            Assert.Throws<ArgumentNullException>(() => client.Axis(NodeId.Null));
            Assert.Throws<ArgumentNullException>(() => new CranesHoistsClient(null!, harness.Telemetry));
            Assert.Throws<ArgumentNullException>(() => new CranesHoistsClient(harness.Session.Object, null!));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.ObserveValuesAsync([]).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await client.ObserveValuesAsync([harness.Root, harness.Root]).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.ConfigureLimitAsync(harness.Root, CraneSignal.OperationalMode, Variant.From(1))
                    .ConfigureAwait(false));
        }

        /// <summary>
        /// A strict, namespace-reordered session with explicitly scripted service responses.
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// Supplies only the session services each test explicitly exercises.
            /// </summary>
            internal Harness(bool includeCrane = true)
            {
                Message = ServiceMessageContext.Create(Telemetry);
                ushort ns = Message.NamespaceUris.GetIndexOrAppend("urn:crane:client-test:vendor");
                Root = new NodeId("System", ns);
                VendorType = new NodeId("VendorCraneType", ns);
                if (includeCrane)
                {
                    Message.NamespaceUris.GetIndexOrAppend(CranesHoistsModel.NamespaceUri);
                }
                Message.NamespaceUris.GetIndexOrAppend(Robotics.Namespaces.Robotics);
                Message.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery);
                Message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
                StandardType = includeCrane
                    ? NodeId.Create(ObjectTypes.CraneMotionDeviceSystemType, CranesHoistsModel.NamespaceUri,
                        Message.NamespaceUris)
                    : NodeId.Null;
                Session.SetupGet(session => session.NamespaceUris).Returns(Message.NamespaceUris);
                Session.SetupGet(session => session.MessageContext).Returns(Message);
                Session.SetupGet(session => session.NodeCache).Returns(Cache.Object);
                Cache.Setup(cache => cache.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId type, NodeId expected, CancellationToken _) =>
                        new ValueTask<bool>(expected == StandardType && (type == StandardType || type == VendorType)));
                var node = new Mock<INode>();
                node.SetupGet(value => value.NodeId).Returns(Root);
                node.SetupGet(value => value.BrowseName).Returns(new QualifiedName("System", ns));
                node.SetupGet(value => value.DisplayName).Returns(new LocalizedText("System"));
                node.SetupGet(value => value.TypeDefinitionId).Returns(StandardType);
                Cache.Setup(cache => cache.GetNodeAsync(Root, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<INode>(node.Object));
            }

            /// <summary>
            /// Gets test telemetry.
            /// </summary>
            internal ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

            /// <summary>
            /// Gets the real namespace and encodeable context used by generated proxies.
            /// </summary>
            internal ServiceMessageContext Message { get; }

            /// <summary>
            /// Gets the scripted session.
            /// </summary>
            internal Mock<ISession> Session { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the subtype-aware cache seam.
            /// </summary>
            internal Mock<INodeCache> Cache { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the application root identity.
            /// </summary>
            internal NodeId Root { get; }

            /// <summary>
            /// Gets the namespace-resolved standard type.
            /// </summary>
            internal NodeId StandardType { get; }

            /// <summary>
            /// Gets a real vendor subtype identity distinct from the standard type.
            /// </summary>
            internal NodeId VendorType { get; }

            /// <summary>
            /// Creates the actual client under test.
            /// </summary>
            internal CranesHoistsClient Client(CranesHoistsClientOptions? options = null)
            {
                return new CranesHoistsClient(Session.Object, Telemetry, options);
            }

            /// <summary>
            /// Describes a real discovered reference.
            /// </summary>
            internal ReferenceDescription Reference(string name, NodeId type)
            {
                return new ReferenceDescription
                {
                    NodeId = new NodeId(name, Root.NamespaceIndex),
                    BrowseName = new QualifiedName(name, Root.NamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    TypeDefinition = type
                };
            }

            /// <summary>
            /// Scripts an initial browse response.
            /// </summary>
            internal void Browse(BrowseResult result)
            {
                Session.Setup(session => session.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<BrowseResponse>(new BrowseResponse { Results = [result] }));
            }

            /// <summary>
            /// Scripts cleanup without borrowing another operation's continuation.
            /// </summary>
            internal void ReleaseContinuations()
            {
                Session.Setup(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<BrowseNextResponse>(
                        new BrowseNextResponse { Results = [new BrowseResult()] }));
            }

            /// <summary>
            /// Verifies exact owned-continuation cleanup with an independent live token.
            /// </summary>
            internal void VerifyRelease(ByteString continuation)
            {
                Session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                    It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == continuation),
                    It.Is<CancellationToken>(token => !token.IsCancellationRequested)), Times.Once);
            }

            /// <summary>
            /// Scripts generated ParameterSet resolution and explicit absent optional leaves.
            /// </summary>
            internal void SystemPaths(bool partial = false)
            {
                Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    {
                        var results = new List<BrowsePathResult>();
                        foreach (BrowsePath path in paths)
                        {
                            string? name = path.RelativePath!.Elements[^1].TargetName.Name;
                            bool absent = name is "ExternalControlRequest" or "SpeedLimitGlobalEnabled";
                            results.Add(absent
                                ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                                : new BrowsePathResult
                                {
                                    Targets =
                                    [
                                        new BrowsePathTarget
                                        {
                                            TargetId = new NodeId(name!, Root.NamespaceIndex),
                                            RemainingPathIndex = partial && paths.Count > 1 ? 0u : uint.MaxValue
                                        }
                                    ]
                                });
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse { Results = results.ToArrayOf() });
                    });
            }
        }
    }
}
