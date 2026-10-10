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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedEventRuntimeTests
    {
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
            ServiceResultException? overflow = Assert.ThrowsAsync<ServiceResultException>(async () =>
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
            ServiceResultException? unavailable = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await retry.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(unavailable!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(upstream.Channel.SubscribeEventCount, Is.EqualTo(1));
        }
    }
}
