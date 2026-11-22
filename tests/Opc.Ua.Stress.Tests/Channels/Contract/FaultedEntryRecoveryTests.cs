// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Verifies the implicit faulted-entry auto-reset added by
    /// <see cref="ClientChannelManager"/> in PR #3852 Phase E.
    /// </summary>
    /// <remarks>
    /// Before Phase E, an entry that transitioned to
    /// <see cref="ChannelState.Faulted"/> could only be recovered by
    /// disposing the lease and acquiring a fresh one. Phase E added
    /// <c>SwapFaultedEntryAsync</c>: when <c>ReconnectAsync(lease, …)</c>
    /// is called on a lease whose <c>Entry.State</c> is Faulted or
    /// Closed, the manager transparently swaps the lease's underlying
    /// entry to a freshly-created one under the same
    /// <see cref="ManagedChannelKey"/>, preserves the participant, and
    /// proceeds with the reconnect cycle.
    /// </remarks>
    [TestFixture]
    [Category("Contract")]
    [Category("ChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class FaultedEntryRecoveryTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        public async Task FaultedEntryAutoResetsOnNextReconnectAsync(
            CancellationToken ct)
        {
            using var bindings = new FakeChannelBindings();
            ClientChannelManager manager = CreateManager(bindings);
            await using (manager.ConfigureAwait(false))
            {
                ConfiguredEndpoint endpoint = CreateEndpoint();
                var participant = new FakeParticipant(endpoint);
                participant.ConfigureOnReconnect(
                    static (_, _) => ReconnectResultAsync(ParticipantReconnectResult.FatalForChannel));
                IManagedTransportChannel? channel = null;

                try
                {
                    channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);

                    // First reconnect: participant returns FatalForChannel → entry transitions to Faulted.
                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                    int notificationsAfterFirstCycle = participant.NotificationCount;
                    Assert.That(notificationsAfterFirstCycle, Is.GreaterThanOrEqualTo(1));

                    // Reconfigure participant to succeed on the next cycle, then call ReconnectAsync
                    // again on the SAME lease. SwapFaultedEntryAsync swaps to a fresh entry under
                    // the same ManagedChannelKey, re-attaches the participant, and drives the reconnect.
                    participant.ConfigureOnReconnect(
                        static (_, _) => ReconnectResultAsync(ParticipantReconnectResult.Reactivated));

                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                    Assert.Multiple(() =>
                    {
                        Assert.That(channel.State, Is.EqualTo(ChannelState.Ready),
                            "SwapFaultedEntryAsync should reset the faulted entry and the second cycle should reach Ready.");
                        Assert.That(
                            participant.NotificationCount,
                            Is.GreaterThan(notificationsAfterFirstCycle),
                            "Participant should be re-invoked on the fresh entry after the swap.");
                    });
                }
                finally
                {
                    channel?.Dispose();
                }
            }
        }
    }
}
