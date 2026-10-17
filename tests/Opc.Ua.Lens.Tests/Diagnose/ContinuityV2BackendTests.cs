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
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.Continuity;
using UaLens.Telemetry;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuityV2BackendTests
{
    [Test]
    public async Task DurabilityIsSetBeforeItemsAndStopNeverDisposesThePrimary()
    {
        var context = new ContinuityStackTestContext("primary");
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(() => context.Session.Object, timeline, context.Telemetry);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(durable: true), CancellationToken.None).ConfigureAwait(false);

            Assert.That(context.Trace.Take(3), Is.EqualTo(s_durableInitialization));
            Assert.That(context.Subscriptions[0].DurabilityBeforeItems, Is.True);
            Assert.That(context.LastOptions!.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1000)));
            Assert.That(context.LastOptions.MaxMonitoredItemsPerPartition, Is.EqualTo(16));
            Assert.That(backend.CaptureDiagnostics().ToList()
                .Single(row => row.Name == "Durable lifetime requested / revised").Value,
                Is.EqualTo("1 h / 2 h"));
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Durable),
                Is.True);

            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(context.Subscriptions[0].Disposed, Is.True);
            context.Session.Verify(value => value.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()), Times.Never);
            context.Session.Verify(value => value.Dispose(), Times.Never);
            context.Session.Verify(value => value.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task DeniedDurabilityDoesNotAddItemsOrPretendTheRunStarted()
    {
        var context = new ContinuityStackTestContext("primary") { DenyDurability = true };
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(() => context.Session.Object, timeline, context.Telemetry);
        await using (backend.ConfigureAwait(false))
        {
            await Assert.ThatAsync(
                () => backend.StartAsync(Configuration(durable: true), CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(context.Trace, Does.Not.Contain("Item"));
            Assert.That(context.Subscriptions[0].Items, Is.Empty);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Durable),
                Is.False);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DeliberateRecreationTouchesOnlyTheOwnedSubscription()
    {
        var context = new ContinuityStackTestContext("primary");
        var unrelated = new Mock<ISubscription>();
        context.Unrelated.Add(unrelated.Object);
        var backend = new V2ContinuityBackend(
            () => context.Session.Object, new ContinuityTimeline(), context.Telemetry);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(
                Configuration(ContinuityScenario.RecreateOwnedSubscription), CancellationToken.None)
                .ConfigureAwait(false);
            ContinuityStepResult result = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Description, Does.Contain("recreation completed"));
            Assert.That(context.Trace.Count(value => value == "Recreate"), Is.EqualTo(1));
            unrelated.Verify(value => value.RecreateAsync(It.IsAny<CancellationToken>()), Times.Never);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
            unrelated.Verify(value => value.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task TransferOnLoadSavesOnlyOwnedStateAndObservesTheV2TransferCallback()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target") { TransferSucceeds = true };
        var sessions = new ContinuityTestSessions(source, target);
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, timeline, primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(ContinuityScenario.TransferOnLoad), CancellationToken.None)
                .ConfigureAwait(false);
            ContinuityStepResult result = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.SavedSubscriptions, Has.Count.EqualTo(1));
            Assert.That(source.SavedSubscriptions[0], Is.SameAs(source.Subscriptions[0].Subscription.Object));
            Assert.That(sessions.SourceLease.RetainOnClose, Is.EqualTo(s_retainedOnClose));
            Assert.That(sessions.Opened,
                Is.EqualTo(new[] { ContinuitySessionPurpose.Source, ContinuitySessionPurpose.Restore }));
            Assert.That(target.TransferRequested, Is.True);
            Assert.That(result.Description, Does.Contain("Transfer transition observed"));
            Assert.That(timeline.Snapshot().Counters.TransferObservations, Is.EqualTo(1));
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Recreated),
                Is.False);
            Assert.That(primary.Subscriptions, Is.Empty);

            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sessions.TargetLease.RetainOnClose, Is.EqualTo(s_deletedOnClose));
            Assert.That(target.Subscriptions[0].Disposed, Is.True);
            primary.Session.Verify(value => value.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task TransferFallbackIsRecreatedAndDurabilityIsReestablishedBeforeFreshItems()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target") { TransferSucceeds = false };
        var sessions = new ContinuityTestSessions(source, target);
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, timeline, primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(
                Configuration(ContinuityScenario.TransferOnLoad, durable: true), CancellationToken.None)
                .ConfigureAwait(false);
            ContinuityStepResult result = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(target.TransferRequested, Is.True);
            Assert.That(target.Subscriptions, Has.Count.EqualTo(2));
            Assert.That(target.Subscriptions[0].Disposed, Is.True);
            Assert.That(target.Subscriptions[1].DurabilityBeforeItems, Is.True);
            Assert.That(target.Trace.TakeLast(3), Is.EqualTo(s_durableInitialization));
            Assert.That(result.Description, Does.Contain("Recreation observed"));
            Assert.That(timeline.Snapshot().Counters.TransferObservations, Is.Zero);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row =>
                row.Kind == ContinuityEvidenceKind.Recreated &&
                row.Detail.Contains("fallback", StringComparison.Ordinal)), Is.True);
            await Assert.ThatAsync(() => backend.StopAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row =>
                row.Kind == ContinuityEvidenceKind.CleanupUncertain),
                Is.True);
        }
    }

    [Test]
    public async Task RecreateOnLoadDeletesSourceAndPassesTransferFalse()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target");
        var sessions = new ContinuityTestSessions(source, target);
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, new ContinuityTimeline(), primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(ContinuityScenario.RecreateOnLoad), CancellationToken.None)
                .ConfigureAwait(false);
            await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.Subscriptions[0].Disposed, Is.True);
            source.Session.Verify(value => value.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(sessions.SourceLease.RetainOnClose, Is.EqualTo(s_deletedOnClose));
            Assert.That(target.TransferRequested, Is.False);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GracefulRestoreWaitsForTheUserAndNeverRunsAProcess()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target") { TransferSucceeds = true };
        var sessions = new ContinuityTestSessions(source, target);
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, new ContinuityTimeline(), primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(ContinuityScenario.GracefulDurableRestore), CancellationToken.None)
                .ConfigureAwait(false);
            ContinuityStepResult waiting = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(waiting.WaitingForRestore, Is.True);
            Assert.That(waiting.Description, Does.Contain("yourself"));
            Assert.That(sessions.Opened, Is.EqualTo(new[] { ContinuitySessionPurpose.Source }));
            Assert.That(target.TransferRequested, Is.Null);

            ContinuityStepResult restored = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(restored.WaitingForRestore, Is.False);
            Assert.That(target.TransferRequested, Is.True);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task AdvancedScenariosRequireConfigurationAndIssuedTokensCannotUseQuickstartsPersistence()
    {
        var primary = new ContinuityStackTestContext("primary");
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, new ContinuityTimeline(), primary.Telemetry);
        await using (backend.ConfigureAwait(false))
        {
            Assert.That(backend.CheckSetup(Configuration(ContinuityScenario.TransferOnLoad)).Availability,
                Is.EqualTo(ContinuityAvailability.RequiresConfiguration));
            Assert.That(backend.CheckSetup(Configuration(ContinuityScenario.ConfiguredFailover)).Description,
                Does.Contain("IContinuitySessionFactory"));
            primary.Identity.SetupGet(value => value.TokenType).Returns(UserTokenType.IssuedToken);
            ContinuitySetup setup = backend.CheckSetup(Configuration(ContinuityScenario.GracefulDurableRestore));
            Assert.That(setup.Availability, Is.EqualTo(ContinuityAvailability.Unsupported));
            Assert.That(setup.Description, Does.Contain("issued-token"));
            Assert.That(primary.Subscriptions, Is.Empty);
        }
    }

    [Test]
    public async Task DirectBackendDisposalReleasesResourcesBeforeDetachingFromTheBorrowedSession()
    {
        var context = new ContinuityStackTestContext("primary");
        var backend = new V2ContinuityBackend(
            () => context.Session.Object, new ContinuityTimeline(), context.Telemetry);
        await backend.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);

        await backend.DisposeAsync().ConfigureAwait(false);
        await backend.DisposeAsync().ConfigureAwait(false);

        Assert.That(context.Subscriptions[0].Disposed, Is.True);
        context.Session.Verify(value => value.DeleteSubscriptionsAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Session.Verify(value => value.DisposeAsync(), Times.Never);
    }

    [Test]
    public async Task HiddenStackDeleteOutcomeIsExplicitAndOldIdsAreNeverMutatedAgain()
    {
        var context = new ContinuityStackTestContext("primary");
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(() => context.Session.Object, timeline, context.Telemetry);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(timeline.Snapshot().Entries.ToList().Any(row =>
                row.Kind == ContinuityEvidenceKind.CleanupUncertain &&
                row.Detail.Contains("not exposed", StringComparison.Ordinal)), Is.True);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row =>
                row.Kind == ContinuityEvidenceKind.CleanupUncertain && row.PartitionServerId == 100), Is.True);
            context.Session.Verify(value => value.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()), Times.Never);
            context.Session.Verify(value => value.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task UnknownLoadOutcomeIsNotLabeledAsTransferOrRecreation()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target")
        {
            TransferSucceeds = true,
            ReportLoadState = false
        };
        var sessions = new ContinuityTestSessions(source, target);
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, timeline, primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(ContinuityScenario.TransferOnLoad), CancellationToken.None)
                .ConfigureAwait(false);
            ContinuityStepResult result = await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Description, Does.Contain("no transfer/creation callback"));
            Assert.That(timeline.Snapshot().Counters.TransferObservations, Is.Zero);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Recreated),
                Is.False);
            await Assert.ThatAsync(() => backend.StopAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DifferentAuxiliaryIdentityCannotAcquireOrTransferSubscriptions()
    {
        var primary = new ContinuityStackTestContext("primary");
        primary.Identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target");
        var sessions = new ContinuityTestSessions(source, target);
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, new ContinuityTimeline(), primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await Assert.ThatAsync(
                () => backend.StartAsync(Configuration(ContinuityScenario.TransferOnLoad), CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(source.Subscriptions, Is.Empty);
            Assert.That(target.TransferRequested, Is.Null);
            await backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sessions.SourceLease.RetainOnClose, Is.EqualTo(s_deletedOnClose));
        }
    }

    [Test]
    public async Task ConfiguredFailoverUsesTheExplicitRedundantTargetAndReportsSourceCleanupUncertainty()
    {
        var primary = new ContinuityStackTestContext("primary");
        var source = new ContinuityStackTestContext("source");
        var target = new ContinuityStackTestContext("target") { TransferSucceeds = true };
        var sessions = new ContinuityTestSessions(source, target);
        var timeline = new ContinuityTimeline();
        var backend = new V2ContinuityBackend(
            () => primary.Session.Object, timeline, primary.Telemetry, sessions: sessions);
        await using (backend.ConfigureAwait(false))
        {
            await backend.StartAsync(Configuration(ContinuityScenario.ConfiguredFailover), CancellationToken.None)
                .ConfigureAwait(false);
            await backend.StepAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sessions.Opened[1], Is.EqualTo(ContinuitySessionPurpose.RedundantTarget));
            Assert.That(target.TransferRequested, Is.True);
            Assert.That(primary.Subscriptions, Is.Empty);
            await Assert.ThatAsync(() => backend.StopAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(timeline.Snapshot().Entries.ToList().Any(row =>
                row.Kind == ContinuityEvidenceKind.CleanupUncertain),
                Is.True);
        }
    }

    private static ContinuityConfiguration Configuration(
        ContinuityScenario scenario = ContinuityScenario.Observe,
        bool durable = false)
    {
        return ContinuityState.Validate(new ContinuityStateDto { Scenario = (int)scenario, Durable = durable });
    }

    private static readonly string[] s_durableInitialization = ["Add", "Durable", "Item"];
    private static readonly bool[] s_retainedOnClose = [true];
    private static readonly bool[] s_deletedOnClose = [false];
}
