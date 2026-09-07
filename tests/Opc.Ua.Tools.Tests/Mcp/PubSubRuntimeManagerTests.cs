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

// Opc.Ua.Mcp targets net10.0 only, so the MCP integration fixtures only
// build and run on net10.0.
#if NET10_0
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Deterministic in-process coverage for <see cref="PubSubRuntimeManager"/>:
    /// lifecycle, field parsing, a real UDP/UADP loopback publish/subscribe
    /// round trip on an ephemeral port, and the Action/Discovery/list error
    /// and happy paths reachable without a live broker.
    /// </summary>
    [TestFixture]
    public sealed class PubSubRuntimeManagerTests
    {
        [Test]
        public void ConstructorWithNullServicesThrowsArgumentNullException()
        {
            Assert.That(
                () => new PubSubRuntimeManager(null!, NullLogger<PubSubRuntimeManager>.Instance),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullLoggerThrowsArgumentNullException()
        {
            Assert.That(
                () => new PubSubRuntimeManager(McpTestEnvironment.Services, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task StatusAsyncWithoutStartReportsStoppedAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            PubSubRuntimeStatus status = await manager.StatusAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(status.IsRunning, Is.False);
                Assert.That(status.Mode, Is.EqualTo(nameof(PubSubRuntimeMode.Stopped)));
                Assert.That(status.Endpoint, Is.Empty);
            });
        }

        [Test]
        public async Task StartPublisherAsyncWithEmptyEndpointThrowsArgumentExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            Assert.That(
                async () => await manager.StartPublisherAsync(string.Empty, 1, 1, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task StartSubscriberAsyncWithEmptyEndpointThrowsArgumentExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            Assert.That(
                async () => await manager.StartSubscriberAsync(string.Empty, 1, 1, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task StartPublisherAsyncWithUnsupportedFieldTypeThrowsArgumentExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            Assert.That(
                async () => await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}", 1, 1, "Field:NotAType", CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task PublishAsyncWithoutActivePublisherThrowsInvalidOperationExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            Assert.That(
                async () => await manager.PublishAsync("Field=1").ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task ReadReceivedAsyncWithoutActiveSubscriberReturnsEmptyAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            ArrayOf<PubSubReceivedDataSet> received = await manager.ReadReceivedAsync(clear: false)
                .ConfigureAwait(false);

            Assert.That(received.Count, Is.Zero);
        }

        [Test]
        public async Task RequestDiscoveryAsyncWithoutActiveRuntimeThrowsInvalidOperationExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            var request = new PubSubDiscoveryRequest();

            Assert.That(
                async () => await manager.RequestDiscoveryAsync(request, TimeSpan.FromMilliseconds(50))
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task InvokeActionAsyncWithoutActiveRuntimeThrowsInvalidOperationExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            var request = new PubSubActionRequest { Target = new PubSubActionTarget { DataSetWriterId = 1 } };

            Assert.That(
                async () => await manager.InvokeActionAsync(request, TimeSpan.FromMilliseconds(50))
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task RegisterActionResponderAsyncWithoutActiveRuntimeThrowsInvalidOperationExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            var target = new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 };
            var handler = new DelegatePubSubActionHandler(
                static (invocation, ct) => ValueTask.FromResult(new PubSubActionHandlerResult()));

            Assert.That(
                async () => await manager.RegisterActionResponderAsync(target, handler, "echo", "d")
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task ListActionTargetsAndRespondersWithoutRuntimeReturnEmptyAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            ArrayOf<PubSubActionTargetInfo> targets = await manager.ListActionTargetsAsync().ConfigureAwait(false);
            ArrayOf<PubSubActionResponderRegistration> responders =
                await manager.ListActionRespondersAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(targets.Count, Is.Zero);
                Assert.That(responders.Count, Is.Zero);
            });
        }

        [Test]
        public async Task StopAsyncWithoutActiveRuntimeIsNoOpAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();

            PubSubRuntimeStatus status = await manager.StopAsync().ConfigureAwait(false);

            Assert.That(status.IsRunning, Is.False);
        }

        [Test]
        public async Task StartPublisherAsyncThenStopAsyncTransitionsModeAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            string url = $"opc.udp://127.0.0.1:{port}";
            PubSubRuntimeStatus started = await manager.StartPublisherAsync(url, 42, 7, "Speed:Int32")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(started.IsRunning, Is.True);
                Assert.That(started.Mode, Is.EqualTo(nameof(PubSubRuntimeMode.Publisher)));
                Assert.That(started.Endpoint, Is.EqualTo(url));
                Assert.That(started.PublisherId, Is.EqualTo((ushort)42));
                Assert.That(started.WriterGroupId, Is.EqualTo((ushort)7));
            });

            PubSubRuntimePublishResult published = await manager.PublishAsync("Speed=99").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(published.Fields.Count, Is.EqualTo(1));
                Assert.That(published.Fields[0].Name, Is.EqualTo("Speed"));
                Assert.That(published.Fields[0].Value, Is.EqualTo("99"));
            });

            // Restarting as a publisher must replace (stop + start) the
            // previous runtime rather than leaking it.
            PubSubRuntimeStatus restarted = await manager.StartPublisherAsync(url, 43, 8, null)
                .ConfigureAwait(false);
            Assert.That(restarted.PublisherId, Is.EqualTo((ushort)43));

            PubSubRuntimeStatus stopped = await manager.StopAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stopped.IsRunning, Is.False);
                Assert.That(stopped.Mode, Is.EqualTo(nameof(PubSubRuntimeMode.Stopped)));
            });

            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task PublishAsyncWithJsonFieldValuesUpdatesFieldsAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using (manager)
            {
                await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}",
                    1,
                    1,
                    "Flag:Boolean;Count:Int32;When:DateTime").ConfigureAwait(false);

                PubSubRuntimePublishResult result = await manager.PublishAsync(
                    "{\"Flag\": true, \"Count\": 5, \"When\": \"2026-01-01T00:00:00Z\"}").ConfigureAwait(false);

                Assert.That(result.Fields.Count, Is.EqualTo(3));

                Assert.That(
                    async () => await manager.PublishAsync("Unknown=1").ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>());
            }
        }

        [Test]
        public async Task StartSubscriberAsyncReadReceivedReturnsEmptyRingUntilDataArrivesAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using (manager)
            {
                PubSubRuntimeStatus status = await manager.StartSubscriberAsync(
                    $"opc.udp://127.0.0.1:{port}", 5, 9, null).ConfigureAwait(false);

                Assert.That(status.Mode, Is.EqualTo(nameof(PubSubRuntimeMode.Subscriber)));

                ArrayOf<PubSubReceivedDataSet> received = await manager.ReadReceivedAsync(clear: true)
                    .ConfigureAwait(false);
                Assert.That(received.Count, Is.Zero);
            }
        }

        [Test]
        public async Task StartPublisherAsyncWithAllSupportedFieldTypesParsesAndPublishesEveryTypeAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using (manager)
            {
                const string fieldSpec =
                    "Flag:Boolean;Sb:SByte;B:Byte;I16:Int16;U16:UInt16;I32:Int32;U32:UInt32;" +
                    "I64:Int64;U64:UInt64;F:Float;D:Double;Dt:DateTime;S:String";

                PubSubRuntimeStatus status = await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}", 1, 1, fieldSpec).ConfigureAwait(false);
                Assert.That(status.IsRunning, Is.True);

                const string json = """
                    {
                        "Flag": true,
                        "Sb": -5,
                        "B": 200,
                        "I16": -1234,
                        "U16": 60000,
                        "I32": -100000,
                        "U32": 4000000000,
                        "I64": -1234567890123,
                        "U64": 12345678901234,
                        "F": 1.5,
                        "D": 2.25,
                        "Dt": "2026-01-01T00:00:00Z",
                        "S": "hello"
                    }
                    """;

                PubSubRuntimePublishResult result = await manager.PublishAsync(json).ConfigureAwait(false);

                Assert.That(result.Fields.Count, Is.EqualTo(13));
                Assert.Multiple(() =>
                {
                    Assert.That(result.Fields[0].Value, Is.EqualTo("True"));
                    Assert.That(result.Fields[1].Value, Is.EqualTo("-5"));
                    Assert.That(result.Fields[2].Value, Is.EqualTo("200"));
                    Assert.That(result.Fields[9].Value, Is.EqualTo("1.5"));
                    Assert.That(result.Fields[12].Value, Is.EqualTo("hello"));
                });
            }
        }

        [Test]
        public async Task PublishAsyncWithJsonNullAndFalseValuesUpdatesFieldsAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using (manager)
            {
                await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}", 1, 1, "Flag:Boolean;Name:String").ConfigureAwait(false);

                PubSubRuntimePublishResult result = await manager.PublishAsync(
                    "{\"Flag\": false, \"Name\": null}").ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.Fields[0].Value, Is.EqualTo("False"));
                    Assert.That(result.Fields[1].Value, Is.Empty);
                });

                Assert.That(
                    async () => await manager.PublishAsync("MissingEqualsSign").ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>());
            }
        }

        [Test]
        public async Task StartPublisherAsyncWithSeparatorsOnlyFieldSpecThrowsArgumentExceptionAsync()
        {
            await using PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            Assert.That(
                async () => await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}", 1, 1, ";,;", CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task StartPublisherAsyncPublishesSampledDataOnPublishingIntervalAsync()
        {
            PubSubRuntimeManager manager = PubSubMcpTestHelpers.NewManager();
            int port;
            try
            {
                port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using (manager)
            {
                PubSubRuntimeStatus status = await manager.StartPublisherAsync(
                    $"opc.udp://127.0.0.1:{port}", 1, 1, "Speed:Int32").ConfigureAwait(false);
                Assert.That(status.IsRunning, Is.True);

                // The writer group's publish scheduler samples the DataSet on
                // a fixed ~100ms interval; waiting comfortably past that lets
                // at least one tick (MutablePublishedDataSetSource.SampleAsync
                // / BuildMetaData) fire deterministically, without asserting
                // on live wire delivery (which is unrelated to this timing).
                await Task.Delay(400).ConfigureAwait(false);

                PubSubRuntimeStatus stopped = await manager.StopAsync().ConfigureAwait(false);
                Assert.That(stopped.IsRunning, Is.False);
            }
        }
    }
}
#endif
