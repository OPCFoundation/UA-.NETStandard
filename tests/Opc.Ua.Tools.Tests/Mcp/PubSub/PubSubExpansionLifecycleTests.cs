// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
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
