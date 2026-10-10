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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task ClosingSessionRevokesAllPreparedTransfersBeforeSlowProviderCleanupCompletesAsync()
        {
            var clock = new SyncClock();
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(clock).ConfigureAwait(false);
            await using ManagedSession other = await ConnectAsync().ConfigureAwait(false);
            var owned = new RegistryBridgeTypeClient(other, bridge.ObjectId, m_telemetry);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}");
            (NodeId upload, uint uploadHandle) = await owned.BeginRequestAsync().ConfigureAwait(false);
            var file = new FileTypeClient(other, upload, m_telemetry);
            await WriteChunksAsync(file, uploadHandle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(uploadHandle).ConfigureAwait(false);
            _ = await owned.PrepareRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                .ConfigureAwait(false);
            m_forwarder.DisposeEntered = Signal();
            m_forwarder.DisposeRelease = Signal();
            m_forwarder.DisposeCompleted = Signal();
            Task closing = other.CloseAsync();
            bool entered = false;
            try
            {
                await m_forwarder.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                entered = true;
                await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
                Assert.That(m_forwarder.DisposeCompleted.Task.IsCompleted, Is.False);
                clock.Advance(TimeSpan.FromSeconds(1));
                await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                m_forwarder.DisposeRelease.TrySetResult(true);
                if (entered)
                {
                    await m_forwarder.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
                await other.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [Test]
        public async Task AbortingPreparedUploadAlsoRevokesItsUnreadPreviewAsync()
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}");
            NodeId upload = await UploadAsync(bridge, request).ConfigureAwait(false);
            (NodeId preview, uint handle) = await bridge.PrepareRequestAsync(
                upload, string.Empty, m_codec.ComputeRequestDigest(request)).ConfigureAwait(false);
            await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new FileTypeClient(m_session, preview, m_telemetry).ReadAsync(handle, 32).ConfigureAwait(false));
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [Test]
        public async Task AbortingDuringCommitAuthorizationRevokesTheLeaseBeforeDispatchAsync()
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            NodeId upload = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            m_authorizationEntered = Signal();
            m_authorizationRelease = Signal();
            Task<(NodeId File, uint Handle)> commit = bridge.CommitPreparedRequestAsync(upload).AsTask();
            try
            {
                await m_authorizationEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            }
            finally
            {
                m_authorizationRelease.TrySetResult(true);
            }
            ServiceResultException revoked = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await commit.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(revoked.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
        }

        [Test]
        public async Task ExpiredPreparationsAreAllRevokedEvenWhenOneCleanupFailsAsync()
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(maximumTransfers: 3).ConfigureAwait(false);
            _ = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            _ = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            int before = m_forwarder.CompletedDisposals;
            m_forwarder.FailDisposal = true;
            m_clock.Advance(m_options.FileLifetime);
            try
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await bridge.BeginRequestAsync().ConfigureAwait(false));
            }
            finally
            {
                m_forwarder.FailDisposal = false;
            }
            Assert.That(m_forwarder.CompletedDisposals - before, Is.EqualTo(2));
            await AssertFullTransferCapacityAsync(bridge, 3).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [Test]
        public async Task KnownCommitResponseIsNotChangedIntoRejectionByCleanupFailureAsync()
        {
            m_forwarder.FailDisposal = true;
            XRegistryResponse committed;
            try
            {
                committed = await m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"committed-despite-cleanup"}""")).ConfigureAwait(false);
            }
            finally
            {
                m_forwarder.FailDisposal = false;
            }
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed.StatusCode, Is.EqualTo(200));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("committed-despite-cleanup"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedOutPreparationAbortsItsLateLeaseWithoutAllocatingAPreviewAsync(bool providerFails)
        {
            var clock = new SyncClock();
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(clock).ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}") with { OperationId = "late-prepare" };
            NodeId upload = await UploadAsync(bridge, request).ConfigureAwait(false);
            TaskCompletionSource<bool> entered = Signal();
            TaskCompletionSource<bool> release = Signal();
            m_forwarder.DisposeCompleted = Signal();
            m_forwarder.AfterPrepareAsync = async _ =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (providerFails)
                {
                    throw new IOException("Injected late preparation failure.");
                }
            };
            Task<(NodeId File, uint Handle)> prepare = bridge.PrepareRequestAsync(
                upload, request.OperationId, m_codec.ComputeRequestDigest(request)).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResultException timeout = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await prepare.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.That(timeout.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(m_forwarder.DisposeCompleted.Task.IsCompleted, Is.False);
                await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
                await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await m_forwarder.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            XRegistryOperationOutcome outcome = await m_forwarder.Inner.GetOperationOutcomeAsync(
                request.OperationId, s_writer).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Unknown));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
        }

        [Test]
        public async Task ProviderDisposalFailureDoesNotRetainTransferNodesOrLocalQuotaAsync()
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            NodeId upload = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            m_forwarder.FailDisposal = true;
            try
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await bridge.AbortRequestAsync(upload).ConfigureAwait(false));
            }
            finally
            {
                m_forwarder.FailDisposal = false;
            }
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty);
            ServiceResultException absent = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitPreparedRequestAsync(upload).ConfigureAwait(false));
            Assert.That(absent.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedOutNativeCommitKeepsItsLeaseUntilTheLateOutcomeCompletesAsync(bool providerFails)
        {
            var clock = new SyncClock();
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(clock).ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"late-commit"}""") with
            { OperationId = "late-native-commit" };
            NodeId upload = await PrepareCleanupUploadAsync(bridge, request).ConfigureAwait(false);
            m_forwarder.Entered = Signal();
            m_forwarder.Release = Signal();
            m_forwarder.DisposeEntered = Signal();
            m_forwarder.DisposeCompleted = Signal();
            m_forwarder.IgnoreCommitCancellation = true;
            m_forwarder.FailCommitAfterBarrier = providerFails;
            Task<(NodeId File, uint Handle)> commit = bridge.CommitPreparedRequestAsync(upload).AsTask();
            try
            {
                await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResultException timeout = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await commit.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(timeout.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
                    Assert.That(m_forwarder.DisposeEntered.Task.IsCompleted, Is.False,
                        "A pending provider commit still owns its prepared lease.");
                });
                await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
                await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            }
            finally
            {
                m_forwarder.Release.TrySetResult(true);
                await m_forwarder.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            XRegistryOperationOutcome outcome = await m_forwarder.Inner.GetOperationOutcomeAsync(
                request.OperationId, s_writer).ConfigureAwait(false);
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(providerFails
                    ? XRegistryOperationState.Unknown : XRegistryOperationState.Committed));
                if (!providerFails)
                {
                    Assert.That(outcome.Response!.Metadata.GetProperty("name").GetString(), Is.EqualTo("late-commit"));
                }
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(providerFails ? 0 : 1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedOutAbortReclaimsLocalCapacityWhileProviderCleanupContinuesAsync(bool providerFails)
        {
            var clock = new SyncClock();
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(clock).ConfigureAwait(false);
            NodeId upload = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            m_forwarder.DisposeEntered = Signal();
            m_forwarder.DisposeRelease = Signal();
            m_forwarder.DisposeCompleted = Signal();
            m_forwarder.FailDisposal = providerFails;
            Task abort = bridge.AbortRequestAsync(upload).AsTask();
            try
            {
                await m_forwarder.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResultException timeout = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await abort.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.That(timeout.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
                await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            }
            finally
            {
                m_forwarder.DisposeRelease.TrySetResult(true);
                await m_forwarder.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CleanupTimerFailureStillRetiresTheDetachedTransferAsync()
        {
            var clock = new FailingTimerProvider();
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync(clock).ConfigureAwait(false);
            NodeId upload = await PrepareCleanupUploadAsync(bridge).ConfigureAwait(false);
            m_forwarder.DisposeCompleted = Signal();
            clock.Fail = true;
            try
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await bridge.AbortRequestAsync(upload).ConfigureAwait(false));
                await m_forwarder.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            finally
            {
                clock.Fail = false;
            }
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [Test]
        public async Task FailedResponseFileOpenReclaimsEveryReservedTransferAsync()
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}");
            NodeId upload = await UploadAsync(bridge, request).ConfigureAwait(false);
            m_forwarder.AfterPrepareAsync = _ =>
            {
                m_denyReads = true;
                return default;
            };
            ServiceResultException? denied;
            try
            {
                denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await bridge.PrepareRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                        .ConfigureAwait(false));
            }
            finally
            {
                m_denyReads = false;
                m_forwarder.AfterPrepareAsync = null;
                await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            }
            Assert.That(denied?.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LostPreparedUploadCannotStrandItsResponseTransferAsync(bool expire)
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}");
            NodeId upload = await UploadAsync(bridge, request).ConfigureAwait(false);
            TaskCompletionSource<bool> entered = Signal();
            TaskCompletionSource<bool> release = Signal();
            m_forwarder.AfterPrepareAsync = async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            Task<(NodeId File, uint Handle)> preparing = bridge.PrepareRequestAsync(
                upload, string.Empty, m_codec.ComputeRequestDigest(request)).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (expire)
                {
                    m_clock.Advance(m_options.FileLifetime);
                }
                else
                {
                    await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
                }
            }
            finally
            {
                release.TrySetResult(true);
                m_forwarder.AfterPrepareAsync = null;
            }
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await preparing.ConfigureAwait(false));
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await AssertFullTransferCapacityAsync(bridge).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SlowPreparedCommitOrAbortDoesNotBlockOtherTransfersAsync(bool commit)
        {
            RegistryBridgeTypeClient bridge = await CreateCleanupBridgeAsync().ConfigureAwait(false);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"delayed"}""");
            NodeId upload = await PrepareCleanupUploadAsync(bridge, request).ConfigureAwait(false);
            TaskCompletionSource<bool> entered = Signal();
            TaskCompletionSource<bool> release = Signal();
            if (commit)
            {
                m_forwarder.Entered = entered;
                m_forwarder.Release = release;
            }
            else
            {
                m_forwarder.DisposeEntered = entered;
                m_forwarder.DisposeRelease = release;
            }
            Task pending = commit
                ? bridge.CommitPreparedRequestAsync(upload).AsTask()
                : bridge.AbortRequestAsync(upload).AsTask();
            Task<(NodeId File, uint Handle)>? other = null;
            bool responsive = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                other = bridge.BeginRequestAsync().AsTask();
                try
                {
                    _ = await other.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    responsive = true;
                }
                catch (TimeoutException)
                {
                    // Release the deliberately blocked provider before failing the assertion.
                }
            }
            finally
            {
                release.TrySetResult(true);
                await pending.ConfigureAwait(false);
                if (other is not null)
                {
                    (NodeId file, _) = await other.ConfigureAwait(false);
                    await bridge.AbortRequestAsync(file).ConfigureAwait(false);
                }
                if (commit)
                {
                    await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
                }
            }
            Assert.That(responsive, Is.True, "Unrelated transfers must not wait for an external provider.");
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(commit ? 1 : 0));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(commit ? 1 : 0));
            });
        }

        private async Task<RegistryBridgeTypeClient> CreateCleanupBridgeAsync(
            TimeProvider? timeProvider = null, int maximumTransfers = 2)
        {
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-cleanup:" + Guid.NewGuid().ToString("N"),
                MaxOpenFiles = maximumTransfers,
                TimeProvider = timeProvider ?? m_clock,
                PreparedOperationTimeout = timeProvider is null
                    ? m_options.PreparedOperationTimeout : TimeSpan.FromSeconds(1),
                CleanupTimeout = timeProvider is null ? m_options.CleanupTimeout : TimeSpan.FromSeconds(1)
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            return new RegistryBridgeTypeClient(m_session,
                new NodeId(options.RootIdentifier + "/Bridge", factory.Manager!.RegistryNodeId.NamespaceIndex),
                m_telemetry);
        }

        private async Task<NodeId> PrepareCleanupUploadAsync(
            RegistryBridgeTypeClient bridge, XRegistryRequest? request = null)
        {
            request ??= Request(XRegistryAction.Merge, "/", "{}");
            NodeId upload = await UploadAsync(bridge, request).ConfigureAwait(false);
            (NodeId preview, uint handle) = await bridge.PrepareRequestAsync(
                upload, request.OperationId ?? string.Empty, m_codec.ComputeRequestDigest(request)).ConfigureAwait(
                    false);
            _ = await ReadTransferAsync(bridge, preview, handle).ConfigureAwait(false);
            return upload;
        }

        private async Task<NodeId> UploadAsync(RegistryBridgeTypeClient bridge, XRegistryRequest request)
        {
            (NodeId id, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            var file = new FileTypeClient(m_session, id, m_telemetry);
            await WriteChunksAsync(file, handle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            return id;
        }

        private static async Task AssertFullTransferCapacityAsync(
            RegistryBridgeTypeClient bridge, int maximumTransfers = 2)
        {
            var uploads = new List<NodeId>();
            try
            {
                for (int index = 0; index < maximumTransfers; index++)
                {
                    (NodeId id, _) = await bridge.BeginRequestAsync().ConfigureAwait(false);
                    uploads.Add(id);
                }
            }
            finally
            {
                foreach (NodeId id in uploads)
                {
                    await bridge.AbortRequestAsync(id).ConfigureAwait(false);
                }
            }
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class FailingTimerProvider : TimeProvider
        {
            public bool Fail { get; set; }

            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                return Fail ? throw new InvalidOperationException("Injected timer creation failure.") :
                    base.CreateTimer(callback, state, dueTime, period);
            }
        }
    }
}
