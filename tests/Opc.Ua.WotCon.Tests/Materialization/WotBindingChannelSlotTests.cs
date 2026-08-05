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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises <see cref="WotBindingChannelSlot"/> directly: single-flight
    /// open caching, failed-open eviction (already covered end-to-end through
    /// <see cref="WotProjectionBindingRuntimeTests"/>), and — the focus here —
    /// hardening against <c>GetAsync</c> racing with, or being called after,
    /// <c>DisposeAsync</c>, so a channel this slot opens can never escape
    /// disposal.
    /// </summary>
    [TestFixture]
    public sealed class WotBindingChannelSlotTests
    {
        [Test]
        public async Task GetAsyncAfterDisposeThrowsObjectDisposedExceptionAndNeverOpensAChannel()
        {
            var factory = new FakeWotBindingChannelFactory();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, new WotTargetMappingDescriptor(targetNodeId: "ns=1;s=x"));
            var slot = new WotBindingChannelSlot(form, factory);

            await slot.DisposeAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await slot.GetAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(factory.OpenCount, Is.Zero, "A disposed slot must never open a channel.");
        }

        [Test]
        public async Task GetAsyncAfterDisposeOfAnOpenedSlotThrowsAndNeverOpensASecondChannel()
        {
            var factory = new FakeWotBindingChannelFactory();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, new WotTargetMappingDescriptor(targetNodeId: "ns=1;s=x"));
            var channel = new FakeWotBindingChannel(form);
            factory.SetChannel(form, channel);
            var slot = new WotBindingChannelSlot(form, factory);

            await slot.GetAsync(CancellationToken.None).ConfigureAwait(false);
            await slot.DisposeAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await slot.GetAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(factory.OpenCount, Is.EqualTo(1), "A disposed slot must never open a second channel.");
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeAsyncRacingAnInFlightGetAsyncDisposesTheOpenedChannelExactlyOnceNoLeak()
        {
            var factory = new FakeWotBindingChannelFactory();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, new WotTargetMappingDescriptor(targetNodeId: "ns=1;s=x"));
            var channel = new FakeWotBindingChannel(form);
            var openGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.SetOpener(form, async () =>
            {
                await openGate.Task.ConfigureAwait(false);
                return channel;
            });
            var slot = new WotBindingChannelSlot(form, factory);

            // GetAsync starts the open (which will not complete until the
            // gate is released) and DisposeAsync races it while the open is
            // still in flight.
            Task<IWotBindingChannel> getTask = slot.GetAsync(CancellationToken.None).AsTask();
            Task disposeTask = slot.DisposeAsync().AsTask();

            openGate.SetResult(true);
            IWotBindingChannel got = await getTask.ConfigureAwait(false);
            await disposeTask.ConfigureAwait(false);

            Assert.That(got, Is.SameAs(channel));
            Assert.That(factory.OpenCount, Is.EqualTo(1), "Single-flight must hold even when racing dispose.");
            Assert.That(channel.DisposeCount, Is.EqualTo(1),
                "The channel opened concurrently with dispose must still be disposed exactly once — no leak.");

            // Post-race: the slot is disposed, so no later GetAsync may open
            // another channel.
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await slot.GetAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(factory.OpenCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentSecondCallDoesNotRedisposeOrThrow()
        {
            var factory = new FakeWotBindingChannelFactory();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, new WotTargetMappingDescriptor(targetNodeId: "ns=1;s=x"));
            var channel = new FakeWotBindingChannel(form);
            factory.SetChannel(form, channel);
            var slot = new WotBindingChannelSlot(form, factory);
            await slot.GetAsync(CancellationToken.None).ConfigureAwait(false);

            await slot.DisposeAsync().ConfigureAwait(false);
            await slot.DisposeAsync().ConfigureAwait(false);

            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }
    }
}
