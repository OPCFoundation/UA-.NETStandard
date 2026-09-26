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

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RuntimeResourceIsolationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void StartupLogsEffectiveStageLimitsWithoutOwnerIdentities(bool informationEnabled)
        {
            using var logs = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(
                    informationEnabled ? LogLevel.Information : LogLevel.Warning).AddProvider(logs));
            ServerResourceIsolationPlan plan = Options().CreateRuntimePlan(
                Configuration(), new ServerRateLimitOptions(), new ChunkReassemblyBudget(100, 50));
            using var provider = new DefaultServerResourceIsolationProvider(plan, telemetry);
            RecordedLogRecord[] stages = logs.Records
                .Where(record => record.EventId.Id == ServerEventIds.ResourceIsolation + 1).ToArray();

            Assert.That(stages, Has.Length.EqualTo(
                informationEnabled ? (int)ResourceIsolationStage.ParkedRequest + 1 : 0));
            for (int ii = 0; ii < stages.Length; ii++)
            {
                ResourceIsolationStagePlan limits = plan.GetStage((ResourceIsolationStage)ii);
                RecordedLogRecord record = stages.Single(
                    entry => entry.Properties["Stage"]?.ToString() == limits.Stage.ToString());
                Assert.That(record.LogLevel, Is.EqualTo(LogLevel.Information));
                Assert.That(record.Properties["Capacity"], Is.EqualTo(limits.Capacity));
                Assert.That(record.Properties["SharedCapacity"], Is.EqualTo(limits.SharedCapacity));
                Assert.That(record.Properties["BootstrapReserved"], Is.EqualTo(limits.BootstrapReserved));
                Assert.That(record.Properties["ReconnectReserved"], Is.EqualTo(limits.ReconnectReserved));
                Assert.That(record.Properties["ControlReserved"], Is.EqualTo(limits.ControlReserved));
                Assert.That(record.Properties["TrustedReserved"], Is.EqualTo(limits.TrustedReserved));
                Assert.That(record.Properties["OwnerHardLimit"], Is.EqualTo(limits.OwnerHardLimit));
                Assert.That(record.Properties.Keys, Does.Not.Contain("OwnerKey"));
            }
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public async Task DefaultReassemblyPreservesSessionHeadroomWithoutATenantClassifierAsync(
            int sessionChannels, bool separatePeers)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new ReassemblyPool();
            var buffers = new BufferManager("review-headroom", 8192, telemetry, pool);
            byte[] probe = buffers.TakeBuffer(32, "probe");
            int rental = probe.Length;
            buffers.ReturnBuffer(probe, "probe");
            var budget = new ChunkReassemblyBudget(8L * rental);
            var bindings = new Mock<ISessionBindingProvider>();
            var options = new ServerResourceIsolationOptions { MaxRetainedMessageBytes = 2L * rental };
            ApplicationConfiguration configuration = Configuration();
            using var provider = new DefaultServerResourceIsolationProvider(
                options.CreateRuntimePlan(configuration, new ServerRateLimitOptions(), budget),
                telemetry, bindings.Object);
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry))
            {
                MaxBufferSize = 8192,
                MaxMessageSize = 32768,
                ChunkReassemblyBudget = budget,
                ResourceIsolationProvider = provider,
                SessionBindingProvider = bindings.Object
            };
            using var holder = new ReassemblyChannel(1, buffers, quotas, telemetry, 1);
            using var otherHolder = new ReassemblyChannel(5, buffers, quotas, telemetry, separatePeers ? 5 : 1);
            using var session = new ReassemblyChannel(2, buffers, quotas, telemetry, separatePeers ? 2 : 1);
            using var otherSession = new ReassemblyChannel(3, buffers, quotas, telemetry, separatePeers ? 3 : 1);
            using var refused = new ReassemblyChannel(4, buffers, quotas, telemetry, separatePeers ? 4 : 1);
            bindings.Setup(value => value.HasSession(session.GlobalChannelId)).Returns(true);
            bindings.Setup(value => value.HasSession(otherSession.GlobalChannelId)).Returns(true);

            for (uint sequence = 1; sequence <= 2; sequence++)
            {
                await holder.FeedPartAsync(sequence).ConfigureAwait(false);
                await otherHolder.FeedPartAsync(sequence).ConfigureAwait(false);
            }
            Assert.That(provider.GetUsage(ResourceIsolationStage.ReassemblyBytes),
                Is.EqualTo(4L * rental));
            await session.FeedPartAsync(1).ConfigureAwait(false);
            if (sessionChannels == 2)
            {
                await otherSession.FeedPartAsync(1).ConfigureAwait(false);
                Assert.That(otherSession.CurrentState, Is.EqualTo(TcpChannelState.Open));
            }

            Assert.That(session.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(provider.GetUsage(ResourceIsolationStage.ReassemblyBytes),
                Is.EqualTo((4L + sessionChannels) * rental));
            await refused.FeedPartAsync(1).ConfigureAwait(false);
            Assert.That(refused.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(holder.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(otherHolder.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(session.CurrentState, Is.EqualTo(TcpChannelState.Open));
            session.DiscardMessage();
            bindings.Setup(value => value.HasSession(session.GlobalChannelId)).Returns(false);
            await session.FeedPartAsync(2).ConfigureAwait(false);
            Assert.That(session.CurrentState, Is.EqualTo(TcpChannelState.Closed),
                "A new message after membership removal cannot inherit the continuity reserve.");
            holder.Dispose();
            otherHolder.Dispose();
            session.Dispose();
            otherSession.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.ReassemblyBytes), Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.Outstanding, Is.Zero);
        }

        /// <summary>
        /// Drives real server reassembly without sockets or a tenant-specific classifier.
        /// </summary>
        private sealed class ReassemblyChannel : TcpServerChannel
        {
            public ReassemblyChannel(
                uint id, BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry, int peer)
                : base("headroom-review", Mock.Of<ITcpChannelListener>(), buffers, quotas,
                    null!, [], telemetry, new FakeTimeProvider())
            {
                ChannelId = id;
                State = TcpChannelState.Open;
                MaxRequestChunkCount = 2;
                MaxRequestMessageSize = 16;
                var transport = new Mock<IUaSCByteTransport>();
                transport.SetupGet(value => value.RemoteEndpoint).Returns(Peer(peer));
                Transport = transport.Object;
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(new ChannelToken
                {
                    ChannelId = id,
                    TokenId = 1,
                    SecurityPolicy = SecurityPolicyInfo.None,
                    CreatedAt = DateTime.UtcNow,
                    CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                    Lifetime = 60000
                }, null);
            }

            public TcpChannelState CurrentState => State;

            public void DiscardMessage()
            {
                TakeSavedChunks().Release(BufferManager, nameof(DiscardMessage));
            }

            public ValueTask FeedPartAsync(uint sequence)
            {
                byte[] buffer = BufferManager.TakeBuffer(32, nameof(FeedPartAsync));
                BitConverter.GetBytes(TcpMessageType.Message | TcpMessageType.Intermediate).CopyTo(buffer, 0);
                BitConverter.GetBytes(32).CopyTo(buffer, 4);
                BitConverter.GetBytes(ChannelId).CopyTo(buffer, 8);
                BitConverter.GetBytes(1u).CopyTo(buffer, 12);
                BitConverter.GetBytes(sequence).CopyTo(buffer, 16);
                BitConverter.GetBytes(1u).CopyTo(buffer, 20);
                return OnChunkReceivedAsync(new ArraySegment<byte>(buffer, 0, 32), CancellationToken.None);
            }
        }

        /// <summary>
        /// Tracks actual backing-array ownership with predictable tiny rentals.
        /// </summary>
        private sealed class ReassemblyPool : ArrayPool<byte>
        {
            public int Outstanding => Volatile.Read(ref m_outstanding);

            public override byte[] Rent(int minimumLength)
            {
                Interlocked.Increment(ref m_outstanding);
                return new byte[minimumLength];
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                Interlocked.Decrement(ref m_outstanding);
                array.AsSpan().Clear();
            }

            private int m_outstanding;
        }

        [Test]
        public void ContinuityReassemblyOwnerDoesNotLendTheReconnectTableSlot()
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = 4;
            var bindings = new Mock<ISessionBindingProvider>();
            bindings.Setup(value => value.HasSession("active")).Returns(true);
            using DefaultServerResourceIsolationProvider provider =
                CreateProvider(options, new TestClassifier(), bindings.Object);
            using IDisposable continuity = Acquire(provider, ResourceIsolationStage.ReassemblyBytes,
                provider.ClassifyReassembly(Channel("active", [1])), 1);
            using IDisposable ordinary = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 1);
            AssertRejected(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(11)), 1, ResourceIsolationFailureReason.OwnerTableFull);
            using IDisposable reconnect = Acquire(provider, ResourceIsolationStage.SessionEstablishment,
                provider.ClassifyConnection(Peer(2)), 1);
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(3));
        }

        [Test]
        public void ExplicitSharedClassificationIsNotPromotedBySessionMembership()
        {
            var classifier = new Mock<IResourceIsolationClassifier>();
            ResourceIsolationIdentity identity = new("shared-tenant", ResourceIsolationClass.Established);
            classifier.Setup(value => value.TryClassify(
                    It.IsAny<SecureChannelContext>(), It.IsAny<SessionBindingContext>(), out identity)).Returns(true);
            var bindings = new Mock<ISessionBindingProvider>();
            bindings.Setup(value => value.HasSession(It.IsAny<string>())).Returns(true);
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), classifier.Object,
                bindings.Object);
            SecureChannelContext context = Channel("active", [1]);
            ResourceIsolationOwner mapped = provider.Classify(context);

            Assert.That(provider.ClassifyReassembly(context), Is.SameAs(mapped));
            Assert.That(mapped.Class, Is.EqualTo(ResourceIsolationClass.Established));
        }

        [Test]
        public void SharedLogicalChannelCachesSeveralPeersWithoutCrossPeerOwnerReuse()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options());
            string channelId = new string("shared-https-listener".ToCharArray());
            var contexts = new SecureChannelContext[4];
            var owners = new ResourceIsolationOwner[4];
            for (int ii = 0; ii < contexts.Length; ii++)
            {
                contexts[ii] = new SecureChannelContext(channelId,
                    new EndpointDescription
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }, RequestEncoding.Json, peerAddress: Peer(ii + 1).Address);
                owners[ii] = provider.Classify(contexts[ii]);
            }
            for (int iteration = 0; iteration < 10; iteration++)
            {
                for (int ii = 0; ii < contexts.Length; ii++)
                {
                    Assert.That(provider.Classify(contexts[ii]), Is.SameAs(owners[ii]));
                }
            }
            Assert.That(owners[0].Key, Is.Not.EqualTo(owners[1].Key));
        }

        [Test]
        public void ReassemblyMembershipPromotionCannotGrantOtherResourceOrRatePrivileges()
        {
            var bindings = new Mock<ISessionBindingProvider>();
            bindings.Setup(value => value.HasSession("active")).Returns(true);
            using DefaultServerResourceIsolationProvider provider =
                CreateProvider(Options(), bindings: bindings.Object);
            SecureChannelContext channel = Channel("active", [1]);
            ResourceIsolationOwner promoted = provider.ClassifyReassembly(channel);
            Assert.That(promoted.Class, Is.EqualTo(ResourceIsolationClass.Reconnect));
            Assert.That(provider.Classify(channel).Class, Is.EqualTo(ResourceIsolationClass.Established));
            using IDisposable message = Acquire(provider, ResourceIsolationStage.ReassemblyBytes, promoted, 1);
            for (int index = 0; index <= (int)ResourceIsolationStage.ParkedRequest; index++)
            {
                var stage = (ResourceIsolationStage)index;
                if (stage != ResourceIsolationStage.ReassemblyBytes)
                {
                    AssertRejected(provider, stage, promoted, 1, ResourceIsolationFailureReason.InvalidOwner);
                }
            }
            using var rate = new ResourceIsolationConnectionRateLimiter(10, 10, provider);
            Assert.That(() => rate.TryAdmitConnection(Peer(10), promoted, out _),
                Throws.ArgumentException);
        }

        [Test]
        public void RevalidationDistinguishesLiveActivationChangeFromMissingSession()
        {
            var bindings = new TestBindings();
            var token = new NodeId(1);
            bindings.Bindings.Add(token, Binding(1, "user-a"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            ResourceIsolationOwner owner = provider.Classify(channel, token);
            Assert.That(provider.GetRevalidationStatus(owner, channel, token), Is.EqualTo(StatusCodes.Good));
            bindings.Bindings[token] = Binding(1, "user-a");

            Assert.That(provider.GetRevalidationStatus(owner, channel, token),
                Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(provider.IsCurrent(owner, channel, token), Is.False);
            bindings.Bindings.Clear();
            Assert.That(provider.GetRevalidationStatus(owner, channel, token),
                Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void SessionDerivedKeysAreCachedPerActivationNotChannelOrSequence()
        {
            var bindings = new TestBindings();
            bindings.Bindings.Add(new NodeId(1), Binding(1, "user-a"));
            bindings.Bindings.Add(new NodeId(2), Binding(2, "user-b"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            ResourceIsolationOwner first = provider.Classify(channel, new NodeId(1));
            ResourceIsolationOwner repeated = provider.Classify(channel, new NodeId(1));
            ResourceIsolationOwner other = provider.Classify(channel, new NodeId(2));

            Assert.That(repeated.Key, Is.SameAs(first.Key));
            Assert.That(other.Key, Is.Not.EqualTo(first.Key));
        }

        [Test]
        public void ClassificationCacheDoesNotKeepClosedChannelIdentifiersAlive()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options());
            WeakReference identifier = CreateUnrootedClassification(provider);
            for (int ii = 0; ii < 3 && identifier.IsAlive; ii++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.That(identifier.IsAlive, Is.False);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnrootedClassification(DefaultServerResourceIsolationProvider provider)
        {
            string id = Guid.NewGuid().ToString();
            _ = provider.Classify(Channel(id, [1, 2, 3]));
            return new WeakReference(id);
        }

        [Test]
        public void ClassificationCacheInvalidatesChangedCertificateAndMapping()
        {
            bool mapped = true;
            var classifier = new Mock<IResourceIsolationClassifier>();
            ResourceIsolationIdentity identity = new("approved", ResourceIsolationClass.Bootstrap);
            classifier.Setup(value => value.TryClassify(
                    It.IsAny<SecureChannelContext>(), It.IsAny<SessionBindingContext>(), out identity))
                .Returns(() => mapped);
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), classifier.Object);
            byte[] certificate = [1, 2, 3];
            SecureChannelContext context = Channel("channel", certificate);
            ResourceIsolationOwner first = provider.Classify(context);
            Assert.That(first.Class, Is.EqualTo(ResourceIsolationClass.Bootstrap));
            Assert.That(provider.Classify(context), Is.SameAs(first));

            certificate[0] = 9;
            ResourceIsolationOwner changed = provider.Classify(context);
            Assert.That(changed, Is.Not.SameAs(first));
            Assert.That(provider.IsCurrent(first, context), Is.False);
            mapped = false;
            ResourceIsolationOwner ordinary = provider.Classify(context);
            Assert.That(ordinary.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(provider.IsCurrent(changed, context), Is.False);
        }

        [Test]
        public void RevalidationRejectsMutatedChannelEvidenceWithoutTrustingAnIssuedOwner()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options());
            byte[] certificate = [1, 2, 3];
            SecureChannelContext channel = Channel("channel", certificate);
            ResourceIsolationOwner owner = provider.Classify(channel);
            Assert.That(provider.GetRevalidationStatus(owner, channel), Is.EqualTo(StatusCodes.Good));
            certificate[0] = 9;
            Assert.That(provider.GetRevalidationStatus(owner, channel),
                Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

#if NET8_0_OR_GREATER
        [Test]
        public void RepeatedClassificationReusesOwnerAndCertificateSnapshotAcrossFreshRequestContexts()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options());
            byte[] certificate = new byte[1500];
            SecureChannelContext firstContext = Channel("channel", certificate);
            ResourceIsolationOwner first = provider.Classify(firstContext);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool reused = true;
            for (int ii = 0; ii < 1000; ii++)
            {
                var next = new SecureChannelContext(
                    firstContext.SecureChannelId, firstContext.EndpointDescription, RequestEncoding.Binary,
                    certificate, peerAddress: firstContext.PeerAddress);
                reused &= ReferenceEquals(first, provider.Classify(next));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.Out.WriteLine($"Classification issuance allocated {allocated} bytes for 1000 fresh contexts.");
            Assert.That(reused, Is.True);
            Assert.That(allocated, Is.LessThan(256_000),
                "Issuing classification must not copy/hash the same 1500-byte certificate on every request.");
        }

        [Test]
        public void RevalidationDoesNotAllocateNewOwnerOrHashCertificateForEachCheck()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options());
            SecureChannelContext channel = Channel("channel", new byte[1500]);
            ResourceIsolationOwner owner = provider.Classify(channel);
            _ = provider.GetRevalidationStatus(owner, channel);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool valid = true;
            for (int ii = 0; ii < 1000; ii++)
            {
                valid &= StatusCode.IsGood(provider.GetRevalidationStatus(owner, channel));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(valid, Is.True);
            Assert.That(allocated, Is.LessThan(128_000),
                "Revalidation must not allocate another owner or certificate hash.");
        }
#endif

        [TestCase(ResourceIsolationStage.Connection)]
        [TestCase(ResourceIsolationStage.Handshake)]
        [TestCase(ResourceIsolationStage.ReassemblyBytes)]
        [TestCase(ResourceIsolationStage.SessionEstablishment)]
        [TestCase(ResourceIsolationStage.RequestQueue)]
        [TestCase(ResourceIsolationStage.RequestExecution)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes)]
        [TestCase(ResourceIsolationStage.ParkedRequest)]
        public void SameOwnerCannotBorrowProtectedFloors(ResourceIsolationStage stage)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(classifier: new TestClassifier());
            ResourceIsolationStagePlan plan = provider.Plan.GetStage(stage);
            ResourceIsolationOwner ordinary = provider.ClassifyConnection(Peer(10));
            using IDisposable shared = Acquire(provider, stage, ordinary, plan.SharedCapacity);
            AssertRejected(provider, stage, ordinary, 1, ResourceIsolationFailureReason.Capacity);
            using IDisposable bootstrap = Acquire(provider, stage,
                provider.ClassifyConnection(Peer(1)), plan.BootstrapReserved);
            using IDisposable reconnect = Acquire(provider, stage,
                provider.ClassifyConnection(Peer(2)), plan.ReconnectReserved);
            Assert.That(provider.GetUsage(stage), Is.EqualTo(plan.Capacity));
            AssertRejected(provider, stage, provider.ClassifyConnection(Peer(1)), 1,
                ResourceIsolationFailureReason.Capacity);
        }

        [Test]
        public void DistributedOwnersCannotBorrowHealthyReservedClass()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(classifier: new TestClassifier());
            var leases = new List<IDisposable>();
            try
            {
                for (int ii = 10; ii < 18; ii++)
                {
                    leases.Add(Acquire(provider, ResourceIsolationStage.Connection,
                        provider.ClassifyConnection(Peer(ii)), 1));
                }
                AssertRejected(provider, ResourceIsolationStage.Connection,
                    provider.ClassifyConnection(Peer(18)), 1, ResourceIsolationFailureReason.Capacity);
                using IDisposable bootstrap = Acquire(provider, ResourceIsolationStage.Connection,
                    provider.ClassifyConnection(Peer(1)), 1);
                using IDisposable reconnect = Acquire(provider, ResourceIsolationStage.Connection,
                    provider.ClassifyConnection(Peer(2)), 1);
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(10));
            }
            finally
            {
                foreach (IDisposable lease in leases)
                {
                    lease.Dispose();
                }
            }
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void RealTransportAdmissionSharesPolicyAcrossListenersAndProtectsExplicitIngressOnly()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(classifier: new TestClassifier());
            var first = new UaScConnectionAdmission(100, null, provider,
                timeProvider: new FakeTimeProvider());
            UaScConnectionAdmission second = first.CreateIndependentScope();
            try
            {
                for (int ii = 10; ii < 18; ii++)
                {
                    UaScConnectionAdmission listener = ii % 2 == 0 ? first : second;
                    Assert.That(listener.TryAcquire(Peer(ii), out _), Is.True);
                }
                Assert.That(first.TryAcquire(Peer(18), out _), Is.False);
                Assert.That(second.TryAcquire(null, out _), Is.False);
                Assert.That(first.TryAcquire(Peer(1), out UaScConnectionAdmission.Lease? bootstrap), Is.True);
                Assert.That(second.TryAcquire(Peer(2), out _), Is.True);
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(10));
                Assert.That(provider.GetUsage(ResourceIsolationStage.Handshake), Is.EqualTo(10));
                bootstrap!.CompleteHandshake();
                bootstrap.CompleteHandshake();
                Assert.That(provider.GetUsage(ResourceIsolationStage.Handshake), Is.EqualTo(9));
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(10));
                bootstrap.Dispose();
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(9));
                Assert.That(second.TryAcquire(Peer(19), out _), Is.False);
            }
            finally
            {
                try
                {
                    first.Stop();
                }
                finally
                {
                    second.Stop();
                }
            }
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Handshake), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly)]
        [TestCase(ServerResourceIsolationMode.FairShare)]
        public void SharedCapacityIsWorkConservingAndNeverExceedsTotal(ServerResourceIsolationMode mode)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider(mode);
            ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(10));
            using IDisposable lease = Acquire(provider, ResourceIsolationStage.Connection, owner, 10);
            Assert.That(provider.Plan.GetStage(ResourceIsolationStage.Connection).SharedCapacity, Is.EqualTo(10));
            AssertRejected(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(11)), 1, ResourceIsolationFailureReason.Capacity);
            lease.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
            using IDisposable borrowed = Acquire(provider, ResourceIsolationStage.Connection, owner, 10);
        }

        [Test]
        public void ConfiguredHardCeilingAppliesAcrossClassificationsAndLeases()
        {
            ServerResourceIsolationOptions options = Options(ServerResourceIsolationMode.FairShare);
            options.Stages[0].OwnerHardLimit = 4;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options);
            using IDisposable first = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 3);
            using IDisposable second = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(new IPEndPoint(Peer(10).Address, 4444)), 1);
            AssertRejected(provider, ResourceIsolationStage.Connection, provider.ClassifyConnection(Peer(10)), 1,
                ResourceIsolationFailureReason.OwnerLimit);
            using IDisposable other = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(11)), 4);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(8));
        }

        [Test]
        public void TrustedFloorsAreProvisionedSeparatelyAndWeightsComeFromPolicy()
        {
            ServerResourceIsolationOptions options = Options(ServerResourceIsolationMode.TrustedReservations);
            options.TrustedOwners =
            [
                new TrustedResourceOwnerOptions { Key = "tenant-a", Weight = 7 },
                new TrustedResourceOwnerOptions { Key = "tenant-b", Weight = 3 }
            ];
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, new TestClassifier());
            ResourceIsolationOwner first = provider.ClassifyConnection(Peer(3));
            ResourceIsolationOwner second = provider.ClassifyConnection(Peer(4));
            Assert.That(first.Weight, Is.EqualTo(7));
            Assert.That(second.Weight, Is.EqualTo(3));
            using IDisposable shared = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 6);
            using IDisposable trustedA = Acquire(provider, ResourceIsolationStage.Connection, first, 1);
            AssertRejected(provider, ResourceIsolationStage.Connection, first, 1,
                ResourceIsolationFailureReason.Capacity);
            using IDisposable trustedB = Acquire(provider, ResourceIsolationStage.Connection, second, 1);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(8));
        }

        [Test]
        public void RealSchedulerCannotLendReleasedProtectedExecutionCapacityToSharedWork()
        {
            ServerResourceIsolationOptions options = Options(ServerResourceIsolationMode.TrustedReservations);
            options.TrustedOwners = [new TrustedResourceOwnerOptions { Key = "tenant-a" }];
            options.Stages[(int)ResourceIsolationStage.RequestQueue].Capacity = 4;
            options.Stages[(int)ResourceIsolationStage.RequestQueue].OwnerHardLimit = 4;
            options.Stages[(int)ResourceIsolationStage.RequestExecution].Capacity = 4;
            options.Stages[(int)ResourceIsolationStage.RequestExecution].OwnerHardLimit = 4;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, new TestClassifier());
            using var queue = new FairRequestQueue(provider, 4, 10, false, (_, _) => { });
            Assert.That(queue.TryEnqueue(QueueRequest(10), default, out _), Is.True);
            Assert.That(queue.TryEnqueue(QueueRequest(11), default, out StatusCode rejected), Is.False);
            Assert.That(rejected, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(queue.TryEnqueue(QueueRequest(1), default, out _), Is.True);
            Assert.That(queue.TryEnqueue(QueueRequest(2), default, out _), Is.True);
            Assert.That(queue.TryEnqueue(QueueRequest(3), default, out _), Is.True);
            using FairRequestQueue.Entry shared = Dequeue(queue);
            using FairRequestQueue.Entry bootstrap = Dequeue(queue);
            using FairRequestQueue.Entry reconnect = Dequeue(queue);
            using FairRequestQueue.Entry trusted = Dequeue(queue);
            Assert.That(shared.Owner.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(bootstrap.Owner.Class, Is.EqualTo(ResourceIsolationClass.Bootstrap));
            Assert.That(reconnect.Owner.Class, Is.EqualTo(ResourceIsolationClass.Reconnect));
            Assert.That(trusted.Owner.Class, Is.EqualTo(ResourceIsolationClass.Trusted));
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(4));
            Assert.That(queue.TryEnqueue(QueueRequest(11), default, out _), Is.True);
            Assert.That(queue.TryDequeue(out _), Is.False);
            bootstrap.Dispose();
            Assert.That(queue.TryDequeue(out _), Is.False, "Released protected capacity is not shared capacity.");
            Assert.That(queue.TryEnqueue(QueueRequest(1), default, out _), Is.True);
            using FairRequestQueue.Entry nextBootstrap = Dequeue(queue);
            Assert.That(nextBootstrap.Owner.Class, Is.EqualTo(ResourceIsolationClass.Bootstrap));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(4));
        }

        [Test]
        public void TableSaturationPreservesProtectedSlotsAndNeverEvictsActiveOwners()
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = 3;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, new TestClassifier());
            using IDisposable ordinary = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 1);
            AssertRejected(provider, ResourceIsolationStage.Connection, provider.ClassifyConnection(Peer(11)), 1,
                ResourceIsolationFailureReason.OwnerTableFull);
            using IDisposable bootstrap = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(1)), 1);
            using IDisposable reconnect = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(2)), 1);
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(3));
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(3));
        }

        [Test]
        public void ChurnAndDoubleDisposeReleaseAllStagesWithoutEvictingActiveKey()
        {
            ServerResourceIsolationOptions options = Options(ServerResourceIsolationMode.FairShare);
            options.MaxTrackedOwners = 2;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options);
            ResourceIsolationOwner fixedOwner = provider.ClassifyConnection(Peer(1));
            using IDisposable fixedLease = Acquire(provider, ResourceIsolationStage.Connection, fixedOwner, 1);
            for (int ii = 0; ii < 1000; ii++)
            {
                ResourceIsolationOwner owner = provider.ClassifyConnection(
                    new IPEndPoint(IPAddress.Parse("192.0.2." + (ii % 250 + 2)), 1000));
                IDisposable lease = Acquire(provider, ResourceIsolationStage.Handshake, owner, 1);
                lease.Dispose();
                lease.Dispose();
                Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));
                Assert.That(provider.GetUsage(ResourceIsolationStage.Handshake), Is.Zero);
            }
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(1));
            fixedLease.Dispose();
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void ForgedOwnerAndAnotherProvidersClassificationAreRejected()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using DefaultServerResourceIsolationProvider other = CreateProvider();
            var forged = new ResourceIsolationOwner(
                "trusted", ResourceIsolationClass.Trusted, 100, new long[] { 10, 10, 100, 10, 10, 10, 100, 10 });
            AssertRejected(provider, ResourceIsolationStage.Connection, forged, 1,
                ResourceIsolationFailureReason.InvalidOwner);
            AssertRejected(provider, ResourceIsolationStage.Connection, other.ClassifyConnection(Peer(10)), 1,
                ResourceIsolationFailureReason.InvalidOwner);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void NoneCertificatesAndPortRotationDoNotGrantNewIdentityOrTrust()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            ResourceIsolationOwner first = provider.Classify(Channel("one", [1], MessageSecurityMode.None));
            ResourceIsolationOwner second = provider.Classify(Channel("two", [2], MessageSecurityMode.None));
            ResourceIsolationOwner peer = provider.ClassifyConnection(new IPEndPoint(Peer(10).Address, 54321));
            ResourceIsolationOwner mappedIpv6 = provider.ClassifyConnection(
                new IPEndPoint(Peer(10).Address.MapToIPv6(), 1234));
            Assert.That(first.Key, Is.EqualTo(second.Key));
            Assert.That(first.Key, Is.EqualTo(peer.Key));
            Assert.That(mappedIpv6.Key, Is.EqualTo(peer.Key));
            Assert.That(first.Class, Is.EqualTo(ResourceIsolationClass.Established));
        }

        [Test]
        public void ValidatedApplicationsBehindNatHaveIndependentSharedOwners()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            ResourceIsolationOwner first = provider.Classify(Channel("one", [1]));
            ResourceIsolationOwner second = provider.Classify(Channel("two", [2]));
            ResourceIsolationOwner repeated = provider.Classify(Channel("three", [1]));
            Assert.That(first.Key, Is.Not.EqualTo(second.Key));
            Assert.That(first.Key, Is.EqualTo(repeated.Key));
            Assert.That(first.Class, Is.EqualTo(ResourceIsolationClass.Established));
        }

        [Test]
        public void OnlyLiveVerifiedBindingsPromoteReconnectAndMultipleSessionsShareContinuityKey()
        {
            var bindings = new TestBindings();
            bindings.Bindings.Add(new NodeId(1), Binding(1, "user-a"));
            bindings.Bindings.Add(new NodeId(2), Binding(2, "user-a"));
            bindings.Bindings.Add(new NodeId(3), Binding(3, "user-b"));
            bindings.Bindings.Add(new NodeId(4), Binding(4, null));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(
                Options(), bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            ResourceIsolationOwner first = provider.Classify(channel, new NodeId(1), true);
            ResourceIsolationOwner second = provider.Classify(channel, new NodeId(2), true);
            ResourceIsolationOwner differentUser = provider.Classify(channel, new NodeId(3));
            ResourceIsolationOwner anonymous = provider.Classify(channel, new NodeId(4));
            ResourceIsolationOwner unknown = provider.Classify(channel, new NodeId(99), true);
            ResourceIsolationOwner wrongChannel = provider.Classify(Channel("wrong", [1]), new NodeId(1), true);
            Assert.That(first.Class, Is.EqualTo(ResourceIsolationClass.Reconnect));
            Assert.That(first.Key, Is.EqualTo(second.Key));
            Assert.That(first.Key, Is.Not.EqualTo(differentUser.Key));
            Assert.That(anonymous.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(unknown.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(wrongChannel.Class, Is.EqualTo(ResourceIsolationClass.Established));
            bindings.Bindings.Clear();
            Assert.That(provider.Classify(channel, new NodeId(1), true).Class,
                Is.EqualTo(ResourceIsolationClass.Established));
        }

        [TestCase(ResourceIsolationStage.RequestQueue)]
        [TestCase(ResourceIsolationStage.RequestExecution)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes)]
        [TestCase(ResourceIsolationStage.ParkedRequest)]
        public void VerifiedControlHasSeparateNonBorrowableCapacity(ResourceIsolationStage stage)
        {
            ServerResourceIsolationOptions options = Options();
            options.Stages[(int)stage].ControlReserved = null;
            var token = new NodeId(1);
            var bindings = new TestBindings();
            bindings.Bindings.Add(token, Binding(1, "user-a"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            ResourceIsolationStagePlan pool = provider.Plan.GetStage(stage);
            ResourceIsolationOwner ordinary = provider.Classify(channel, token);
            using IDisposable shared = Acquire(provider, stage, ordinary, pool.SharedCapacity);
            AssertRejected(provider, stage, ordinary, 1, ResourceIsolationFailureReason.Capacity);
            ResourceIsolationOwner control = provider.Classify(channel, token, controlRequest: true);
            Assert.That(control.Class, Is.EqualTo(ResourceIsolationClass.Control));
            Assert.That(provider.IsCurrent(control, channel, token, controlRequest: true), Is.True);
            using IDisposable protectedControl = Acquire(provider, stage, control, pool.ControlReserved);
            ResourceIsolationOwner reconnect = provider.Classify(channel, token, sessionEstablishment: true);
            using IDisposable protectedReconnect = Acquire(provider, stage, reconnect, pool.ReconnectReserved);
            ResourceIsolationOwner unknown = provider.Classify(channel, new NodeId(99), controlRequest: true);
            Assert.That(unknown.Class, Is.EqualTo(ResourceIsolationClass.Established));
            AssertRejected(provider, stage, unknown, 1, ResourceIsolationFailureReason.Capacity);
            protectedControl.Dispose();
            AssertRejected(provider, stage, ordinary, 1, ResourceIsolationFailureReason.Capacity);
            bindings.Bindings.Clear();
            Assert.That(provider.IsCurrent(control, channel, token, controlRequest: true), Is.False);
        }

        [Test]
        public void PromotingExistingKeyDoesNotLendItsProtectedTableSlot()
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = 4;
            options.Stages[(int)ResourceIsolationStage.RequestExecution].ControlReserved = 1;
            var bindings = new TestBindings();
            var token = new NodeId(1);
            bindings.Bindings.Add(token, Binding(1, "user-a"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            using IDisposable ordinary = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, token), 1);
            using IDisposable control = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, token, controlRequest: true), 1);
            AssertRejected(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyConnection(Peer(11)), 1, ResourceIsolationFailureReason.OwnerTableFull);
            control.Dispose();
            AssertRejected(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyConnection(Peer(12)), 1, ResourceIsolationFailureReason.OwnerTableFull);
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));
        }

        [Test]
        public void ReleasingFirstProtectedClassPreservesTableHeadroomForAnotherOwner()
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = 5;
            options.Stages[(int)ResourceIsolationStage.RequestExecution].ControlReserved = 1;
            var bindings = new TestBindings();
            var firstToken = new NodeId(1);
            var nextToken = new NodeId(2);
            bindings.Bindings.Add(firstToken, Binding(1, "user-a"));
            bindings.Bindings.Add(nextToken, Binding(2, "user-b"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            using IDisposable reconnect = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, firstToken, sessionEstablishment: true), 1);
            using IDisposable ordinary = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, firstToken), 1);
            using IDisposable secondOwner = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyConnection(Peer(11)), 1);
            AssertRejected(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyConnection(Peer(12)), 1, ResourceIsolationFailureReason.OwnerTableFull);
            reconnect.Dispose();
            using IDisposable nextReconnect = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, nextToken, sessionEstablishment: true), 1);
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(3));
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(3));
        }

        [Test]
        public void ClassChangeCannotConsumeLastProtectedTableSlot()
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = 4;
            options.Stages[(int)ResourceIsolationStage.RequestExecution].ControlReserved = 1;
            var bindings = new TestBindings();
            var token = new NodeId(1);
            bindings.Bindings.Add(token, Binding(1, "user-a"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            using IDisposable reconnect = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, token, sessionEstablishment: true), 1);
            using IDisposable ordinary = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyConnection(Peer(11)), 1);
            AssertRejected(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, token), 1, ResourceIsolationFailureReason.OwnerTableFull);
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(2));
            ordinary.Dispose();
            using IDisposable reclassified = Acquire(provider, ResourceIsolationStage.RequestExecution,
                provider.Classify(channel, token), 1);
            reconnect.Dispose();
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
        }

        [Test]
        public void RuntimePlanPreservesTotalsAndRejectsInvalidCapacityWithoutOverflow()
        {
            var options = Options();
            ApplicationConfiguration configuration = Configuration();
            ServerResourceIsolationPlan plan = options.CreateRuntimePlan(configuration, new ServerRateLimitOptions(),
                new ChunkReassemblyBudget(100, 50));
            Assert.That(plan.GetStage(ResourceIsolationStage.Connection).Capacity, Is.EqualTo(10));
            Assert.That(plan.MaxReassemblyBytes, Is.EqualTo(100));
            options.Stages[0].Capacity = 11;
            Assert.That(() => options.CreateRuntimePlan(configuration, new ServerRateLimitOptions()),
                Throws.ArgumentException);
            options.Stages[0].Capacity = 10;
            options.Stages[0].BootstrapReserved = long.MaxValue;
            Assert.That(() => options.CreateRuntimePlan(configuration, new ServerRateLimitOptions()),
                Throws.ArgumentException);
            options.Stages[0].BootstrapReserved = 1;
            ServerConfiguration server = configuration.ServerConfiguration!;
            server.MaxSessionCount = int.MaxValue;
            server.MaxChannelCount = int.MaxValue;
            Assert.That(() => options.CreateRuntimePlan(configuration, new ServerRateLimitOptions()),
                Throws.ArgumentException);
            Assert.That(plan.GetStage(ResourceIsolationStage.Connection).BootstrapReserved, Is.EqualTo(1));
        }

        [TestCase(ServerResourceIsolationMode.Balanced, 3)]
        [TestCase(ServerResourceIsolationMode.Balanced, 4)]
        [TestCase(ServerResourceIsolationMode.TrustedReservations, 5)]
        public void ReservedConnectionsCannotSilentlyReduceAdvertisedSessionCapacity(
            ServerResourceIsolationMode mode,
            int channels)
        {
            var options = new ServerResourceIsolationOptions { Mode = mode, MaxRetainedMessageBytes = 10 };
            if (mode == ServerResourceIsolationMode.TrustedReservations)
            {
                options.TrustedOwners = [new TrustedResourceOwnerOptions { Key = "tenant-a" }];
            }
            ApplicationConfiguration configuration = Configuration();
            configuration.ServerConfiguration!.MaxChannelCount = channels;
            Assert.That(() => options.CreateRuntimePlan(
                configuration, new ServerRateLimitOptions(), new ChunkReassemblyBudget(100)),
                Throws.ArgumentException.With.Message.Contains("MaxSessionCount plus one ordinary reconnect"));
        }

        [TestCase(ServerResourceIsolationMode.Balanced, 5)]
        [TestCase(ServerResourceIsolationMode.FairShare, 3)]
        [TestCase(ServerResourceIsolationMode.TrustedReservations, 6)]
        public void SharedConnectionsAtNPlusOneBoundaryPreserveAdvertisedCapacity(
            ServerResourceIsolationMode mode,
            int channels)
        {
            var options = new ServerResourceIsolationOptions { Mode = mode, MaxRetainedMessageBytes = 10 };
            if (mode == ServerResourceIsolationMode.TrustedReservations)
            {
                options.TrustedOwners = [new TrustedResourceOwnerOptions { Key = "tenant-a" }];
            }
            ApplicationConfiguration configuration = Configuration();
            configuration.ServerConfiguration!.MaxChannelCount = channels;
            ServerResourceIsolationPlan plan = options.CreateRuntimePlan(
                configuration, new ServerRateLimitOptions(), new ChunkReassemblyBudget(100));
            Assert.That(plan.MaxSessionCount, Is.EqualTo(2));
            Assert.That(plan.MaxChannelCount, Is.EqualTo(channels));
            Assert.That(plan.GetStage(ResourceIsolationStage.Connection).Capacity, Is.EqualTo(channels));
            Assert.That(plan.GetStage(ResourceIsolationStage.Connection).SharedCapacity, Is.EqualTo(3));
        }

        [Test]
        public void ExplicitConnectionFloorOptOutPreservesNPlusOneWithoutIncreasingTotal()
        {
            var options = new ServerResourceIsolationOptions
            {
                MaxRetainedMessageBytes = 10,
                Stages =
                [
                    new ResourceIsolationStageOptions
                    {
                        Stage = ResourceIsolationStage.Connection,
                        BootstrapReserved = 0,
                        ReconnectReserved = 0
                    }
                ]
            };
            ApplicationConfiguration configuration = Configuration();
            configuration.ServerConfiguration!.MaxChannelCount = 3;
            ServerResourceIsolationPlan plan = options.CreateRuntimePlan(
                configuration, new ServerRateLimitOptions(), new ChunkReassemblyBudget(100));
            Assert.That(plan.MaxSessionCount, Is.EqualTo(2));
            Assert.That(plan.MaxChannelCount, Is.EqualTo(3));
            Assert.That(plan.GetStage(ResourceIsolationStage.Connection).SharedCapacity, Is.EqualTo(3));
        }

        [Test]
        public void MissingOrUnprovisionedTrustedClassifierFailsExplicitly()
        {
            ServerResourceIsolationOptions options = Options(ServerResourceIsolationMode.TrustedReservations);
            options.TrustedOwners = [new TrustedResourceOwnerOptions { Key = "tenant-a" }];
            Assert.That(() => CreateProvider(options), Throws.ArgumentException);
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options, new TestClassifier());
            Assert.That(() => provider.ClassifyConnection(Peer(4)), Throws.InvalidOperationException);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void QueuedBindingIsInvalidatedByReactivationAndUnknownTokensDoNotBecomeTrusted()
        {
            var bindings = new TestBindings();
            var token = new NodeId(1);
            bindings.Bindings.Add(token, Binding(1, "user-a"));
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), bindings: bindings);
            SecureChannelContext channel = Channel("channel", [1]);
            ResourceIsolationOwner owner = provider.Classify(channel, token);
            Assert.That(provider.IsCurrent(owner, channel, token), Is.True);
            bindings.Bindings[token] = new SessionBindingContext(new NodeId(1), "channel", 2,
                UserTokenType.UserName, "user-a", SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt);
            Assert.That(provider.IsCurrent(owner, channel, token), Is.False);
            ResourceIsolationOwner current = provider.Classify(channel, token);
            Assert.That(provider.IsCurrent(current, channel, token), Is.True);
            bindings.Bindings.Clear();
            Assert.That(provider.IsCurrent(current, channel, token), Is.False);
        }

        [Test]
        public void ClassifierCannotObserveCertificateOnNoneChannel()
        {
            var classifier = new Mock<IResourceIsolationClassifier>(MockBehavior.Strict);
            ResourceIsolationIdentity identity = default;
            classifier.Setup(c => c.TryClassify(
                It.Is<SecureChannelContext>(c => c.ClientChannelCertificate == null),
                null, out identity)).Returns(false);
            classifier.Setup(c => c.TryClassifyIngress(It.IsAny<IPEndPoint>(), out identity)).Returns(false);
            using DefaultServerResourceIsolationProvider provider = CreateProvider(Options(), classifier.Object);
            Assert.That(provider.Classify(Channel("channel", [1, 2, 3], MessageSecurityMode.None)).Class,
                Is.EqualTo(ResourceIsolationClass.Established));
            classifier.VerifyAll();
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly)]
        [TestCase(ServerResourceIsolationMode.FairShare)]
        [TestCase(ServerResourceIsolationMode.Balanced)]
        public void DefaultOwnerCeilingAllowsWholeUnreservedPool(ServerResourceIsolationMode mode)
        {
            ServerResourceIsolationOptions options = Options(mode);
            options.Stages[0].OwnerHardLimit = null;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options);
            ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(10));
            ResourceIsolationStagePlan pool = provider.Plan.GetStage(ResourceIsolationStage.Connection);
            Assert.That(owner.GetHardLimit(ResourceIsolationStage.Connection), Is.EqualTo(pool.Capacity));
            Assert.That(provider.UseFairScheduling, Is.EqualTo(mode != ServerResourceIsolationMode.SharedOnly));
            using IDisposable first = Acquire(provider, ResourceIsolationStage.Connection, owner, pool.SharedCapacity);
            AssertRejected(provider, ResourceIsolationStage.Connection, owner, 1,
                mode == ServerResourceIsolationMode.Balanced
                    ? ResourceIsolationFailureReason.Capacity : ResourceIsolationFailureReason.OwnerLimit);
            AssertRejected(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(11)), 1, ResourceIsolationFailureReason.Capacity);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(pool.SharedCapacity));
            first.Dispose();
            using IDisposable next = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(11)), pool.SharedCapacity);
        }

        [Test]
        public void RuntimeBalancedFloorsReplaceLegacySessionlessPriority()
        {
            ServerResourceIsolationPlan plan = Options().CreateRuntimePlan(
                Configuration(), new ServerRateLimitOptions(), new ChunkReassemblyBudget(100, 0));
            Assert.That(plan.MaxBytesWithoutSession, Is.Zero);
            Assert.That(plan.BootstrapReservedBytes, Is.EqualTo(10));
            Assert.That(plan.ReconnectReservedBytes, Is.EqualTo(10));
            Assert.That(plan.UnreservedReassemblyBytes, Is.EqualTo(80));
        }

        [Test]
        public void ReferenceReassemblyFloorsFitOriginalTotalWithoutControlByteFloor()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 75, MaxChannelCount = 1000 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024, MaxBufferSize = 65536 }
            };
            ServerResourceIsolationPlan plan = new ServerResourceIsolationOptions().CreateRuntimePlan(
                configuration, new ServerRateLimitOptions(), new ChunkReassemblyBudget(64L * 1024 * 1024));
            ResourceIsolationStagePlan reassembly = plan.GetStage(ResourceIsolationStage.ReassemblyBytes);
            Assert.That(plan.MaxRetainedMessageBytes, Is.EqualTo(17039360));
            Assert.That(reassembly.Capacity, Is.EqualTo(67108864));
            Assert.That(reassembly.BootstrapReserved, Is.EqualTo(17039360));
            Assert.That(reassembly.ReconnectReserved, Is.EqualTo(17039360));
            Assert.That(reassembly.ControlReserved, Is.Zero);
            Assert.That(reassembly.SharedCapacity, Is.EqualTo(33030144));
            Assert.That(reassembly.SharedCapacity, Is.GreaterThanOrEqualTo(plan.MaxRetainedMessageBytes));
            Assert.That(plan.GetStage(ResourceIsolationStage.RequestQueueBytes).ControlReserved,
                Is.EqualTo(configuration.TransportQuotas.MaxMessageSize));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidAmountsNeverChangeAccounting(long amount)
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(10));
            Assert.That(() => provider.TryAcquire(ResourceIsolationStage.Connection, owner, amount, out _, out _),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void MaximumAmountCannotOverflowUsage()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            AssertRejected(provider, ResourceIsolationStage.Connection, provider.ClassifyConnection(Peer(10)),
                long.MaxValue, ResourceIsolationFailureReason.OwnerLimit);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
        }

        [Test]
        public async Task ConcurrentAdmissionAndDoubleReleaseRespectExactCapacityAsync()
        {
            using DefaultServerResourceIsolationProvider provider =
                CreateProvider(ServerResourceIsolationMode.FairShare);
            ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(10));
            var leases = new IDisposable?[40];
            var attempts = new Task[leases.Length];
            for (int ii = 0; ii < attempts.Length; ii++)
            {
                int index = ii;
                attempts[ii] = Task.Run(() => provider.TryAcquire(
                    ResourceIsolationStage.Connection, owner, 1, out leases[index], out _));
            }
            await Task.WhenAll(attempts).ConfigureAwait(false);
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.EqualTo(10));
            int admitted = 0;
            for (int ii = 0; ii < attempts.Length; ii++)
            {
                int index = ii;
                attempts[ii] = Task.Run(() =>
                {
                    if (leases[index] != null)
                    {
                        Interlocked.Increment(ref admitted);
                        leases[index]!.Dispose();
                        leases[index]!.Dispose();
                    }
                });
            }
            await Task.WhenAll(attempts).ConfigureAwait(false);
            Assert.That(admitted, Is.EqualTo(10));
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void CapacityNotificationRunsAfterReleaseAndOnlyOnce()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            int notifications = 0;
            provider.CapacityAvailable += _ =>
            {
                Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
                notifications++;
            };
            IDisposable lease = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 1);
            lease.Dispose();
            lease.Dispose();
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void InvalidOwnerTableAndWeightsRejectConfiguration(int value)
        {
            ServerResourceIsolationOptions options = Options();
            options.MaxTrackedOwners = value;
            Assert.That(() => CreateProvider(options), Throws.ArgumentException);
            options.MaxTrackedOwners = 4096;
            options.DefaultWeight = value;
            Assert.That(() => CreateProvider(options), Throws.ArgumentException);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(121)]
        public void InvalidHandshakeTimeoutIsRejectedBeforeStartup(int seconds)
        {
            ServerResourceIsolationOptions options = Options();
            options.HandshakeTimeout = TimeSpan.FromSeconds(seconds);
            Assert.That(() => CreateProvider(options), Throws.ArgumentException
                .With.Message.Contains(nameof(ServerResourceIsolationOptions.HandshakeTimeout)));
        }

        [Test]
        public void DuplicateStagesAndTrustedOwnersRejectConfiguration()
        {
            ServerResourceIsolationOptions options = Options();
            options.Stages = [new ResourceIsolationStageOptions(), new ResourceIsolationStageOptions()];
            Assert.That(() => CreateProvider(options), Throws.ArgumentException);
            options = Options(ServerResourceIsolationMode.TrustedReservations);
            options.TrustedOwners =
            [
                new TrustedResourceOwnerOptions { Key = "tenant-a" },
                new TrustedResourceOwnerOptions { Key = "tenant-a" }
            ];
            Assert.That(() => CreateProvider(options, new TestClassifier()), Throws.ArgumentException);
        }

        [Test]
        public void ExplicitZeroStageFloorsDisableGuaranteeWithoutIncreasingCapacity()
        {
            ServerResourceIsolationOptions options = Options();
            options.Stages[0].BootstrapReserved = 0;
            options.Stages[0].ReconnectReserved = 0;
            using DefaultServerResourceIsolationProvider provider = CreateProvider(options);
            Assert.That(provider.Plan.GetStage(ResourceIsolationStage.Connection).SharedCapacity, Is.EqualTo(10));
            using IDisposable lease = Acquire(provider, ResourceIsolationStage.Connection,
                provider.ClassifyConnection(Peer(10)), 10);
        }

        [Test]
        public void DisposalRejectsNewWorkButOutstandingLeasesStillRelease()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            ResourceIsolationOwner owner = provider.ClassifyConnection(Peer(10));
            IDisposable lease = Acquire(provider, ResourceIsolationStage.Connection, owner, 1);
            provider.Dispose();
            Assert.That(() => provider.TryAcquire(ResourceIsolationStage.Connection, owner, 1, out _, out _),
                Throws.TypeOf<ObjectDisposedException>());
            lease.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void StandardDefaultsProduceUsableBalancedRuntimePools()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            ServerResourceIsolationPlan plan = new ServerResourceIsolationOptions().CreateRuntimePlan(
                configuration, new ServerRateLimitOptions());
            Assert.That(plan.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            long queued = Math.Max(100, configuration.ServerConfiguration.MaxQueuedRequestCount);
            long executing = Math.Max(100, Math.Max(configuration.ServerConfiguration.MinRequestThreadCount,
                configuration.ServerConfiguration.MaxRequestThreadCount));
            Assert.That(plan.GetStage(ResourceIsolationStage.RequestQueueBytes).Capacity,
                Is.EqualTo((2 * queued + executing) * configuration.TransportQuotas.MaxMessageSize));
            for (int ii = 0; ii <= (int)ResourceIsolationStage.ParkedRequest; ii++)
            {
                var stage = (ResourceIsolationStage)ii;
                ResourceIsolationStagePlan pool = plan.GetStage(stage);
                long unit = stage == ResourceIsolationStage.ReassemblyBytes ? plan.MaxRetainedMessageBytes : 1;
                Assert.That(pool.BootstrapReserved, Is.GreaterThanOrEqualTo(unit));
                Assert.That(pool.ReconnectReserved, Is.GreaterThanOrEqualTo(unit));
                Assert.That(pool.SharedCapacity, Is.GreaterThanOrEqualTo(unit));
            }
        }

        [Test]
        public async Task ContextualSessionAdmissionPreservesGlobalLimiterAndReleasesBothLeasesAsync()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            var global = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            var globalLease = new Mock<IDisposable>();
            IDisposable? lease = globalLease.Object;
            TimeSpan? retryAfter = TimeSpan.FromSeconds(5);
            global.Setup(p => p.TryAcquireSessionEstablishment(out lease, out retryAfter)).Returns(true);
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationProvider = provider,
                RateLimiterProvider = global.Object
            };
            using IDisposable admitted = server.BeginSessionEstablishmentOrThrow(Channel("channel"), default) ??
                throw new InvalidOperationException("Expected a combined admission lease.");
            Assert.That(provider.GetUsage(ResourceIsolationStage.SessionEstablishment), Is.EqualTo(1));
            admitted.Dispose();
            admitted.Dispose();
            globalLease.Verify(l => l.Dispose(), Times.Once);
            Assert.That(provider.GetUsage(ResourceIsolationStage.SessionEstablishment), Is.Zero);
            lease = null;
            global.Setup(p => p.TryAcquireSessionEstablishment(out lease, out retryAfter)).Returns(false);
            Assert.That(() => server.BeginSessionEstablishmentOrThrow(Channel("channel"), default),
                Throws.TypeOf<ServerBusyException>().With.Property("StatusCode").EqualTo(StatusCodes.BadServerTooBusy)
                    .And.Property(nameof(ServerBusyException.RetryAfter)).EqualTo(retryAfter));
            Assert.That(provider.GetUsage(ResourceIsolationStage.SessionEstablishment), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public async Task IsolationRejectionDoesNotConsumeCustomGlobalPermitAsync()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            using IDisposable saturated = Acquire(provider, ResourceIsolationStage.SessionEstablishment,
                provider.Classify(Channel("channel")), 8);
            var global = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationProvider = provider,
                RateLimiterProvider = global.Object
            };
            Assert.That(() => server.BeginSessionEstablishmentOrThrow(Channel("channel"), default),
                Throws.TypeOf<ServerBusyException>());
            global.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ThrowingGlobalAdmissionReleasesIsolationLeaseAsync()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            var global = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            IDisposable? lease = null;
            TimeSpan? retryAfter = null;
            global.Setup(p => p.TryAcquireSessionEstablishment(out lease, out retryAfter))
                .Throws<InvalidOperationException>();
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationProvider = provider,
                RateLimiterProvider = global.Object
            };
            Assert.That(() => server.BeginSessionEstablishmentOrThrow(Channel("channel"), default),
                Throws.InvalidOperationException);
            Assert.That(provider.GetUsage(ResourceIsolationStage.SessionEstablishment), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public async Task ThrowingGlobalLeaseReleaseStillReleasesIsolationExactlyOnceAsync()
        {
            using DefaultServerResourceIsolationProvider provider = CreateProvider();
            var global = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            var globalLease = new Mock<IDisposable>(MockBehavior.Strict);
            globalLease.Setup(l => l.Dispose()).Throws<InvalidOperationException>();
            IDisposable? lease = globalLease.Object;
            TimeSpan? retryAfter = null;
            global.Setup(p => p.TryAcquireSessionEstablishment(out lease, out retryAfter)).Returns(true);
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationProvider = provider,
                RateLimiterProvider = global.Object
            };
            using IDisposable admitted = server.BeginSessionEstablishmentOrThrow(Channel("channel"), default) ??
                throw new InvalidOperationException("Expected a combined admission lease.");
            Assert.That(() => admitted.Dispose(), Throws.InvalidOperationException);
            admitted.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.SessionEstablishment), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
            globalLease.Verify(l => l.Dispose(), Times.Once);
        }

        [Test]
        public async Task HostingAndDirectStartupInstallConfiguredProviderWithoutReplacingCustomRateProviderAsync()
        {
            var services = new ServiceCollection();
            var classifier = new TestClassifier();
            services.AddOpcUa().AddServer(_ => { }).ConfigureResourceIsolation(o =>
                o.Mode = ServerResourceIsolationMode.FairShare);
            services.AddSingleton<IResourceIsolationClassifier>(classifier);
            using ServiceProvider container = services.BuildServiceProvider();
            OpcUaServerOptions options = container.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            IServerRateLimiterProvider rateProvider = Mock.Of<IServerRateLimiterProvider>();
            server.RateLimiterProvider = rateProvider;
            OpcUaServerHostedService.ApplyResourceIsolation(server, container, options);
            server.InitializeResourceIsolation(Configuration(), NUnitTelemetryContext.Create());
            Assert.That(server.ResourceIsolationClassifier, Is.SameAs(classifier));
            Assert.That(server.RateLimiterProvider, Is.SameAs(rateProvider));
            Assert.That(server.ResourceIsolationProvider, Is.TypeOf<DefaultServerResourceIsolationProvider>());
            Assert.That(((DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!).Plan.Mode,
                Is.EqualTo(ServerResourceIsolationMode.FairShare));
        }

        [Test]
        public void ConfigurationBindingReadsAdvancedIsolationAndRejectsMalformedValues()
        {
            var values = new Dictionary<string, string?>
            {
                ["Server:ResourceIsolation:Mode"] = "TrustedReservations",
                ["Server:ResourceIsolation:MaxTrackedOwners"] = "32",
                ["Server:ResourceIsolation:Stages:0:Stage"] = "Connection",
                ["Server:ResourceIsolation:Stages:0:OwnerHardLimit"] = "9",
                ["Server:ResourceIsolation:TrustedOwners:0:Key"] = "tenant-a",
                ["Server:ResourceIsolation:TrustedOwners:0:Weight"] = "4",
                ["Server:ResourceIsolation:TrustedOwners:0:Reservations:0:Stage"] = "Connection",
                ["Server:ResourceIsolation:TrustedOwners:0:Reservations:0:Reserved"] = "2"
            };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(configuration.GetSection("Server"));
            using ServiceProvider provider = services.BuildServiceProvider();
            ServerResourceIsolationOptions options =
                provider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value.ResourceIsolation;
            Assert.That(options.Mode, Is.EqualTo(ServerResourceIsolationMode.TrustedReservations));
            Assert.That(options.MaxTrackedOwners, Is.EqualTo(32));
            Assert.That(options.Stages[0].OwnerHardLimit, Is.EqualTo(9));
            Assert.That(options.TrustedOwners[0].Weight, Is.EqualTo(4));
            Assert.That(options.TrustedOwners[0].Reservations[0].Reserved, Is.EqualTo(2));
            configuration["Server:ResourceIsolation:Mode"] = "999";
            var invalid = new ServiceCollection();
            invalid.AddOpcUa().AddServer(configuration.GetSection("Server"));
            using ServiceProvider invalidProvider = invalid.BuildServiceProvider();
            Assert.That(() => invalidProvider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value,
                Throws.ArgumentException);
        }

        [Test]
        public async Task ExplicitDependencyInjectionProviderAndOptionsTakePrecedenceAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o => o.ResourceIsolation.Mode = ServerResourceIsolationMode.Balanced);
            services.Configure<ServerResourceIsolationOptions>(o => o.Mode = ServerResourceIsolationMode.FairShare);
            IServerResourceIsolationProvider custom = Mock.Of<IServerResourceIsolationProvider>();
            services.AddSingleton(custom);
            using ServiceProvider container = services.BuildServiceProvider();
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            OpcUaServerHostedService.ApplyResourceIsolation(server, container,
                container.GetRequiredService<IOptions<OpcUaServerOptions>>().Value);
            Assert.That(server.ResourceIsolationOptions.Mode, Is.EqualTo(ServerResourceIsolationMode.FairShare));
            Assert.That(server.ResourceIsolationProvider, Is.SameAs(custom));
            server.InitializeResourceIsolation(Configuration(), NUnitTelemetryContext.Create());
            Assert.That(server.ResourceIsolationProvider, Is.SameAs(custom));
        }

        [Test]
        public async Task SharedOnlyStartupPreservesUnlimitedLegacyConfigurationAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationOptions = new ServerResourceIsolationOptions
                {
                    Mode = ServerResourceIsolationMode.SharedOnly
                }
            };
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 0, MaxChannelCount = 0 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 0, MaxBufferSize = 0 }
            };
            server.InitializeResourceIsolation(configuration, NUnitTelemetryContext.Create());
            Assert.That(server.ResourceIsolationProvider, Is.Null);
            Assert.That(server.ChunkReassemblyBudget, Is.Null);
        }

        [Test]
        public async Task SharedOnlyStartupRetainsExplicitCustomProviderAndBudgetAsync()
        {
            IServerResourceIsolationProvider custom = Mock.Of<IServerResourceIsolationProvider>();
            var budget = new ChunkReassemblyBudget(100, 0);
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationOptions = new ServerResourceIsolationOptions
                {
                    Mode = ServerResourceIsolationMode.SharedOnly
                },
                ResourceIsolationProvider = custom,
                ChunkReassemblyBudget = budget
            };
            server.InitializeResourceIsolation(new ApplicationConfiguration(), NUnitTelemetryContext.Create());
            Assert.That(server.ResourceIsolationProvider, Is.SameAs(custom));
            Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
        }

        [Test]
        public async Task SwitchingOwnedProviderToSharedOnlyRemovesRuntimeIsolationAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create())
            {
                ResourceIsolationOptions = Options()
            };
            server.InitializeResourceIsolation(Configuration(), NUnitTelemetryContext.Create());
            var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            server.ResourceIsolationOptions.Mode = ServerResourceIsolationMode.SharedOnly;
            server.InitializeResourceIsolation(new ApplicationConfiguration(), NUnitTelemetryContext.Create());
            Assert.That(server.ResourceIsolationProvider, Is.Null);
            Assert.That(server.ChunkReassemblyBudget, Is.Null);
            Assert.That(() => previous.ClassifyConnection(Peer(10)), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task RestartRecreatesOwnedProviderAndDefaultBudgetFromNewConfigurationAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry)
            {
                RateLimitOptions = new ServerRateLimitOptions { MaxConcurrentSessionEstablishment = 32 }
            };
            ApplicationConfiguration firstConfiguration = Configuration();
            firstConfiguration.TransportQuotas!.MaxMessageSize = 4 * 1024 * 1024;
            firstConfiguration.TransportQuotas.MaxBufferSize = 65536;
            server.InitializeResourceIsolation(firstConfiguration, telemetry);
            var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            ChunkReassemblyBudget previousBudget = server.ChunkReassemblyBudget!;
            Assert.That(previous.Plan.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            Assert.That(previousBudget.MaxBytes, Is.EqualTo(64L * 1024 * 1024));

            await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
            server.ResourceIsolationOptions = new ServerResourceIsolationOptions
            {
                Mode = ServerResourceIsolationMode.FairShare,
                DefaultWeight = 3
            };
            server.RateLimitOptions.MaxConcurrentSessionEstablishment = 64;
            ApplicationConfiguration nextConfiguration = Configuration();
            nextConfiguration.ServerConfiguration!.MaxChannelCount = 20;
            nextConfiguration.TransportQuotas!.MaxMessageSize = 8 * 1024 * 1024;
            nextConfiguration.TransportQuotas.MaxBufferSize = 65536;
            server.InitializeResourceIsolation(nextConfiguration, telemetry);
            var current = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(current.Plan.Mode, Is.EqualTo(ServerResourceIsolationMode.FairShare));
            Assert.That(current.Plan.GetStage(ResourceIsolationStage.Connection).Capacity, Is.EqualTo(20));
            Assert.That(current.Plan.GetStage(ResourceIsolationStage.SessionEstablishment).Capacity, Is.EqualTo(64));
            Assert.That(current.ClassifyConnection(Peer(10)).Weight, Is.EqualTo(3));
            Assert.That(server.ChunkReassemblyBudget, Is.Not.SameAs(previousBudget));
            Assert.That(server.ChunkReassemblyBudget!.MaxBytes, Is.EqualTo(128L * 1024 * 1024));
            Assert.That(current.Plan.MaxReassemblyBytes, Is.EqualTo(128L * 1024 * 1024));
            Assert.That(previous.Plan.GetStage(ResourceIsolationStage.Connection).Capacity, Is.EqualTo(10));
            Assert.That(previous.Plan.GetStage(ResourceIsolationStage.SessionEstablishment).Capacity, Is.EqualTo(32));
            Assert.That(previousBudget.MaxBytes, Is.EqualTo(64L * 1024 * 1024));
            Assert.That(() => previous.ClassifyConnection(Peer(10)), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task RestartPreservesExplicitBudgetWhenRecreatingOwnedProviderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new ChunkReassemblyBudget(100, 50);
            await using var server = new StandardServer(telemetry)
            {
                ResourceIsolationOptions = Options(),
                ChunkReassemblyBudget = budget
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
            server.ResourceIsolationOptions.DefaultWeight = 2;
            server.InitializeResourceIsolation(Configuration(), telemetry);
            var current = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
            Assert.That(current.Plan.MaxReassemblyBytes, Is.EqualTo(100));
            Assert.That(current.ClassifyConnection(Peer(10)).Weight, Is.EqualTo(2));
            Assert.That(() => previous.ClassifyConnection(Peer(10)), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task RestartPreservesHostReplacementsAndDisposesOnlyOldOwnedProviderAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var custom = new Mock<IServerResourceIsolationProvider>(MockBehavior.Strict);
            Mock<IDisposable> disposable = custom.As<IDisposable>();
            var budget = new ChunkReassemblyBudget(200, 100);
            await using (var server = new StandardServer(telemetry) { ResourceIsolationOptions = Options() })
            {
                server.InitializeResourceIsolation(Configuration(), telemetry);
                var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
                await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                server.ResourceIsolationProvider = custom.Object;
                server.ChunkReassemblyBudget = budget;
                server.InitializeResourceIsolation(new ApplicationConfiguration(), telemetry);
                Assert.That(server.ResourceIsolationProvider, Is.SameAs(custom.Object));
                Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
                Assert.That(() => previous.ClassifyConnection(Peer(10)), Throws.TypeOf<ObjectDisposedException>());
            }
            disposable.Verify(d => d.Dispose(), Times.Never);
        }

        private static IDisposable Acquire(
            DefaultServerResourceIsolationProvider provider,
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount)
        {
            bool admitted = provider.TryAcquire(stage, owner, amount, out IDisposable? lease, out var failure);
            Assert.That(admitted, Is.True, failure.ToString());
            Assert.That(lease, Is.Not.Null);
            Assert.That(failure.Reason, Is.EqualTo(ResourceIsolationFailureReason.None));
            return lease!;
        }

        private static IEndpointIncomingRequest QueueRequest(int peer)
        {
            var request = new Mock<IEndpointIncomingRequest>();
            request.SetupGet(r => r.Request).Returns(new ReadRequest { RequestHeader = new RequestHeader() });
            request.SetupGet(r => r.SecureChannelContext).Returns(new SecureChannelContext(
                "channel-" + peer, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }, RequestEncoding.Binary, peerAddress: Peer(peer).Address));
            return request.Object;
        }

        private static FairRequestQueue.Entry Dequeue(FairRequestQueue queue)
        {
            Assert.That(queue.TryDequeue(out FairRequestQueue.Entry? entry), Is.True);
            return entry!;
        }

        private static void AssertRejected(
            DefaultServerResourceIsolationProvider provider,
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount,
            ResourceIsolationFailureReason expected)
        {
            Assert.That(provider.TryAcquire(stage, owner, amount, out IDisposable? lease, out var failure), Is.False);
            Assert.That(lease, Is.Null);
            Assert.That(failure.Reason, Is.EqualTo(expected));
            Assert.That(failure.RetryAfter, Is.GreaterThan(TimeSpan.Zero));
        }

        private static DefaultServerResourceIsolationProvider CreateProvider(
            ServerResourceIsolationMode mode = ServerResourceIsolationMode.Balanced,
            IResourceIsolationClassifier? classifier = null)
        {
            return CreateProvider(Options(mode), classifier);
        }

        private static DefaultServerResourceIsolationProvider CreateProvider(
            ServerResourceIsolationOptions options,
            IResourceIsolationClassifier? classifier = null,
            ISessionBindingProvider? bindings = null)
        {
            return new DefaultServerResourceIsolationProvider(
                options.CreateRuntimePlan(Configuration(), new ServerRateLimitOptions(),
                    new ChunkReassemblyBudget(100, 50)),
                NUnitTelemetryContext.Create(), bindings, classifier);
        }

        private static ServerResourceIsolationOptions Options(
            ServerResourceIsolationMode mode = ServerResourceIsolationMode.Balanced)
        {
            var stages = new ResourceIsolationStageOptions[8];
            for (int ii = 0; ii < stages.Length; ii++)
            {
                stages[ii] = new ResourceIsolationStageOptions
                {
                    Stage = (ResourceIsolationStage)ii,
                    Capacity = ii is (int)ResourceIsolationStage.ReassemblyBytes or
                        (int)ResourceIsolationStage.RequestQueueBytes ? 100 : 10,
                    OwnerHardLimit = ii is (int)ResourceIsolationStage.ReassemblyBytes or
                        (int)ResourceIsolationStage.RequestQueueBytes ? 100 : 10,
                    ControlReserved = 0
                };
            }
            return new ServerResourceIsolationOptions
            {
                Mode = mode,
                MaxRetainedMessageBytes = 10,
                Stages = stages
            };
        }

        private static ApplicationConfiguration Configuration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 2, MaxChannelCount = 10 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 10, MaxBufferSize = 8192 }
            };
        }

        private static IPEndPoint Peer(int suffix)
        {
            return new IPEndPoint(IPAddress.Parse("192.0.2." + suffix), 1234);
        }

        private static SecureChannelContext Channel(
            string id,
            byte[]? certificate = null,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            return new SecureChannelContext(id, new EndpointDescription
            {
                SecurityMode = mode,
                SecurityPolicyUri = mode == MessageSecurityMode.None ? SecurityPolicies.None :
                    SecurityPolicies.Basic256Sha256
            }, RequestEncoding.Binary, certificate, peerAddress: Peer(10).Address);
        }

        private static SessionBindingContext Binding(uint id, string? user)
        {
            return new SessionBindingContext(new NodeId(id), "channel", 1,
                user == null ? UserTokenType.Anonymous : UserTokenType.UserName, user,
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt);
        }

        private sealed class TestBindings : ISessionBindingProvider
        {
            public Dictionary<NodeId, SessionBindingContext> Bindings { get; } = [];

            public bool HasSession(string secureChannelId)
            {
                return false;
            }

            public bool TryGetSessionContext(NodeId authenticationToken, SecureChannelContext channelContext,
                [NotNullWhen(true)] out SessionBindingContext? context)
            {
                if (Bindings.TryGetValue(authenticationToken, out SessionBindingContext? binding) &&
                    binding.SecureChannelId == channelContext.SecureChannelId)
                {
                    context = binding;
                    return true;
                }
                context = null;
                return false;
            }
        }

        private sealed class TestClassifier : IResourceIsolationClassifier
        {
            public bool TryClassifyIngress(IPEndPoint? remoteEndpoint, out ResourceIsolationIdentity identity)
            {
                string? address = remoteEndpoint?.Address.ToString();
                identity = address switch
                {
                    "192.0.2.1" => new("bootstrap", ResourceIsolationClass.Bootstrap),
                    "192.0.2.2" => new("reconnect", ResourceIsolationClass.Reconnect),
                    "192.0.2.3" => new("tenant-a", ResourceIsolationClass.Trusted),
                    "192.0.2.4" => new("tenant-b", ResourceIsolationClass.Trusted),
                    _ => default
                };
                return identity.Key != null;
            }

            public bool TryClassify(SecureChannelContext channelContext, SessionBindingContext? sessionBinding,
                out ResourceIsolationIdentity identity)
            {
                identity = default;
                return false;
            }
        }
    }
}
