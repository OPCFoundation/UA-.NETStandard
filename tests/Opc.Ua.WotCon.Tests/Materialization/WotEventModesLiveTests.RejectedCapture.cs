// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT event modes live.
    /// </summary>
    public sealed partial class WotEventModesLiveTests
    {
        /// <summary>
        /// Verifies that native rejected capture keeps retained state and provenance.
        /// </summary>
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRejectedCaptureKeepsRetainedStateAndProvenanceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(ConditionSourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session), null, publisher,
                    new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions { MaxEventRoutes = 2 }));
            WotProjectionHandle handle = await host.AddAsync(
                ConditionProjection(mode, source.EndpointUrl, PrivatePublicSelection("Core")), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                ByteString acceptedId = ByteString.From(new byte[] { 0xD3, 0xC8, 0x11 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, acceptedId, ct,
                    ConfigurePrivateCoreFields).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> first = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString localId), Is.True);
                NodeId descriptor = await CapacityDescriptorAsync(destination, "condition", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);

                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true,
                    ByteString.From(new byte[] { 0xD3, 0xC8, 0x12 }), ct, condition =>
                    {
                        ConfigurePrivateCoreFields(condition);
                        condition.ClientUserId = null;
                        condition.Severity!.Value = 999;
                        condition.Time!.Value = SourceTime + TimeSpan.FromSeconds(1);
                    }).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.BadNoData));
                foreach (Subscription subscription in destination.Session.Subscriptions)
                {
                    Assert.That(await subscription.ConditionRefreshAsync(ct).ConfigureAwait(false), Is.True);
                }
                ArrayOf<Variant> retained = await events.ReadAsync(ct).ConfigureAwait(false);
                WoTEventOriginDataType after = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(retained[0], Is.EqualTo(new Variant(localId)));
                    Assert.That(retained[4], Is.EqualTo(first[4]));
                    Assert.That(retained[7], Is.EqualTo(first[7]));
                    Assert.That(after.SourceEventId, Is.EqualTo(acceptedId));
                    Assert.That(after.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                    Assert.That(after.Generation, Is.EqualTo(before.Generation));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
#endif
