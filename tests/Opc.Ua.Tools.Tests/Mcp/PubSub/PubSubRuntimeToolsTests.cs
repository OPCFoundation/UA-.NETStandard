// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PubSubRuntimeToolsTests
    {
        [Test]
        public async Task StoppedRuntimeToolsReturnStoppedStateAsync()
        {
            await using var scope = new PubSubMcpTestScope();
            using IDisposable caller = scope.Enter();
            await using PubSubRuntimeManager manager =
                scope.NewManager();

            PubSubRuntimeStatus status = await PubSubRuntimeTools.StatusAsync(manager)
                .ConfigureAwait(false);
            ArrayOf<PubSubReceivedDataSet> received =
                await PubSubRuntimeTools.ReadReceivedAsync(manager, true)
                    .ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(status.IsRunning, Is.False);
            Assert.That(received, Is.Empty);
            Assert.That(stopped.Mode, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task PublisherRuntimeToolsStartPublishAndStopAsync()
        {
            await using var scope = new PubSubMcpTestScope();
            using IDisposable caller = scope.Enter();
            await using PubSubRuntimeManager manager =
                scope.NewManager();
            int port = scope.Port;
            string endpoint = $"opc.udp://127.0.0.1:{port}";

            PubSubRuntimeStatus started =
                await PubSubRuntimeTools.StartPublisherAsync(
                    manager,
                    endpoint,
                    12,
                    34,
                    "Value:Int32").ConfigureAwait(false);
            PubSubRuntimePublishResult published =
                await PubSubRuntimeTools.PublishAsync(manager, "Value=42")
                    .ConfigureAwait(false);
            PubSubRuntimeStatus status = await PubSubRuntimeTools.StatusAsync(manager)
                .ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(started.Mode, Is.EqualTo("Publisher"));
            Assert.That(published.Fields, Has.Count.EqualTo(1));
            Assert.That(status.IsRunning, Is.True);
            Assert.That(stopped.IsRunning, Is.False);
        }

        [Test]
        public async Task SubscriberRuntimeToolStartsAndStopsAsync()
        {
            await using var scope = new PubSubMcpTestScope();
            using IDisposable caller = scope.Enter();
            await using PubSubRuntimeManager manager =
                scope.NewManager();
            int port = scope.Port;

            PubSubRuntimeStatus started =
                await PubSubRuntimeTools.StartSubscriberAsync(
                    manager,
                    $"opc.udp://127.0.0.1:{port}",
                    1,
                    2,
                    "Value:Int32").ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(started.Mode, Is.EqualTo("Subscriber"));
            Assert.That(stopped.Mode, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task ExplicitTestGrantStillRejectsAnUnlistedLoopbackDestinationAsync()
        {
            await using var scope = new PubSubMcpTestScope();
            using IDisposable caller = scope.Enter();
            await using PubSubRuntimeManager manager = scope.NewManager();
            int otherPort = scope.Port == ushort.MaxValue ? scope.Port - 1 : scope.Port + 1;

            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await PubSubRuntimeTools.StartPublisherAsync(
                    manager, $"opc.udp://127.0.0.1:{otherPort}", 1, 2, "Value:Int32").ConfigureAwait(false));

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That((await manager.StatusAsync().ConfigureAwait(false)).IsRunning, Is.False);
        }
    }
}
#endif
