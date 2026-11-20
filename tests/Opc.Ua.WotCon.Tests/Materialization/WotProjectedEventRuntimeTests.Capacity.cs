/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT projected event runtime.
    /// </summary>
    public sealed partial class WotProjectedEventRuntimeTests
    {
        /// <summary>
        /// Verifies that queue overflow drains accepted notifications without releasing generation evidence.
        /// </summary>
        [Test]
        public async Task QueueOverflowDrainsAcceptedNotificationsWithoutReleasingGenerationEvidence()
        {
            var h = new EventHarness();
            var (form, acknowledge, _) = h.AddAlarm("a");
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            h.Invokes["aAcknowledge"].OnInvoke = (_, _) =>
                new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            var factory = new WotProjectionBindingRuntimeFactory(
                h.Nodes.ChannelFactory, null, h.Publisher, h.Conditions,
                new WotProjectionBindingRuntimeOptions { MaxQueuedEvents = 1, MaxEventRoutes = 2 });
            await using IAsyncDisposable runtime = await h.WireAsync(factory).ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            upstream.Push(Notification("a", ByteString.From(new byte[] { 1 })));
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString firstId = events.Current.EventId!.Value;

            upstream.Push(Notification("a", ByteString.From(new byte[] { 2 }), acked: true));
            upstream.Push(Notification("a", ByteString.From(new byte[] { 3 }), acked: true, confirmed: true));
            Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString secondId = events.Current.EventId!.Value;
            ServiceResultException? overflow = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(overflow!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(upstream.Stopped, Is.EqualTo(1));
            Assert.That(Read(h.Conditions.Nodes["a"], "ConfirmedState", "Id").TryGetValue(out bool confirmed), Is.True);
            Assert.That(confirmed, Is.False, "The unqueued third occurrence must not mutate the accepted state.");
            foreach (ByteString eventId in new[] { firstId, secondId })
            {
                ServiceResult action = await CallAsync(h, acknowledge, [new Variant(eventId)]).ConfigureAwait(false);
                Assert.That(action.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            IAsyncEnumerator<BaseEventState> retry = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var retryOwner = retry.ConfigureAwait(false);
            ServiceResultException? unavailable = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await retry.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(unavailable!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(upstream.Channel.SubscribeEventCount, Is.EqualTo(1));
        }
    }
}
