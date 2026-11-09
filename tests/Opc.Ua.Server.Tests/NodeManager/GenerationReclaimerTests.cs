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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Tests.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies independent retirement ownership, exact drains, and staged cleanup retry without a lifecycle writer.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class GenerationReclaimerTests
    {
        /// <summary>
        /// Consumer disposal uses the existing gate and remains observable and retryable at retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredConsumerDisposalUsesGenerationDrainAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var completedResource = new Mock<IDisposable>();
            var resource = new Mock<IDisposable>();
            int attempts = 0;
            var failure = new InvalidOperationException("Consumer disposal failed.");
            resource.Setup(value => value.Dispose()).Callback(() =>
            {
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                if (++attempts == 1 && failFirst)
                {
                    throw failure;
                }
            });

            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False,
                "An ordinary live consumer keeps its existing immediate disposal behavior.");
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            retired.Generation.Gate.Retire();
            try
            {
                Assert.That(retired.Generation.TryDeferDisposal(completedResource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                    "Repeated disposal must transfer ownership only once.");
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.InvalidOperationException);
                completedResource.Verify(value => value.Dispose(), Times.Never);
                resource.Verify(value => value.Dispose(), Times.Never);
            }
            finally
            {
                retired.Generation.Gate.Exit(admission);
            }

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            if (failFirst)
            {
                Exception cleanupFailure = await Assert.CatchAsync<Exception>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false));
                Assert.That(cleanupFailure, Is.SameAs(failure));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(retired), Is.True);
            }
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, failFirst ? failure : null, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False);
            completedResource.Verify(value => value.Dispose(), Times.Once);
            resource.Verify(value => value.Dispose(), Times.Exactly(failFirst ? 2 : 1));
            Assert.That(reclaimer.Contains(retired), Is.False);
        }

        /// <summary>
        /// Private identities retain their own view pins, provider cleanup, and deferred resources.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RetirementDrainsPrivateIdentityAndRetriesItsCleanupAsync(bool facade, bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            var rawManager = new Mock<IAsyncNodeManager>();
            manager.As<IAsyncDisposable>();
            Mock<IAsyncNodeManager> privateManager = facade ? manager : rawManager;
            var privateIdentity = new NodeManagerGeneration(
                new NodeManagerSlot(), 1, privateManager.Object, NodeManagerGenerationState.Prepared);
            var generation = new NodeManagerGeneration(
                new NodeManagerSlot(),
                1,
                manager.Object,
                NodeManagerGenerationState.Prepared,
                privateSourceGeneration: facade ? null : privateIdentity,
                privateGate: facade ? privateIdentity.Gate : null);
            if (facade)
            {
                generation.RetainPrivateFacade(privateIdentity);
            }
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            var sessionId = new NodeId("private-identity-session", 2);
            var session = new Mock<ISession>();
            var identity = new UserIdentity();
            var points = new SessionContinuationPoints(() => sessionId, 8, 8, null);
            session.SetupGet(value => value.Id).Returns(sessionId);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            session.SetupGet(value => value.ContinuationPoints).Returns(points);
            Mock.Get(context.Server.Object.SessionManager)
                .Setup(value => value.GetSessions()).Returns([session.Object]);
            generation.SessionBindings[sessionId] = new GenerationSessionBindingRecord
            {
                Binding = new GenerationReconciler.SessionBinding(session.Object, identity, generation.Reference)
            };
            privateIdentity.SessionBindings[sessionId] = new GenerationSessionBindingRecord
            {
                Binding = new GenerationReconciler.SessionBinding(session.Object, identity, privateIdentity.Reference)
            };
            var privateBrowse = new ContinuationPoint
            {
                Id = Guid.NewGuid(),
                Generation = privateIdentity.Reference
            };
            var unrelated = new NodeManagerGeneration(
                new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
            var unrelatedBrowse = new ContinuationPoint
            {
                Id = Guid.NewGuid(),
                Generation = unrelated.Reference
            };
            var privateHistory = new Mock<IHistoryContinuationPoint>();
            Guid privateHistoryId = Guid.NewGuid();
            privateHistory.SetupGet(value => value.Id).Returns(privateHistoryId);
            points.SaveBrowse(privateBrowse);
            points.SaveBrowse(unrelatedBrowse);
            points.SaveHistory(privateHistory.Object);
            var privateHistoryToken = new ByteString(privateHistoryId.ToByteArray());
            points.BindHistoryGeneration(privateHistoryToken, privateIdentity);
            var epoch = new AddressSpaceEpoch();
            privateIdentity.RegisterViewEpoch(epoch);
            Assert.That(epoch.TryPin(out int pin), Is.True);
            epoch.Close();
            generation.Gate.Activate();
            generation.Gate.Retire();

            var deferred = new Mock<IDisposable>();
            Assert.That(privateIdentity.TryDeferDisposal(deferred.Object), Is.True);
            int attempts = 0;
            var failure = new InvalidOperationException("Private source cleanup failed.");
            privateIdentity.RetainSourceRelease(Mock.Of<IMonitoredItem>(), 1, _ =>
            {
                if (++attempts == 1 && failFirst)
                {
                    throw failure;
                }
                return default;
            });
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                await reclaimer.ReclaimReadyAsync([retired]).AsTask().WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(reclaimer.Contains(retired), Is.True);
                Assert.That(attempts, Is.Zero);
                deferred.Verify(value => value.Dispose(), Times.Never);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    manager.Object, It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                epoch.Exit(pin);
            }

            if (failFirst)
            {
                Exception? cleanupFailure = await Assert.CatchAsync<Exception>(
                    async () => await reclaimer.ReclaimReadyAsync([retired]).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(cleanupFailure, Is.SameAs(failure));
                Assert.That(privateIdentity.HasPendingSourceReleases, Is.True);
                deferred.Verify(value => value.Dispose(), Times.Never);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    manager.Object, It.IsAny<CancellationToken>()), Times.Never);
            }
            await reclaimer.ReclaimReadyAsync([retired]).AsTask().WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, failFirst ? failure : null, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(failFirst ? 2 : 1));
            Assert.That(privateIdentity.HasPendingSourceReleases, Is.False);
            deferred.Verify(value => value.Dispose(), Times.Once);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Once);
            context.Host.Verify(value => value.ReleaseRetiringEventGeneration(privateIdentity), Times.AtLeastOnce);
            Assert.That(generation.SessionBindings, Is.Empty);
            Assert.That(privateIdentity.SessionBindings, Is.Empty);
            manager.Verify(value => value.SessionClosingAsync(
                It.IsAny<OperationContext>(), sessionId, false, It.IsAny<CancellationToken>()),
                Times.Exactly(facade ? 2 : 1));
            if (!facade)
            {
                rawManager.Verify(value => value.SessionClosingAsync(
                    It.IsAny<OperationContext>(), sessionId, false, It.IsAny<CancellationToken>()), Times.Once);
            }
            Assert.That(points.RestoreBrowse(new ByteString(privateBrowse.Id.ToByteArray())), Is.Null);
            Assert.That(points.RestoreHistory(privateHistoryToken), Is.Null);
            privateHistory.Verify(value => value.Dispose(), Times.Once);
            using ContinuationPoint? restored =
                points.RestoreBrowse(new ByteString(unrelatedBrowse.Id.ToByteArray()));
            Assert.That(restored, Is.SameAs(unrelatedBrowse));
        }

        /// <summary>
        /// A staged cleanup retry does not repeat the manager's successful disposal callback.
        /// </summary>
        [Test]
        public async Task RetirementRetriesStagedResourceAfterManagerDisposalWithoutRepeatingManagerDisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();

            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaceUris);
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            using var servingRegistry = new HistorianProviderRegistry(namespaceUris);
            context.Server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            context.Server.SetupGet(value => value.ServerUris).Returns(serverUris);
            context.Server.SetupGet(value => value.TypeTree).Returns(typeTree);
            context.Server.SetupGet(value => value.Factory).Returns(factory);
            context.Server.SetupGet(value => value.MessageContext).Returns(
                new ServiceMessageContext(context.Server.Object.Telemetry, factory)
                {
                    NamespaceUris = namespaceUris,
                    ServerUris = serverUris
                });
            context.Server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(context.Server.Object));
            context.Server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry).Returns(servingRegistry);

            int stagedCleanupAttempts = 0;
            var failure = new InvalidOperationException("Staged cleanup failed once.");
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.RegisterHistorianBuilder(It.IsAny<HistorianBuilder>()));
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()))
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref stagedCleanupAttempts) == 1)
                    {
                        throw failure;
                    }
                });

            var preparation = new ChangePreparer(context.Server.Object);
            await using ChangePreparer.StagingServer stagingServer = preparation.CreateServer();
            stagingServer.Attach(retired.NodeManager);
            Assert.That(ChangePreparer.GetServer(retired.NodeManager), Is.SameAs(stagingServer));
            _ = new HistorianBuilder(stagingServer)
                .UseProvider(Mock.Of<IHistorianProvider>())
                .RegisterAsDefault();

            Mock<IAsyncDisposable> managerDisposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            int managerDisposeCount = 0;
            managerDisposal.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                managerDisposeCount++;
                return default;
            });
            retired.Generation.Gate.Retire();

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Exception cleanupFailure = await Assert.CatchAsync<Exception>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("The first retirement cleanup attempt did not fail.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cleanupFailure, Is.TypeOf<AggregateException>());
                Assert.That(
                    ((AggregateException)cleanupFailure).Flatten().InnerExceptions,
                    Has.Member(failure));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(retired), Is.True);
                Assert.That(managerDisposeCount, Is.EqualTo(1));
                Assert.That(stagedCleanupAttempts, Is.EqualTo(1));
            }
            managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);

            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, cleanupFailure, timeout.Token)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reclaimer.Count, Is.Zero);
                Assert.That(managerDisposeCount, Is.EqualTo(1));
                Assert.That(stagedCleanupAttempts, Is.EqualTo(2));
            }
            managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);
            context.Server.As<IHistorianBuilderRegistry>().Verify(
                value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Retirement retains the manager until a file handle finishes cleanup after the public disposal deadline.
        /// </summary>
        [Test]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        [Category("FileSystem")]
        public async Task RetirementWaitsForFileHandleCleanupAfterBoundedDisposalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            using var harness = new FileOpenOwnershipRegressionTests.FileHarness();
            using var stream = new FileOpenOwnershipRegressionTests.SuspendedProtocolReadStream();
            harness.Manager.CloseTimeout = TimeSpan.Zero;
            harness.Provider.Setup(value => value.OpenReadAsync("file", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(stream));

            uint id = await FileReadRegressionTests.OpenAsync(harness.File, harness.Manager.SystemContext)
                .ConfigureAwait(false);
            FileHandle handles = harness.GetHandle();
            Task<ReadMethodStateResult> read = harness.File.Read!.OnCallAsync!(
                harness.Manager.SystemContext, harness.File.Read, harness.File.NodeId, id, 1, CancellationToken.None)
                .AsTask();

            var destroyed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var referencesRemoved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new Mock<IDynamicNodeManagerHost>();
            host.Setup(value => value.DestroyAddressSpaceAsync(
                    harness.Manager, It.IsAny<CancellationToken>()))
                .Returns((IAsyncNodeManager manager, CancellationToken ct) => DestroyAsync(manager, ct));
            host.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                    harness.Manager, It.IsAny<CancellationToken>()))
                .Returns((IAsyncNodeManager manager, CancellationToken ct) =>
                {
                    referencesRemoved.TrySetResult(true);
                    return default;
                });
            var generation = new NodeManagerGeneration(
                new NodeManagerSlot(), 1, harness.Manager, NodeManagerGenerationState.Live);
            var retired = new GenerationReclaimer.RetiredGeneration(
                harness.Server, host.Object, generation, [], needsDetachment: false);
            generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Task reclaim = reclaimer.ReclaimReadyAsync().AsTask();
            try
            {
                await stream.Cancelled.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await destroyed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await referencesRemoved.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                while (harness.Manager.GetOrCreateHandle(harness.File.NodeId, harness.File.ProviderPath) is not null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token).ConfigureAwait(false);
                }
                await harness.Manager.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(handles.CleanupCompletion.IsCompleted, Is.False);
                Assert.That(stream.CanRead, Is.True);

                Task completed = await Task.WhenAny(reclaim, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.Not.SameAs(reclaim),
                    "Retirement cannot finish while the provider still owns an active file read.");
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(retired.Cleanup.ManagerDisposed, Is.False);
                Assert.That(reclaimer.Contains(retired), Is.True);
            }
            finally
            {
                stream.Release.TrySetResult(true);
                await read.WaitAsync(timeout.Token).ConfigureAwait(false);
                await handles.CleanupCompletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaim.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(retired.Cleanup.ManagerDisposed, Is.True);
            Assert.That(reclaimer.Contains(retired), Is.False);
            Assert.That(stream.CanRead, Is.False);

            async ValueTask DestroyAsync(IAsyncNodeManager manager, CancellationToken ct)
            {
                await manager.DeleteAddressSpaceAsync(ct).ConfigureAwait(false);
                destroyed.TrySetResult(true);
            }
        }

        /// <summary>
        /// Retirement retains a directory cursor while session close and provider disposal are in flight.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        [Category("FileSystem")]
        public async Task RetirementWaitsForCursorDuringSessionCloseAsync(bool closeBeforeRetirement)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var reclaimer = new GenerationReclaimer();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
                server.SetupGet(value => value.IdentityRegistry)
                    .Returns(new ServerIdentityRegistry(new AnonymousAuthenticator()));
                var closeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseClose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                diagnostics.Setup(value => value.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeId>(new NodeId(24680u, 1)));
                diagnostics.Setup(value => value.DeleteSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        closeEntered.TrySetResult(true);
                        return new ValueTask(releaseClose.Task);
                    });
                server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1_000,
                        MaxSessionTimeout = 60_000,
                        MaxSessionCount = 10,
                        MaxBrowseContinuationPoints = 10,
                        MaxHistoryContinuationPoints = 10
                    }
                };
                using var sessions = new SnapshotObservedSessionManager(server.Object, configuration);
                server.SetupGet(value => value.SessionManager).Returns(sessions);
                using Certificate certificate = CertificateBuilder.Create("CN=Cursor Close Test")
                    .SetRSAKeySize(2048).CreateForRSA();
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost/cursor-close",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
                var channel = new SecureChannelContext("cursor-close", endpoint, RequestEncoding.Binary);
                using var createContext = new OperationContext(
                    new RequestHeader(), channel, RequestType.CreateSession, RequestLifetime.None);
                using var activateContext = new OperationContext(
                    new RequestHeader(), channel, RequestType.ActivateSession, RequestLifetime.None);
                var enumerationStarted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var cursorEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseCursor = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cursorCompleted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var cursor = new Mock<IAsyncEnumerator<FileSystemEntry>>();
                cursor.SetupGet(value => value.Current).Returns(new FileSystemEntry(
                    "entry", "entry", true, 0, false,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), string.Empty));
                cursor.Setup(value => value.MoveNextAsync())
                    .Callback(() => enumerationStarted.TrySetResult(true))
                    .Returns(new ValueTask<bool>(true));
                cursor.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(DisposeCursorAsync()));
                var entries = new Mock<IAsyncEnumerable<FileSystemEntry>>();
                entries.Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                    .Returns(cursor.Object);
                var provider = new Mock<IFileSystemProvider>();
                provider.SetupGet(value => value.MountName).Returns("cursor-close");
                provider.Setup(value => value.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                    .Returns(entries.Object);
                using var manager = new FileSystemNodeManager(server.Object, configuration, provider.Object);
                var directory = new DirectoryObjectState(
                    manager.SystemContext,
                    FileSystemNodeId.BuildRoot(manager.NamespaceIndex),
                    string.Empty,
                    "cursor-close",
                    isRoot: true);
                INodeBrowser? browser = null;
                CreateSessionResult? created = null;
                Task? close = null;
                Task<Exception?>? worker = null;
                try
                {
                    created = await sessions.CreateSessionAsync(
                        createContext, certificate, "cursor-close", default, new ApplicationDescription(),
                        endpoint.EndpointUrl, null, null, 60_000, 65_536, timeout.Token).ConfigureAwait(false);
                    (_, _, ServiceResult activation) = await sessions.ActivateSessionAsync(
                        activateContext, created.AuthenticationToken, new SignatureData(), default,
                        new SignatureData(), [], timeout.Token).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(activation), Is.True);
                    Assert.That(created.Session.Activated, Is.True);
                    browser = directory.CreateBrowser(
                        manager.SystemContext, null, ReferenceTypeIds.HasComponent, true, BrowseDirection.Forward,
                        default, null, false);
                    for (int i = 0; i < 128 && !enumerationStarted.Task.IsCompleted; i++)
                    {
                        Assert.That(await browser.NextAsync(timeout.Token).ConfigureAwait(false), Is.Not.Null);
                    }
                    Assert.That(enumerationStarted.Task.IsCompleted, Is.True);

                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
                    var point = new ContinuationPoint { Id = Guid.NewGuid(), Manager = manager, Data = browser };
                    var points = (SessionContinuationPoints)created.Session.ContinuationPoints;
                    points.SaveBrowse(point);
                    Assert.That(points.HasBrowseForGeneration(generation), Is.True);
                    if (closeBeforeRetirement)
                    {
                        close = sessions.CloseSessionAsync(created.SessionId, timeout.Token).AsTask();
                        await closeEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(sessions.GetSession(created.AuthenticationToken), Is.Null);
                        Assert.That(points.HasBrowseForGeneration(generation), Is.True);
                    }

                    var host = new Mock<IDynamicNodeManagerHost>();
                    host.Setup(value => value.DestroyAddressSpaceAsync(manager, It.IsAny<CancellationToken>()))
                        .Returns((IAsyncNodeManager owner, CancellationToken ct) => owner.DeleteAddressSpaceAsync(ct));
                    var retired = new GenerationReclaimer.RetiredGeneration(
                        server.Object, host.Object, generation, [], needsDetachment: false);
                    generation.Gate.Retire();
                    sessions.ObserveNextSnapshot();
                    reclaimer.Enqueue([retired]);
                    reclaimer.Activate([retired]);
                    reclaimer.Schedule();
                    worker = retired.Worker;
                    Assert.That(worker, Is.Not.Null);
                    int visibleSessions = await sessions.SnapshotObserved.Task.WaitAsync(timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(visibleSessions, Is.EqualTo(closeBeforeRetirement ? 0 : 1));
                    if (closeBeforeRetirement)
                    {
                        Assert.That(cursorEntered.Task.IsCompleted, Is.False);
                        await AssertRetirementPendingAsync(
                            worker!, retired, "The removed session still owns the directory cursor.")
                            .ConfigureAwait(false);
                        releaseClose.TrySetResult(true);
                        await close!.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    await cursorEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await AssertRetirementPendingAsync(
                        worker!, retired, "Provider cursor disposal has not completed.").ConfigureAwait(false);
                    releaseCursor.TrySetResult(true);
                    await cursorCompleted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(reclaimer.Contains(retired), Is.False);
                }
                finally
                {
                    releaseClose.TrySetResult(true);
                    releaseCursor.TrySetResult(true);
                    browser?.Dispose();
                    try
                    {
                        if (created is not null)
                        {
                            close ??= sessions.CloseSessionAsync(created.SessionId, timeout.Token).AsTask();
                            await close.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (worker is not null)
                        {
                            Exception? failure = await worker.WaitAsync(timeout.Token).ConfigureAwait(false);
                            Assert.That(failure, Is.Null, "Retirement cleanup failed.");
                        }
                        if (enumerationStarted.Task.IsCompleted)
                        {
                            await cursorCompleted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                }
                cursor.Verify(value => value.DisposeAsync(), Times.Once);

                async Task DisposeCursorAsync()
                {
                    cursorEntered.TrySetResult(true);
                    await releaseCursor.Task.ConfigureAwait(false);
                    cursorCompleted.TrySetResult(true);
                }

                async Task AssertRetirementPendingAsync(
                    Task<Exception?> attempt,
                    GenerationReclaimer.RetiredGeneration retired,
                    string message)
                {
                    Task observed = await Task.WhenAny(
                        attempt, retired.Retirement, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                        .ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    Assert.That(observed, Is.Not.SameAs(attempt), message);
                    Assert.That(observed, Is.Not.SameAs(retired.Retirement), message);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                }
            }
        }

        /// <summary>
        /// A cleanup failure after a bounded disposal is reported and the retired manager remains owned.
        /// </summary>
        [Test]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        public async Task RetirementReportsDeferredManagerCleanupFailureAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                Mock.Get(server.Object.Telemetry)
                    .SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
                var manager = new BoundedCleanupNodeManager(server.Object);
                var host = new Mock<IDynamicNodeManagerHost>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager, NodeManagerGenerationState.Live);
                var retired = new GenerationReclaimer.RetiredGeneration(
                    server.Object, host.Object, generation, [], needsDetachment: false);
                generation.Gate.Retire();
                reclaimer.Enqueue([retired]);
                reclaimer.Activate([retired]);
                Task reclaim = reclaimer.ReclaimReadyAsync().AsTask();
                try
                {
                    await manager.CleanupStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await manager.PublicDisposalReturned.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(manager.FullDisposal, Is.Not.Null);
                    Assert.That(manager.FullDisposal!.IsCompleted, Is.False);
                    var failure = new InvalidOperationException("Deferred manager cleanup failed.");
                    manager.CleanupRelease.TrySetException(failure);
                    AggregateException reported = await Assert.CatchAsync<AggregateException>(
                        async () => await reclaim.WaitAsync(timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false) ??
                        throw new AssertionException("Retirement did not report the deferred cleanup failure.");
                    Assert.That(reported.Flatten().InnerExceptions, Has.Member(failure));
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    Assert.That(retired.Cleanup.ManagerDisposed, Is.False);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                    await Assert.ThatAsync(
                        async () => await manager.FullDisposal!.ConfigureAwait(false),
                        Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
                }
                finally
                {
                    reclaimer.StopScheduling();
                    manager.CleanupRelease.TrySetResult(true);
                    if (manager.FullDisposal is { } fullDisposal)
                    {
                        if (manager.CleanupRelease.Task.IsFaulted)
                        {
                            await Assert.ThatAsync(
                                () => fullDisposal.WaitAsync(timeout.Token),
                                Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
                        }
                        else
                        {
                            await fullDisposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    if (!reclaim.IsCompleted)
                    {
                        if (manager.CleanupRelease.Task.IsFaulted)
                        {
                            await Assert.ThatAsync(
                                () => reclaim.WaitAsync(timeout.Token),
                                Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
                        }
                        else
                        {
                            await reclaim.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    _ = reclaim.Exception;
                }
            }
        }

        /// <summary>
        /// Public disposal failure does not dispose candidate staging before the manager's cleanup has finished.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        public async Task RetirementJoinsManagerCleanupWhenPublicDisposalFailsAsync(bool cleanupFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                Mock.Get(server.Object.Telemetry)
                    .SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
                var publicFailure = new InvalidOperationException("Public disposal failed.");
                var cleanupFailure = new InvalidOperationException("Owned-resource cleanup failed.");
                var manager = new BoundedCleanupNodeManager(server.Object)
                {
                    PublicDisposalFailure = publicFailure
                };
                var host = new Mock<IDynamicNodeManagerHost>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager, NodeManagerGenerationState.Live);
                var retired = new GenerationReclaimer.RetiredGeneration(
                    server.Object, host.Object, generation, [], needsDetachment: false);
                generation.Gate.Retire();
                reclaimer.Enqueue([retired]);
                reclaimer.Activate([retired]);
                Task reclaim = reclaimer.ReclaimReadyAsync().AsTask();
                try
                {
                    await manager.CleanupStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await manager.PublicDisposalReturned.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Task completed = await Task.WhenAny(reclaim, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                        .ConfigureAwait(false);
                    Assert.That(completed, Is.Not.SameAs(reclaim));
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    Assert.That(retired.Cleanup.StagingDisposed, Is.False);

                    if (cleanupFails)
                    {
                        manager.CleanupRelease.TrySetException(cleanupFailure);
                    }
                    else
                    {
                        manager.CleanupRelease.TrySetResult(true);
                    }
                    Exception reported = await Assert.CatchAsync<Exception>(
                        async () => await reclaim.WaitAsync(timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false) ??
                        throw new AssertionException("Retirement did not report the public disposal failure.");
                    if (cleanupFails)
                    {
                        Assert.That(reported, Is.TypeOf<AggregateException>());
                        IReadOnlyCollection<Exception> failures =
                            ((AggregateException)reported).Flatten().InnerExceptions;
                        Assert.That(failures, Has.Member(publicFailure));
                        Assert.That(failures, Has.Member(cleanupFailure));
                        await Assert.ThatAsync(
                            () => manager.FullDisposal!.WaitAsync(timeout.Token),
                            Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
                    }
                    else
                    {
                        Assert.That(reported, Is.SameAs(publicFailure));
                        await manager.FullDisposal!.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    Assert.That(retired.Cleanup.ManagerDisposed, Is.False);
                    Assert.That(retired.Cleanup.StagingDisposed, Is.True);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                }
                finally
                {
                    reclaimer.StopScheduling();
                    manager.CleanupRelease.TrySetResult(true);
                    if (manager.FullDisposal is { } fullDisposal)
                    {
                        if (manager.CleanupRelease.Task.IsFaulted)
                        {
                            await Assert.ThatAsync(
                                () => fullDisposal.WaitAsync(timeout.Token),
                                Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
                        }
                        else
                        {
                            await fullDisposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    if (!reclaim.IsCompleted)
                    {
                        await Assert.ThatAsync(
                            () => reclaim.WaitAsync(timeout.Token),
                            Throws.Exception).ConfigureAwait(false);
                    }
                    _ = reclaim.Exception;
                }
            }
        }

        /// <summary>
        /// When a manager skips base disposal, retirement reports an error instead of waiting indefinitely.
        /// </summary>
        [Test]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        public async Task RetirementReportsManagerThatDoesNotStartBaseDisposalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                Mock.Get(server.Object.Telemetry)
                    .SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
                var manager = new BoundedCleanupNodeManager(server.Object)
                {
                    SkipBaseDisposal = true
                };
                var host = new Mock<IDynamicNodeManagerHost>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager, NodeManagerGenerationState.Live);
                var retired = new GenerationReclaimer.RetiredGeneration(
                    server.Object, host.Object, generation, [], needsDetachment: false);
                generation.Gate.Retire();
                reclaimer.Enqueue([retired]);
                reclaimer.Activate([retired]);
                Task reclaim = reclaimer.ReclaimReadyAsync().AsTask();
                try
                {
                    await Assert.ThatAsync(
                        () => reclaim.WaitAsync(timeout.Token),
                        Throws.InstanceOf<InvalidOperationException>()
                            .With.Message.Contains("did not start its base resource disposal"))
                        .ConfigureAwait(false);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    Assert.That(retired.Cleanup.ManagerDisposed, Is.False);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                }
                finally
                {
                    reclaimer.StopScheduling();
                    manager.CleanupRelease.TrySetResult(true);
                    manager.Dispose();
                    await manager.WaitForDisposalCompletionAsync().AsTask()
                        .WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// An unrelated generation finishes while another generation is blocked in its drain or destruction callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentRetirementDoesNotWaitForUnrelatedDrainOrDestructionAsync(bool blockDrain)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    if (!blockDrain)
                    {
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            reclaimer.Activate([first, second]);
            try
            {
                reclaimer.Schedule();
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(first), Is.True);
                Assert.That(reclaimer.Contains(second), Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    second.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
                if (blockDrain)
                {
                    context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                        first.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                }
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Both the captured view epoch and the exact generation's admitted callback must drain before destruction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetirementWaitsForExactEpochAndGenerationGateAsync(bool releaseEpochFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int pin), Is.True);
            retired.Generation.RegisterViewEpoch(epoch);
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            epoch.Close();
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            if (releaseEpochFirst)
            {
                epoch.Exit(pin);
            }
            else
            {
                retired.Generation.Gate.Exit(admission);
            }
            try
            {
                Assert.That(retired.Generation.IsQuiescent, Is.False);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
            }
            finally
            {
                if (releaseEpochFirst)
                {
                    retired.Generation.Gate.Exit(admission);
                }
                else
                {
                    epoch.Exit(pin);
                }
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(retired.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// A delayed reclamation logs its exact blockers once without releasing or destroying the generation.
        /// </summary>
        [Test]
        public async Task QuiescenceWarningReportsExactGenerationAndPinsWithoutForcingCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            var timerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var warning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timeProvider = new Mock<TimeProvider>();
            timeProvider.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            timeProvider.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            timeProvider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ITimer timer = clock.CreateTimer(callback, state, dueTime, period);
                    timerReady.TrySetResult(true);
                    return timer;
                });
            using var reclaimer = new GenerationReclaimer(timeProvider.Object);
            var context = new ReclamationContext();
            GenerationReclaimer.RetiredGeneration retired = context.CreateRetirement();
            context.Logger.Setup(value => value.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                    warning.TrySetResult(invocation.Arguments[2]?.ToString()
                        ?? throw new InvalidOperationException("The warning did not contain log state."))));
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int pin), Is.True);
            retired.Generation.RegisterViewEpoch(epoch);
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            epoch.Close();
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                reclaimer.Schedule();
                await timerReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                clock.Advance(LifecycleWaitDiagnostics.WarningThreshold);
                string message = await warning.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(message, Does.Contain("retired generation quiescence"));
                Assert.That(message, Does.Contain(LifecycleWaitDiagnostics.WarningThreshold.ToString()));
                Assert.That(message, Does.Contain(retired.Generation.GetQuiescenceWaitContext()));
                Assert.That(message, Does.Contain($"pinnedEpochs=[{epoch.Id}=1]"));
                Assert.That(retired.Generation.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(epoch.ActiveCount, Is.EqualTo(1));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                clock.Advance(TimeSpan.FromMinutes(1));
            }
            finally
            {
                epoch.Exit(pin);
                retired.Generation.Gate.Exit(admission);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            context.Logger.Verify(value => value.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(retired.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// A failed post-destruction stage is logged while retry skips every successful stage.
        /// </summary>
        [TestCase("Notifications")]
        [TestCase("References")]
        [TestCase("Destruction")]
        [TestCase("DestroyedReferences")]
        [TestCase("Disposal")]
        public async Task CleanupFailureIsLoggedAndRetriedWithoutReplayingSuccessfulStagesAsync(string stage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            int calls = 0;
            var failure = new InvalidOperationException(stage + " failed.");
            ValueTask FailOnce()
            {
                return ++calls == 1 ? new ValueTask(Task.FromException(failure)) : default;
            }
            Mock<IMasterNodeManager> master = Mock.Get(retired.Server.NodeManager);
            retired.PendingReferences.Add(new LocalReference(
                new NodeId(1), ReferenceTypeIds.HasComponent, false, new NodeId(2)));
            switch (stage)
            {
                case "Notifications":
                    context.Host.Setup(value => value.FinalizeRetiredGenerationNotificationsAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "References":
                    master.Setup(value => value.RemoveReferencesAsync(
                        It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Destruction":
                    context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "DestroyedReferences":
                    context.Host.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Disposal":
                    disposal.Setup(value => value.DisposeAsync()).Returns(FailOnce);
                    break;
                default:
                    Assert.Fail("Unknown cleanup stage.");
                    break;
            }
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            await Assert.ThatAsync(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message)).ConfigureAwait(false);
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Assert.That(reclaimer.Count, Is.EqualTo(1));
            disposal.Verify(value => value.DisposeAsync(), Times.Exactly(stage == "Disposal" ? 1 : 0));
            context.Logger.Verify(value => value.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);
            Assert.That(reclaimer.Count, Is.Zero);
            Assert.That(retired.PendingReferences, Is.Empty);
            Assert.That(calls, Is.EqualTo(2));
            context.Host.Verify(value => value.FinalizeRetiredGenerationNotificationsAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Notifications" ? 2 : 1));
            master.Verify(value => value.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "References" ? 2 : 1));
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Destruction" ? 2 : 1));
            context.Host.Verify(value => value.RemoveDestroyedExternalReferencesAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "DestroyedReferences" ? 2 : 1));
            disposal.Verify(value => value.DisposeAsync(), Times.Exactly(stage == "Disposal" ? 2 : 1));
        }

        /// <summary>
        /// A transient cleanup failure is retried after backoff; retirement completes after the retry
        /// and surfaces the first failure.
        /// </summary>
        [Test]
        public async Task CleanupFailureSchedulesDelayedRetryAndCompletesRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            using var reclaimer = new GenerationReclaimer(clock);
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            int attempts = 0;
            var failure = new InvalidOperationException("Transient destruction failure.");
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(() => ++attempts == 1
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(observed, Is.SameAs(failure));
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Shutdown independently admits pending generations even if normal post-publication activation never ran.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ShutdownStartsPendingGenerationsIndependentlyAsync(bool blockDrain, bool alreadyAdmitted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            if (alreadyAdmitted)
            {
                reclaimer.Activate([first]);
                reclaimer.Schedule();
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Task shutdown = reclaimer.CompleteShutdownAsync(timeout.Token).AsTask();
            try
            {
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(second), Is.False);
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Concurrent drain observers cannot replay an already admitted destruction callback.
        /// </summary>
        [Test]
        public async Task ConcurrentSchedulingClaimsGenerationOnceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(reclaimer.Schedule))).WaitAsync(timeout.Token).ConfigureAwait(false);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Repeated publication activation and a worker share one transition task, including a failed-task retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentActivationSharesTransitionFinalizationAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.NodeId).Returns(new NodeId(1));
            Mock<IMonitoredItemGenerationBinding> binding = item.As<IMonitoredItemGenerationBinding>();
            int marks = 0;
            binding.Setup(value => value.TryInvalidateSource(0, false)).Returns(() =>
            {
                if (Interlocked.Increment(ref marks) == 1 && failFirst)
                {
                    throw new InvalidOperationException("Transition finalization failed.");
                }
                return true;
            });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int drains = 0;
            Func<CancellationToken, ValueTask>? drain = async ct =>
            {
                Interlocked.Increment(ref drains);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            var current = new Mock<INodeManagerMonitoredItemSource>();
            current.Setup(value => value.GetNotificationDrain(item.Object.NodeId)).Returns(drain);
            retired.Transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current.Object, null, [], [item.Object], _ => true);
            Assert.That(
                await retired.Transition.AdoptAsync(timeout.Token).ConfigureAwait(false),
                Is.Empty);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            reclaimer.Schedule();
            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(drains, Is.EqualTo(1));
            Assert.That(marks, Is.Zero);
            Task<Exception?> worker = retired.Worker!;
            release.TrySetResult(true);
            if (failFirst)
            {
                Exception? failure = await worker.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<AggregateException>());
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(marks, Is.EqualTo(1));
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => retired.Retirement.WaitAsync(timeout.Token),
                    Throws.Exception).ConfigureAwait(false);
                Assert.That(retired.Retirement.IsFaulted, Is.True);
            }
            else
            {
                await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(marks, Is.EqualTo(failFirst ? 2 : 1));
            Assert.That(drains, Is.EqualTo(1), "Retry must not repeat completed notification fences.");
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Shutdown joins an admitted asynchronous disposal, and a cancelled join leaves cleanup retryable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownWaitsForAdmittedCleanupAndCanRetryCancelledWaitAsync(bool cancelWait)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            disposal.Setup(value => value.DisposeAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            Task shutdown = reclaimer.CompleteShutdownAsync(
                cancelWait ? cancelled.Token : timeout.Token).AsTask();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                if (cancelWait)
                {
                    cancelled.Cancel();
                    await Assert.ThatAsync(async () => await shutdown.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            if (!cancelWait)
            {
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Asserts that retirement completed and surfaced the first cleanup failure, if any, even after a
        /// successful retry.
        /// </summary>
        private static async Task AssertRetirementOutcomeAsync(
            Task retirement,
            Exception? firstFailure,
            CancellationToken cancellationToken)
        {
            if (firstFailure is null)
            {
                await retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                return;
            }
            await Assert.ThatAsync(
                () => retirement.WaitAsync(cancellationToken),
                Throws.Exception.SameAs(firstFailure)).ConfigureAwait(false);
            Assert.That(retirement.IsFaulted, Is.True);
        }

        /// <summary>
        /// Returns from public disposal while its owned-resource cleanup is still pending.
        /// </summary>
        private sealed class BoundedCleanupNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates a manager backed by the deterministic server.
            /// </summary>
            public BoundedCleanupNodeManager(IServerInternal server)
                : base(server, new ApplicationConfiguration(), DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <summary>
            /// Signals that the base disposal core has started.
            /// </summary>
            public TaskCompletionSource<bool> CleanupStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Controls completion of the base disposal core.
            /// </summary>
            public TaskCompletionSource<bool> CleanupRelease { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Signals that the bounded public disposal has returned.
            /// </summary>
            public TaskCompletionSource<bool> PublicDisposalReturned { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the base disposal task so the test can observe its result.
            /// </summary>
            public Task? FullDisposal { get; private set; }

            /// <summary>
            /// Gets whether public disposal omits base cleanup.
            /// </summary>
            public bool SkipBaseDisposal { get; init; }

            /// <summary>
            /// Gets the public disposal failure to report while base cleanup is pending.
            /// </summary>
            public Exception? PublicDisposalFailure { get; init; }

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                if (SkipBaseDisposal)
                {
                    return;
                }
                Task cleanup = base.DisposeAsync().AsTask();
                FullDisposal = cleanup;
                if (PublicDisposalFailure is { } failure)
                {
                    PublicDisposalReturned.TrySetResult(true);
                    throw failure;
                }
                if (await Task.WhenAny(cleanup, Task.CompletedTask).ConfigureAwait(false) == cleanup)
                {
                    await cleanup.ConfigureAwait(false);
                }
                PublicDisposalReturned.TrySetResult(true);
            }

            /// <inheritdoc/>
            protected override async ValueTask DisposeAsyncCore()
            {
                CleanupStarted.TrySetResult(true);
                await CleanupRelease.Task.ConfigureAwait(false);
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Observes a session snapshot while using the real session manager close path.
        /// </summary>
        private sealed class SnapshotObservedSessionManager : SessionManager
        {
            /// <summary>
            /// Creates a session manager whose next session snapshot can be observed.
            /// </summary>
            internal SnapshotObservedSessionManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            /// <summary>
            /// Receives the number of sessions in the next observed snapshot.
            /// </summary>
            internal TaskCompletionSource<int> SnapshotObserved { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public override IList<ISession> GetSessions()
            {
                IList<ISession> sessions = base.GetSessions();
                if (Interlocked.Exchange(ref m_observeSnapshot, 0) != 0)
                {
                    SnapshotObserved.TrySetResult(sessions.Count);
                }
                return sessions;
            }

            /// <summary>
            /// Observes the next session snapshot taken by retirement.
            /// </summary>
            internal void ObserveNextSnapshot()
            {
                Interlocked.Exchange(ref m_observeSnapshot, 1);
            }

            /// <summary>Marks whether to report the next session snapshot.</summary>
            private int m_observeSnapshot;
        }

        /// <summary>
        /// Supplies deterministic server services and a host without sockets or lifecycle writer state.
        /// </summary>
        private sealed class ReclamationContext
        {
            /// <summary>
            /// Creates an empty server with source-generated logging captured for assertions.
            /// </summary>
            internal ReclamationContext()
            {
                _ = m_server.As<IHistorianRegistryProvider>();
                _ = m_server.As<IHistorianBuilderRegistry>();
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                var subscriptions = new Mock<ISubscriptionManager>();
                subscriptions.Setup(value => value.GetSubscriptions()).Returns([]);
                Logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
                m_server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                m_server.SetupGet(value => value.SubscriptionManager).Returns(subscriptions.Object);
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                Host = m_master.As<IDynamicNodeManagerHost>();
                m_server.SetupGet(value => value.NodeManager).Returns(m_master.Object);
                Host.SetupGet(value => value.GenerationReconciler)
                    .Returns(new GenerationReconciler(m_server.Object));
            }

            /// <summary>
            /// Gets the dynamic host callback mock.
            /// </summary>
            internal Mock<IDynamicNodeManagerHost> Host { get; }

            /// <summary>
            /// Gets the server mock used to construct candidate staging metadata.
            /// </summary>
            internal Mock<IServerInternal> Server => m_server;

            /// <summary>
            /// Gets the cleanup diagnostic logger.
            /// </summary>
            internal Mock<ILogger> Logger { get; } = new();

            /// <summary>
            /// Creates a distinct live generation ready to be retired by the test.
            /// </summary>
            /// <returns>The exact generation's retirement record.</returns>
            internal GenerationReclaimer.RetiredGeneration CreateRetirement()
            {
                var manager = new Mock<IAsyncNodeManager>();
                manager.As<IAsyncDisposable>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                return new GenerationReclaimer.RetiredGeneration(
                    m_server.Object, Host.Object, generation, [], needsDetachment: true);
            }

            /// <summary>The owning server mock.</summary>
            private readonly Mock<IServerInternal> m_server = new();

            /// <summary>The host's master-node-manager interface.</summary>
            private readonly Mock<IMasterNodeManager> m_master = new();
        }
    }
}
