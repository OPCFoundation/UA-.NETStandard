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

#if NET10_0
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Composes live WoT ownership with the real shared registry rather than reimplementing lease policy.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeLifecycleTests
    {
        [Test]
        public async Task ForeignAndWrongKindHandlesAreRejectedAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            string handle;
            using (fixture.Enter())
            {
                handle = await fixture.OpenAsync().ConfigureAwait(false);
            }
            using (fixture.Enter(WotRuntimeTestContext.Other))
            {
                JsonObject read = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);
                JsonObject close = WotRuntimeTestContext.Payload(
                    await fixture.Tools.CloseAsync(handle).ConfigureAwait(false), error: true);
                Assert.That(read["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
                Assert.That(close["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
                Assert.That(WotRuntimeTestContext.Payload(await fixture.Tools.ListAsync().ConfigureAwait(false))
                    ["runtimes"]!.AsArray(), Is.Empty);
            }
            using (fixture.Enter())
            {
                var otherRuntime = new McpOtherTestRuntime();
                McpRuntimeInfo other = await fixture.Registry.OpenAsync("other", McpCapability.PubSubRuntime,
                    _ => ValueTask.FromResult(otherRuntime)).ConfigureAwait(false);
                JsonObject wrongKind = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(other.Handle).ConfigureAwait(false), error: true);
                _ = WotRuntimeTestContext.Payload(
                    await fixture.Tools.CloseAsync(other.Handle).ConfigureAwait(false), error: true);
                Assert.That(wrongKind["statusCodeValue"]!.GetValue<uint>(),
                    Is.EqualTo(StatusCodes.BadTypeMismatch.Code));
                Assert.That(otherRuntime.DisposeCalls, Is.Zero);
                _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
                Assert.That(fixture.Factory.ReadCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task CloseDrainsAndDisposesOnceAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            TaskCompletionSource<bool> entered = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<WotWriteResult> committed = McpRuntimeTestContext.Completion<WotWriteResult>();
            fixture.Factory.Write = async (_, _) =>
            {
                entered.TrySetResult(true);
                return await committed.Task.ConfigureAwait(false);
            };
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> write = fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.Int32, "91"));
            await McpRuntimeTestContext.AwaitAsync(entered.Task).ConfigureAwait(false);
            Task<CallToolResult> close = fixture.Tools.CloseAsync(handle);

            Assert.That(close.IsCompleted, Is.False);
            Assert.That(fixture.Factory.DisposeCalls, Is.Zero);
            committed.TrySetResult(new WotWriteResult(StatusCodes.Good));
            JsonObject result = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(write).ConfigureAwait(false));
            _ = WotRuntimeTestContext.Payload(await McpRuntimeTestContext.AwaitAsync(close).ConfigureAwait(false));

            Assert.That(result["completion"]!.GetValue<string>(), Is.EqualTo("completed"));
            Assert.That(fixture.Factory.Writes, Has.Count.EqualTo(1));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public async Task IdleExpiryClosesOwnedChannelsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync().ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));

            fixture.Clock.Advance(TimeSpan.FromMinutes(5));
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Disposed.Task).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.ReadCalls, Is.EqualTo(1));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task HardExpiryDoesNotSlideWithRepeatedReadsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync().ConfigureAwait(false);
            DateTimeOffset originalExpiry = fixture.Registry.List().ToList().Single().ExpiresAt;
            for (int index = 0; index < 7; index++)
            {
                fixture.Clock.Advance(TimeSpan.FromMinutes(4));
                _ = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
                Assert.That(fixture.Registry.List().ToList().Single().ExpiresAt, Is.EqualTo(originalExpiry));
            }
            fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Disposed.Task).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.ReadCalls, Is.EqualTo(7));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task LateChannelActivationRemainsOwnedDuringCloseAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            TaskCompletionSource<bool> entered = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> release = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<bool> cancelled = McpRuntimeTestContext.Completion<bool>();
            fixture.Factory.Activating = async token =>
            {
                using System.Threading.CancellationTokenRegistration registration =
                    token.Register(() => cancelled.TrySetResult(true));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };
            string handle = await fixture.OpenAsync().ConfigureAwait(false);
            Task<CallToolResult> reading = fixture.Tools.ReadPropertyAsync(handle);
            await McpRuntimeTestContext.AwaitAsync(entered.Task).ConfigureAwait(false);
            Task<CallToolResult> closing = fixture.Tools.CloseAsync(handle);
            Assert.That(closing.IsCompleted, Is.False);
            await McpRuntimeTestContext.AwaitAsync(cancelled.Task).ConfigureAwait(false);
            release.TrySetResult(true);

            _ = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(reading).ConfigureAwait(false), error: true);
            _ = WotRuntimeTestContext.Payload(await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false));
            Assert.That(fixture.Factory.ReadCalls, Is.Zero);
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(1));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task CallerQuotaCountsPlansBeforeChannelActivationAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            _ = await fixture.OpenAsync("one").ConfigureAwait(false);
            _ = await fixture.OpenAsync("two").ConfigureAwait(false);
            JsonObject rejected = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("three",
                new WotDocumentSource { Text = WotRuntimeTestContext.PropertyDocument },
                WotRuntimeTestContext.Selection(), ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(rejected["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadTooManyOperations.Code));
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(2));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }
    }
}
#endif
