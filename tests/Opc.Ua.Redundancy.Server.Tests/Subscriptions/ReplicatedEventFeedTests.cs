// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Exercises the hosted event registration against two independently created replica runtimes.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedEventFeedTests
    {
        [Test]
        public async Task HostedSuccessorCatchesUpBeforeServingAndCommitsAckAndConfirmAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var leaseClock = new FakeTimeProvider();
            using var storage = new InMemorySharedKeyValueStore(leaseClock);
            using var payload = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            var key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool pauseCommit = false;
            var view = new FreshnessProtectedKeyValueStore(payload, CreateAuthority(storage, BeforeCommit), protector,
                Guid.NewGuid(), Guid.NewGuid());
            await view.EnrollAsync().ConfigureAwait(false);
            Uuid group = Uuid.NewUuid();
            await using var a = new SharedStoreLeaseElection(view, "lease/events", "a", ["a", "b"],
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), leaseClock);
            await using var b = new SharedStoreLeaseElection(view, "lease/events", "b", ["a", "b"],
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), leaseClock);
            using var serviceA = new LeaderServiceLevelProvider(a, RedundancySupport.HotAndMirrored);
            using var serviceB = new LeaderServiceLevelProvider(b, RedundancySupport.HotAndMirrored);
            await using ServiceProvider scopeA = CreateScope(view, a, serviceA, group, telemetry);
            await using ServiceProvider scopeB = CreateScope(view, b, serviceB, group, telemetry);
            var policy = (Source)scopeB.GetRequiredService<IReplicatedEventSource>();
            var writer = scopeA.GetRequiredService<ReplicatedEventFeed>();
            var standby = scopeB.GetRequiredService<ReplicatedEventFeed>();
            IServerInternal serverA = CreateServer(telemetry);
            var audits = new List<AuditEventState>();
            IServerInternal serverB = CreateServer(telemetry, audits);
            AcknowledgeableConditionState conditionA = CreateCondition(serverA.DefaultSystemContext);
            AcknowledgeableConditionState conditionB = CreateCondition(serverB.DefaultSystemContext);
            writer.BindCondition(conditionA);
            standby.BindCondition(conditionB);
            var notifierA = new BaseObjectState(null) { NodeId = new NodeId(90) };
            var notifierB = new BaseObjectState(null) { NodeId = notifierA.NodeId };
            writer.BindNotifier(notifierA);
            standby.BindNotifier(notifierB);
            var events = new List<ByteString>();
            notifierA.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState { EventId: { } id })
                {
                    events.Add(id.Value);
                }
            };
            await StartAsync(scopeA, serverA).ConfigureAwait(false);
            await StartAsync(scopeB, serverB).ConfigureAwait(false);
            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            await writer.CatchUpAsync(true, default).ConfigureAwait(false);
            Assert.That(writer.IsReady, Is.True);

            Uuid occurrence = Uuid.NewUuid();
            ByteString ordinary = await writer.PublishEventAsync(notifierA.NodeId, occurrence, 7).ConfigureAwait(false);
            Assert.That(await writer.PublishEventAsync(notifierA.NodeId, occurrence, 7).ConfigureAwait(false),
                Is.EqualTo(ordinary));
            Assert.That(events, Is.EqualTo(new[] { ordinary }));
            var branch = new NodeId(51);
            ByteString old = await writer.PublishConditionAsync(conditionA.NodeId, branch, Uuid.NewUuid(), 1)
                .ConfigureAwait(false);
            ByteString current = await writer.PublishConditionAsync(conditionA.NodeId, branch, Uuid.NewUuid(), 2)
                .ConfigureAwait(false);
            await standby.CatchUpAsync(false, default).ConfigureAwait(false);
            Assert.That(conditionB.GetBranch(current), Is.Not.Null);
            Assert.That(serviceB.GetServiceLevel(), Is.EqualTo(ServiceLevels.NoData));
            ByteString latest = await writer.PublishConditionAsync(conditionA.NodeId, branch, Uuid.NewUuid(), 3)
                .ConfigureAwait(false);

            Volatile.Write(ref pauseCommit, true);
            Task<ByteString> pending = writer.PublishEventAsync(notifierA.NodeId, Uuid.NewUuid(), 8).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                leaseClock.Advance(TimeSpan.FromSeconds(10));
                Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
                Assert.That(writer.IsReady, Is.False);
                Assert.That(standby.IsReady, Is.False, "An election by itself does not validate the feed checkpoint.");
                serviceB.SetServiceLevel(ServiceLevels.Maximum);
                Assert.That(serviceB.GetServiceLevel(), Is.EqualTo(ServiceLevels.NoData));
                serviceB.SetServiceLevel(ServiceLevels.Maintenance);
                Assert.That(serviceB.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maintenance));
                serviceB.ClearServiceLevelOverride();
                await standby.CatchUpAsync(false, default).ConfigureAwait(false);
                Assert.That(standby.IsReady, Is.True);
                Assert.That(conditionB.GetBranch(latest), Is.Not.Null);
            }
            finally
            {
                Volatile.Write(ref pauseCommit, false);
                release.TrySetResult(true);
            }
            await Assert.ThatAsync(() => pending,
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotWritable)).ConfigureAwait(false);
            Assert.That(events, Is.EqualTo(new[] { ordinary }));

            ByteString disabled = await standby.PublishConditionAsync(conditionB.NodeId, branch, Uuid.NewUuid(), 0)
                .ConfigureAwait(false);
            Assert.That(await CallAsync(conditionB.Acknowledge!, disabled).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadConditionDisabled));
            Assert.That(await CallAsync(conditionB.Confirm!, disabled).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadConditionDisabled));
            Assert.That(conditionB.GetBranch(disabled), Is.Not.Null);
            Assert.That(policy.MethodCalls, Is.Empty);

            ByteString enabled = await standby.PublishConditionAsync(conditionB.NodeId, branch, Uuid.NewUuid(), 4)
                .ConfigureAwait(false);
            var requestedComment = new LocalizedText("en-US", "operator comment");
            policy.Authorization = StatusCodes.BadUserAccessDenied;
            Assert.That(await CallAsync(conditionB.Acknowledge!, old, requestedComment).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(policy.MethodCalls.Single(), Is.EqualTo((
                conditionB.GetBranch(enabled), ReplicatedConditionMethod.Acknowledge, old, requestedComment)));
            Assert.That(conditionB.GetBranch(enabled), Is.Not.Null);

            policy.ThrowOnAuthorize = true;
            Assert.That(await CallAsync(conditionB.Confirm!, enabled).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(conditionB.GetBranch(enabled), Is.Not.Null);
            policy.ThrowOnAuthorize = false;
            policy.Authorization = ServiceResult.Good;

            StatusCode ack = await CallAsync(conditionB.Acknowledge!, old).ConfigureAwait(false);
            Assert.That(ack, Is.EqualTo(StatusCodes.Good));
            var acked = (AcknowledgeableConditionState)conditionB.GetBranches().Values.Single();
            Assert.That(acked.AckedState!.Id!.Value, Is.True);
            ByteString acknowledgedId = acked.EventId!.Value;
            StatusCode confirm = await CallAsync(conditionB.Confirm!, acknowledgedId).ConfigureAwait(false);
            Assert.That(confirm, Is.EqualTo(StatusCodes.Good));
            Assert.That(conditionB.GetBranchCount(), Is.Zero);
            Assert.That(audits, Has.Count.EqualTo(6));
            Assert.That(audits[^2], Is.TypeOf<AuditConditionAcknowledgeEventState>());
            Assert.That(audits[^1], Is.TypeOf<AuditConditionConfirmEventState>());
            await writer.CatchUpAsync(false, default).ConfigureAwait(false);
            Assert.That(conditionA.GetBranchCount(), Is.Zero);

            async Task<StatusCode> CallAsync(MethodState method, ByteString eventId, LocalizedText comment = default)
            {
                ServiceResult result = await method.CallAsync(serverB.DefaultSystemContext, conditionB.NodeId,
                    [new Variant(eventId), new Variant(comment)], [], [])
                    .ConfigureAwait(false);
                return result?.StatusCode ?? StatusCodes.Good;
            }

            async ValueTask BeforeCommit(string recordKey, CancellationToken ct)
            {
                if (recordKey.Contains("/generation/event-feed/", StringComparison.Ordinal) &&
                    Volatile.Read(ref pauseCommit))
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            }
        }

        private static IDeadlineGuardedSharedKeyValueStore CreateAuthority(
            InMemorySharedKeyValueStore backend, Func<string, CancellationToken, ValueTask> beforeCommit)
        {
            var mock = new Mock<IDeadlineGuardedSharedKeyValueStore>(MockBehavior.Strict);
            Mock<ISharedKeyValueStoreConsistency> consistency = mock.As<ISharedKeyValueStoreConsistency>();
            consistency.Setup(x => x.IsProcessLocal(It.IsAny<string>())).Returns(false);
            consistency.Setup(x => x.IsLinearizable(It.IsAny<string>())).Returns(true);
            mock.SetupGet(x => x.SupportsGuardedDeadlines).Returns(true);
            mock.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            mock.Setup(x => x.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString previous, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, previous, value, ct));
            mock.Setup(x => x.CompareAndSwapBeforeAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<string>(),
                    It.IsAny<ByteString>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, ByteString previous, ByteString value, string guard, ByteString grant,
                    DateTimeOffset deadline, CancellationToken ct) =>
                {
                    await beforeCommit(key, ct).ConfigureAwait(false);
                    return await backend.CompareAndSwapBeforeAsync(key, previous, value, guard, grant, deadline, ct)
                        .ConfigureAwait(false);
                });
            return mock.Object;
        }

        private static ServiceProvider CreateScope(
            FreshnessProtectedKeyValueStore store,
            ILeaderElection election,
            LeaderServiceLevelProvider serviceLevel,
            Uuid group,
            ITelemetryContext telemetry)
        {
            var builder = new DiTestServerBuilder();
            builder.Services
                .AddSingleton(store)
                .AddSingleton(election)
                .AddSingleton<IServiceLevelProvider>(serviceLevel)
                .AddSingleton<TimeProvider>(new FakeTimeProvider())
                .AddSingleton(telemetry)
                .AddSingleton(new ServerRedundancyOptions { Mode = RedundancySupport.HotAndMirrored });
            builder.UseReplicatedEventReporting(group, _ => new Source());
            return builder.Services.BuildServiceProvider();
        }

        private static async Task StartAsync(ServiceProvider scope, IServerContext server)
        {
            await scope.GetRequiredService<IServerPreStartupTask>().OnServerStartingAsync(server)
                .ConfigureAwait(false);
            await scope.GetRequiredService<IServerStartupTask>().OnServerStartedAsync(server)
                .ConfigureAwait(false);
        }

        private static IServerInternal CreateServer(ITelemetryContext telemetry, List<AuditEventState>? audits = null)
        {
            var server = new Mock<IServerInternal>();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:example:events");
            server.SetupGet(x => x.MessageContext).Returns(messageContext);
            server.SetupGet(x => x.Telemetry).Returns(telemetry);
            server.SetupGet(x => x.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.SetupGet(x => x.ServerUris).Returns(messageContext.ServerUris);
            server.SetupGet(x => x.Factory).Returns(messageContext.Factory);
            server.SetupGet(x => x.TypeTree).Returns(new TypeTable(messageContext.NamespaceUris));
            server.SetupGet(x => x.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            if (audits != null)
            {
                server.SetupGet(x => x.Auditing).Returns(true);
                server.Setup(x => x.ReportEventAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Callback((ISystemContext _, IFilterTarget target, CancellationToken _) =>
                    {
                        if (target is AuditEventState audit)
                        {
                            audits.Add(audit);
                        }
                    });
            }
            return server.Object;
        }

        private static AcknowledgeableConditionState CreateCondition(ISystemContext context)
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.ConfirmedState = new TwoStateVariableState(condition);
            condition.Confirm = new AddCommentMethodState(condition);
            condition.Create(context, new NodeId(50), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(context, true);
            condition.SetConfirmedState(context, false);
            return condition;
        }

        private sealed class Source : IReplicatedEventSource
        {
            public ServiceResult Authorization { get; set; } = ServiceResult.Good;

            public bool ThrowOnAuthorize { get; set; }

            public List<(ConditionState, ReplicatedConditionMethod, ByteString, LocalizedText)> MethodCalls { get; }
                = [];

            public BaseEventState CreateEvent(ISystemContext context, NodeState notifier, in Variant payload)
            {
                var state = new BaseEventState(null);
                state.Initialize(context, notifier, EventSeverity.Low, LocalizedText.From(payload.ToString()));
                return state;
            }

            public void ApplyCondition(
                ISystemContext context, AcknowledgeableConditionState condition, in Variant payload)
            {
                condition.SetEnableState(context, payload.TryGetValue(out int state) && state != 0);
                condition.SetConfirmedState(context, false);
                condition.SetAcknowledgedState(context, false, clearChangeMasks: false, updateRetainState: true);
                condition.Retain!.Value = true;
            }

            public ServiceResult AuthorizeConditionMethod(
                ISystemContext context, AcknowledgeableConditionState condition,
                ReplicatedConditionMethod method, ByteString eventId, LocalizedText comment)
            {
                MethodCalls.Add((condition, method, eventId, comment));
                return ThrowOnAuthorize ? throw new InvalidOperationException("Test policy failure.") : Authorization;
            }
        }
    }
}
