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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Actual application activation/disposal races beneath the shared owner-bound runtime registry.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionLifecycleTests
    {
        [Test]
        public async Task ConcurrentStartsMaterializeOnlyOneTransportAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();

            JsonObject[] results = await Task.WhenAll(
                fixture.Runtimes.StartAsync(handle).AsTask(), fixture.Runtimes.StartAsync(handle).AsTask())
                .ConfigureAwait(false);

            Assert.That(results.All(result => result["isRunning"]!.GetValue<bool>()), Is.True);
            Assert.That(fixture.Transports.Created, Has.Count.EqualTo(1));
            Assert.That(fixture.Transports.Factories, Is.EqualTo(1));
            _ = await fixture.Runtimes.RemoveAsync(handle).ConfigureAwait(false);
            Assert.That(fixture.Transports.Created.Single().DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task RemovalDuringBlockedStartCancelsAndDrainsTheOwnedTransportAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Transports.OpenGate = release.Task;
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            Task<JsonObject> starting = fixture.Runtimes.StartAsync(handle).AsTask();
            await fixture.Transports.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Task<JsonObject> removing = fixture.Runtimes.RemoveAsync(handle).AsTask();
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await starting.ConfigureAwait(false));
            Assert.That(starting.IsCanceled, Is.True);
            JsonObject removed = await removing.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(removed["removed"]!.GetValue<bool>(), Is.True);
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Transports.Created.Single().DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Transports.Created.Single().IsConnected, Is.False);
        }

        [Test]
        public async Task IdleExpiryClosesRealResourcesAndDoesNotRequireAnotherToolCallAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            PubSubExpansionTransport transport = fixture.Transports.Created.Single();

            fixture.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
            Assert.That(transport.DisposeCalls, Is.Zero);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            await transport.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(transport.DisposeCalls, Is.EqualTo(1));
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task CanceledCreationDoesNotReserveCapacityOrConstructTransportAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await fixture.Runtimes.CreateAsync(
                PubSubExpansionTestContext.Input(), cancelled.Token).ConfigureAwait(false));

            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }
    }
}
#endif
