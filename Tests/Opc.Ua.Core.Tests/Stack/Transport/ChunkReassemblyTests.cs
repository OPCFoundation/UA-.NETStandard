/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("ChunkReassembly")]
    public class ChunkReassemblyTests
    {
        [TestCase(0, 1073741824L)]
        [TestCase(-1, 1073741824L)]
        [TestCase(2097152, 67108864L)]
        [TestCase(8388608, 134217728L)]
        [TestCase(134217728, 1073741824L)]
        [TestCase(int.MaxValue, 8589934588L)]
        public void DefaultBudgetMatchesUpstreamSizing(int size, long expected)
        {
            Assert.That(ChunkReassemblyBudget.CreateDefault(size).MaxBytes, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConcurrentReservationsNeverExceedCapacityAndCanBeReused(bool activated)
        {
            var budget = new ChunkReassemblyBudget(1000);
            long limit = activated ? 1000 : 500;
            Parallel.For(0, 100, _ =>
            {
                for (int i = 0; i < 100; i++)
                {
                    if (budget.TryReserve(37, activated))
                    {
                        Assert.That(budget.ReservedBytes, Is.InRange(37L, limit));
                        budget.Release(37);
                    }
                }
            });
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(budget.TryReserve(limit, activated), Is.True);
            Assert.That(budget.TryReserve(1, activated), Is.False);
            Assert.Throws<InvalidOperationException>(() => budget.Release(1001));
            budget.Release(limit);
        }

        [TestCase(1000)]
        [TestCase(1001)]
        public void SessionlessThresholdIncludesActivatedReservations(long capacity)
        {
            var budget = new ChunkReassemblyBudget(capacity);
            long half = capacity / 2;
            Assert.That(budget.TryReserve(half - 1, true), Is.True);
            Assert.That(budget.TryReserve(2, false), Is.False);
            Assert.That(budget.TryReserve(1, false), Is.True);
            Assert.That(budget.TryReserve(1, false), Is.False);
            Assert.That(budget.TryReserve(capacity - half, true), Is.True);
            Assert.That(budget.ReservedBytes, Is.EqualTo(capacity));
            budget.Release(capacity);
            Assert.That(budget.TryReserve(half, false), Is.True);
            budget.Release(half);
        }

        [Test]
        public void LiveMembershipOverridesActivationResponseHintAndDowngradeReleasesBuffers()
        {
            var budget = new ChunkReassemblyBudget(1024);
            bool activated = true;
            var quotas = new ChannelQuotas(new ServiceMessageContext(NUnitTelemetryContext.Create()))
            {
                ChunkReassemblyBudget = budget,
                HasActivatedSession = id => id == "test-1" && activated
            };
            using var owner = new TestChannel(budget, quotas);
            owner.SetSession(false); // A failed/late response cannot downgrade a live session.
            Assert.That(owner.UsedBySession, Is.True);
            for (int i = 0; i < 3; i++)
            {
                owner.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            }
            Assert.That(budget.ReservedBytes, Is.EqualTo(768));
            using var sessionless = new TestChannel(budget);
            sessionless.SetSession(false);
            sessionless.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            Assert.That(sessionless.Closed, Is.True);
            Assert.That(budget.ReservedBytes, Is.EqualTo(768));

            activated = false;
            owner.SetSession(true); // A successful but stale response cannot restore membership.
            Assert.That(owner.UsedBySession, Is.False);
            Assert.That(budget.ReservedBytes, Is.EqualTo(768), "Downgrade does not evict retained chunks.");
            owner.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            Assert.That(owner.Closed, Is.True);
            Assert.That(owner.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
            using var next = new TestChannel(budget);
            next.SetSession(false);
            next.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            Assert.That(next.Closed, Is.False);
        }

        [Test]
        public void DowngradedChannelCanStillCompleteAnAlreadyRetainedRequest()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            byte[] body = channel.EncodeRequest();
            int split = body.Length / 2;
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, body.Take(split).ToArray(), 1);
            channel.SetSession(false);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Final, body.Skip(split).ToArray(), 1);
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManyChannelsCannotRetainMoreThanTheSharedBudget(bool activatedSession)
        {
            var budget = new ChunkReassemblyBudget(1024);
            var channels = new List<TestChannel>();
            try
            {
                for (int i = 0; i < 40; i++)
                {
                    var channel = new TestChannel(budget);
                    channels.Add(channel);
                    channel.SetSession(activatedSession);
                    // Empty bodies still retain a complete backing array.
                    channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
                    Assert.That(budget.ReservedBytes, Is.LessThanOrEqualTo(budget.MaxBytes));
                    Assert.That(channel.Requests, Is.Empty);
                    if (channel.Closed)
                    {
                        Assert.That(channel.Pool.Outstanding, Is.Zero);
                        Assert.That(channel.LastError, Is.EqualTo(StatusCodes.BadTcpNotEnoughResources));
                    }
                }
                Assert.That(channels.Count(c => !c.Closed), Is.EqualTo(activatedSession ? 4 : 2));
                Assert.That(budget.ReservedBytes, Is.EqualTo(activatedSession ? 1024 : 512));
            }
            finally
            {
                foreach (TestChannel channel in channels)
                {
                    channel.Dispose();
                    Assert.That(channel.Pool.Outstanding, Is.Zero);
                }
            }
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExhaustionReleasesOldAndIncomingBuffersEvenIfReportingFails(bool failReporting)
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [1], 1);
            channel.FailReporting = failReporting;
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [2], 1);
            Assert.That(channel.Closed, Is.True);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
            // A receive callback already queued when the socket closed must not dispatch.
            channel.Receive(TcpMessageType.Message | TcpMessageType.Final, channel.EncodeRequest(), 1);
            Assert.That(channel.Requests, Is.Empty);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
        }

        [Test]
        public void CompletionReleasesCapacityAndDispatchesValidChunkedRequests()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            byte[] body = channel.EncodeRequest();
            for (uint requestId = 1; requestId <= 2; requestId++)
            {
                int split = body.Length / 2;
                channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, body.Take(split).ToArray(), requestId);
                Assert.That(budget.ReservedBytes, Is.EqualTo(256));
                channel.Receive(TcpMessageType.Message | TcpMessageType.Final, body.Skip(split).ToArray(), requestId);
                Assert.That(budget.ReservedBytes, Is.Zero);
                Assert.That(channel.Pool.Outstanding, Is.Zero);
            }
            Assert.That(channel.Requests.Count, Is.EqualTo(2));
            Assert.That(channel.Requests.All(r => r is ReadRequest), Is.True);
            Assert.That(channel.Closed, Is.False);
        }

        [Test]
        public void SingleChunkRequestWorksWhileAnotherChannelHoldsTheEntireBudget()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var holder = new TestChannel(budget);
            using var other = new TestChannel(budget);
            holder.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            other.SetSession(false);
            other.Receive(TcpMessageType.Message | TcpMessageType.Final, other.EncodeRequest(), 1);
            Assert.That(other.Requests.Count, Is.EqualTo(1));
            Assert.That(other.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.EqualTo(256));
        }

        [Test]
        public void AbortReleasesCapacityAndKeepsTheChannelUsable()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Abort, [], 1);
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(channel.Closed, Is.False);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Final, channel.EncodeRequest(), 2);
            Assert.That(channel.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReplacementReleasesPreviousReservationBeforeReservingNewChunk()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [1], 1);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [2], 2);
            Assert.That(budget.ReservedBytes, Is.EqualTo(256));
            Assert.That(channel.Pool.Outstanding, Is.EqualTo(1));
            Assert.That(channel.Closed, Is.False);
        }

        [TestCase("dispose")]
        [TestCase("close")]
        [TestCase("fault")]
        [TestCase("disconnect")]
        public void TerminalAndFaultPathsReleasePartialMessages(string action)
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [1], 1);
            switch (action)
            {
                case "dispose": channel.Dispose(); break;
                case "close": channel.CloseChannel(); break;
                case "fault": channel.Fault(); break;
                case "disconnect": channel.OnReceiveError(null, new ServiceResult(StatusCodes.BadConnectionClosed)); break;
            }
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            channel.Dispose();
            Assert.That(channel.Pool.Outstanding, Is.Zero);
        }

        [Test]
        public void FaultCleanupDoesNotPermanentlyDisableReassembly()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            channel.Fault();
            channel.Reopen();
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 2);
            Assert.That(budget.ReservedBytes, Is.EqualTo(256));
            Assert.That(channel.Pool.Outstanding, Is.EqualTo(1));
        }

        [Test]
        public void IntermediateCloseChunkIsReturnedExactlyOnce()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.Receive(TcpMessageType.Close | TcpMessageType.Intermediate, [], 1);
            Assert.That(channel.Closed, Is.True);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [Test]
        public void DiscoveryRejectionDoesNotLeakOrReadReturnedBuffers()
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.SetDiscoveryOnly();
            channel.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, channel.EncodeRequest(), 1);
            Assert.That(channel.Requests, Is.Empty);
            Assert.That(channel.Closed, Is.True);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [TestCase(TcpMessageType.Intermediate)]
        [TestCase(TcpMessageType.Final)]
        public void MalformedFirstDiscoveryChunkReleasesItsBuffer(uint chunkType)
        {
            var budget = new ChunkReassemblyBudget(256);
            using var channel = new TestChannel(budget);
            channel.SetDiscoveryOnly();
            channel.Receive(TcpMessageType.Message | chunkType, [], 1);
            Assert.That(channel.Requests, Is.Empty);
            Assert.That(channel.Pool.Outstanding, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(channel.SentMessages, Is.EqualTo(1), "The discovery fault response must still be sent.");
            Assert.That(channel.DecodeLastResponse(), Is.TypeOf<ServiceFault>());
            Assert.That(channel.Closed, Is.False);
        }

        [Test]
        public void SavingWhileDisposingReturnsEveryBufferOnce()
        {
            for (int i = 0; i < 50; i++)
            {
                var budget = new ChunkReassemblyBudget(1024);
                using var channel = new TestChannel(budget);
                byte[] buffer = channel.Rent(100);
                Parallel.Invoke(() => channel.Save(buffer), channel.Dispose);
                Assert.That(channel.Pool.Outstanding, Is.Zero);
                Assert.That(budget.ReservedBytes, Is.Zero);
            }
        }

        [Test]
        public void ClientContextChunksDoNotConsumeServerBudget()
        {
            var budget = new ChunkReassemblyBudget(1);
            using var channel = new TestChannel(budget);
            channel.Save(channel.Rent(100), serverContext: false);
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(channel.Pool.Outstanding, Is.EqualTo(1));
        }

        [Test]
        public void StandaloneListenerCreatesDefaultBudgetAndTwoListenersShareInjectedBudget()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new ChunkReassemblyBudget(512);
            using var first = new TcpTransportListener(telemetry);
            using var second = new TcpTransportListener(telemetry);
            using var standalone = new TcpTransportListener(telemetry);
            Func<string, bool> membership = _ => false;
            OpenListener(first, budget, telemetry, membership);
            OpenListener(second, budget, telemetry, membership);
            OpenListener(standalone, null, telemetry);
            var firstQuotas = (ChannelQuotas)typeof(TcpTransportListener)
                .GetField("m_quotas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(first);
            var secondQuotas = (ChannelQuotas)typeof(TcpTransportListener)
                .GetField("m_quotas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second);
            var standaloneQuotas = (ChannelQuotas)typeof(TcpTransportListener)
                .GetField("m_quotas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standalone);
            Assert.That(firstQuotas.HasActivatedSession, Is.SameAs(membership));
            Assert.That(secondQuotas.HasActivatedSession, Is.SameAs(membership));
            Assert.That(standaloneQuotas.HasActivatedSession, Is.Null);
            Assert.That(firstQuotas.ChunkReassemblyBudget, Is.SameAs(budget));
            Assert.That(secondQuotas.ChunkReassemblyBudget, Is.SameAs(budget));
            Assert.That(standaloneQuotas.ChunkReassemblyBudget.MaxBytes, Is.EqualTo(64L * 1024 * 1024));
            using var one = new TestChannel(budget, firstQuotas);
            using var two = new TestChannel(budget, secondQuotas);
            one.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            two.Receive(TcpMessageType.Message | TcpMessageType.Intermediate, [], 1);
            Assert.That(two.Closed, Is.True);
            Assert.That(one.Closed, Is.False);
            Assert.That(budget.ReservedBytes, Is.EqualTo(256));
        }

        [Test]
        public async Task ServerSharesOneBudgetAcrossEndpointsAndResetsItAfterStopAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var server = new ServerBase();
            typeof(ServerBase).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(server, new ApplicationConfiguration(telemetry) { ServerConfiguration = new ServerConfiguration() });
            typeof(ServerBase).GetProperty("MessageContext")
                .SetValue(server, new ServiceMessageContext(telemetry));
            Func<string, bool> membership = id => id == "activated";
            server.HasActivatedSession = membership;
            var captured = new List<ChunkReassemblyBudget>();
            var capturedMembership = new List<Func<string, bool>>();
            var listener = new Mock<ITransportListener>();
            listener.Setup(l => l.Open(It.IsAny<Uri>(), It.IsAny<TransportListenerSettings>(), It.IsAny<ITransportListenerCallback>()))
                .Callback<Uri, TransportListenerSettings, ITransportListenerCallback>((uri, settings, callback) =>
                {
                    captured.Add(settings.ChunkReassemblyBudget);
                    capturedMembership.Add(settings.HasActivatedSession);
                });
            var endpoint = new Uri("opc.tcp://localhost:12345");
            server.CreateServiceHostEndpoint(endpoint, [], EndpointConfiguration.Create(), listener.Object, null);
            server.CreateServiceHostEndpoint(endpoint, [], EndpointConfiguration.Create(), listener.Object, null);
            Assert.That(captured[0], Is.Not.Null);
            Assert.That(captured[1], Is.SameAs(captured[0]));
            Assert.That(capturedMembership.All(query => ReferenceEquals(query, membership)), Is.True);
            await server.StopAsync().ConfigureAwait(false);
            typeof(ServerBase).GetProperty("MessageContext")
                .SetValue(server, new ServiceMessageContext(telemetry));
            server.CreateServiceHostEndpoint(endpoint, [], EndpointConfiguration.Create(), listener.Object, null);
            Assert.That(captured[2], Is.Not.SameAs(captured[0]));
        }

        [Test]
        public async Task RealSocketsCannotAccumulateUnfinishedOpenMessagesAcrossListenersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var budget = new ChunkReassemblyBudget(32768);
            using var first = new TcpTransportListener(telemetry);
            using var second = new TcpTransportListener(telemetry);
            OpenListener(first, budget, telemetry);
            OpenListener(second, budget, telemetry);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var one = new TcpClient();
            using var two = new TcpClient();
            await one.ConnectAsync(IPAddress.Loopback, first.EndpointUrl.Port).ConfigureAwait(false);
            await two.ConnectAsync(IPAddress.Loopback, second.EndpointUrl.Port).ConfigureAwait(false);
            foreach ((TcpClient client, Uri endpoint) in new[] { (one, first.EndpointUrl), (two, second.EndpointUrl) })
            {
                byte[] hello = EncodeFrame(context, encoder =>
                {
                    encoder.WriteUInt32(null, TcpMessageType.Hello);
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteUInt32(null, 8192);
                    encoder.WriteUInt32(null, 8192);
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteString(null, endpoint.ToString());
                });
                await WriteBytesAsync(client.GetStream(), hello, timeout.Token).ConfigureAwait(false);
                byte[] acknowledge = await ReadFrameAsync(client.GetStream(), timeout.Token).ConfigureAwait(false);
                Assert.That(BitConverter.ToUInt32(acknowledge, 0), Is.EqualTo(TcpMessageType.Acknowledge));
            }
            byte[] partialOpen = EncodeFrame(context, encoder =>
            {
                encoder.WriteUInt32(null, TcpMessageType.Open | TcpMessageType.Intermediate);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, SecurityPolicies.None);
                encoder.WriteByteString(null, (byte[])null);
                encoder.WriteByteString(null, (byte[])null);
                encoder.WriteUInt32(null, 1);
                encoder.WriteUInt32(null, 1);
            });
            await WriteBytesAsync(one.GetStream(), partialOpen, timeout.Token).ConfigureAwait(false);
            while (budget.ReservedBytes == 0)
            {
                await Task.Delay(10, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(budget.ReservedBytes, Is.EqualTo(budget.MaxBytes / 2));
            await WriteBytesAsync(two.GetStream(), partialOpen, timeout.Token).ConfigureAwait(false);
            byte[] error = await ReadFrameAsync(two.GetStream(), timeout.Token).ConfigureAwait(false);
            Assert.That(BitConverter.ToUInt32(error, 0), Is.EqualTo(TcpMessageType.Error));
            Assert.That(BitConverter.ToUInt32(error, 8), Is.EqualTo(StatusCodes.BadTcpNotEnoughResources));
            Assert.That(budget.ReservedBytes, Is.EqualTo(budget.MaxBytes / 2));
            one.Close();
            while (budget.ReservedBytes != 0)
            {
                await Task.Delay(10, timeout.Token).ConfigureAwait(false);
            }
        }

        private static byte[] EncodeFrame(IServiceMessageContext context, Action<BinaryEncoder> write)
        {
            using var encoder = new BinaryEncoder(context);
            write(encoder);
            byte[] bytes = encoder.CloseAndReturnBuffer();
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 4);
            return bytes;
        }

        private static Task WriteBytesAsync(NetworkStream stream, byte[] bytes, CancellationToken ct)
        {
#if NET6_0_OR_GREATER
            return stream.WriteAsync(bytes.AsMemory(), ct).AsTask();
#else
            return stream.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
        }

        private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] header = new byte[8];
            await ReadBytesAsync(stream, header, 0, header.Length, ct).ConfigureAwait(false);
            int size = BitConverter.ToInt32(header, 4);
            Assert.That(size, Is.InRange(8, 65536));
            byte[] frame = new byte[size];
            header.CopyTo(frame, 0);
            await ReadBytesAsync(stream, frame, 8, size - 8, ct).ConfigureAwait(false);
            return frame;
        }

        private static async Task ReadBytesAsync(NetworkStream stream, byte[] bytes, int offset, int length, CancellationToken ct)
        {
            while (length > 0)
            {
#if NET6_0_OR_GREATER
                int read = await stream.ReadAsync(bytes.AsMemory(offset, length), ct).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(bytes, offset, length, ct).ConfigureAwait(false);
#endif
                Assert.That(read, Is.GreaterThan(0), "Socket closed before sending the expected frame.");
                offset += read;
                length -= read;
            }
        }

        private static void OpenListener(
            TcpTransportListener listener,
            ChunkReassemblyBudget budget,
            ITelemetryContext telemetry,
            Func<string, bool> membership = null)
        {
            var context = new ServiceMessageContext(telemetry);
            int port;
            using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)probe.LocalEndPoint).Port;
            }
            var endpoint = new Uri($"opc.tcp://127.0.0.1:{port}");
            listener.Open(endpoint, new TransportListenerSettings
            {
                Configuration = EndpointConfiguration.Create(),
                ServerCertificateTypesProvider = new CertificateTypesProvider(
                    new ApplicationConfiguration(telemetry)
                    {
                        SecurityConfiguration = new SecurityConfiguration
                        {
                            ApplicationCertificates = [new CertificateIdentifier
                            {
                                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                            }]
                        }
                    }, telemetry),
                Descriptions = [new EndpointDescription(endpoint.ToString())
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }],
                NamespaceUris = context.NamespaceUris,
                Factory = context.Factory,
                ChunkReassemblyBudget = budget,
                HasActivatedSession = membership
            }, null);
        }

        private sealed class TrackingPool : ArrayPool<byte>
        {
            public int Outstanding
            {
                get { lock (m_lock) { return m_buffers.Count; } }
            }

            public override byte[] Rent(int minimumLength)
            {
                var buffer = new byte[Math.Max(256, minimumLength)];
                lock (m_lock) { m_buffers.Add(buffer); }
                return buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                lock (m_lock)
                {
                    Assert.That(m_buffers.Remove(array), Is.True, "Buffer returned twice or not rented here.");
                    // Expose accidental reads after return deterministically.
                    Array.Clear(array, 0, array.Length);
                }
            }

            private readonly object m_lock = new();
            private readonly HashSet<byte[]> m_buffers = [];
        }

        private sealed class TestChannel : TcpServerChannel
        {
            public TestChannel(ChunkReassemblyBudget budget, ChannelQuotas quotas = null)
                : this(budget, quotas, NUnitTelemetryContext.Create())
            {
            }

            private TestChannel(ChunkReassemblyBudget budget, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base("test", new Mock<ITcpChannelListener>().Object,
                    new BufferManager("test", 65535, telemetry),
                    quotas ?? new ChannelQuotas(new ServiceMessageContext(telemetry)) { ChunkReassemblyBudget = budget },
                    null, [], telemetry)
            {
                Pool = new TrackingPool();
                typeof(BufferManager).GetField("m_arrayPool", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(BufferManager, Pool);
                ChannelId = 1;
                UsedBySession = true;
                var token = CreateToken();
                token.TokenId = 1;
                ActivateToken(token);
                State = TcpChannelState.Open;
                SetRequestReceivedCallback((channel, id, request) => Requests.Add(request));
                var socket = new Mock<IMessageSocket>();
                socket.Setup(s => s.MessageSocketEventArgs()).Returns(() =>
                {
                    if (FailReporting) { throw new InvalidOperationException("Simulated write failure"); }
                    var args = new Mock<IMessageSocketAsyncEventArgs>();
                    args.SetupAllProperties();
                    byte[] bytes = null;
                    int count = 0;
                    args.Setup(a => a.SetBuffer(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                        .Callback<byte[], int, int>((b, offset, n) =>
                        {
                            bytes = b;
                            count = n;
                            if (BitConverter.ToUInt32(b, offset) == TcpMessageType.Error)
                            {
                                LastError = BitConverter.ToUInt32(b, offset + 8);
                            }
                        });
                    args.SetupGet(a => a.Buffer).Returns(() => bytes);
                    args.SetupGet(a => a.BytesTransferred).Returns(() => args.Object.BufferList?.TotalSize ?? count);
                    return args.Object;
                });
                socket.Setup(s => s.Send(It.IsAny<IMessageSocketAsyncEventArgs>()))
                    .Callback<IMessageSocketAsyncEventArgs>(args =>
                    {
                        SentMessages++;
                        // Copy before synchronous write completion returns and clears the buffers.
                        if (args.BufferList != null)
                        {
                            m_lastResponse = args.BufferList.SelectMany(buffer => buffer).ToArray();
                        }
                    })
                    .Returns(false);
                Socket = socket.Object;
            }

            public int SentMessages { get; private set; }
            public IEncodeable DecodeLastResponse()
            {
                // TestChannel uses unsecured symmetric messages.
                int headerSize = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
                using var stream = new ArraySegmentStream(new BufferCollection(
                    new ArraySegment<byte>(m_lastResponse, headerSize, m_lastResponse.Length - headerSize)));
                return BinaryDecoder.DecodeMessage(stream, null, Quotas.MessageContext);
            }

            public TrackingPool Pool { get; }
            public List<IServiceRequest> Requests { get; } = [];
            public bool Closed => State == TcpChannelState.Closed;
            public bool FailReporting { get; set; }
            public uint LastError { get; private set; }
            public void SetSession(bool active) => UsedBySession = active;
            public void SetDiscoveryOnly() => typeof(UaSCUaBinaryChannel)
                .GetProperty("DiscoveryOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(this, true);
            public void Reopen() => State = TcpChannelState.Open;
            public void Fault() => ForceChannelFault(new ServiceResult(StatusCodes.BadConnectionClosed));
            public void CloseChannel() => ChannelClosed();
            public byte[] Rent(int size) => BufferManager.TakeBuffer(size, "test");
            public void Save(byte[] buffer, bool serverContext = true) =>
                SaveIntermediateChunk(1, new ArraySegment<byte>(buffer, 0, 1), serverContext);
            public byte[] EncodeRequest() => BinaryEncoder.EncodeMessage(new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                NodesToRead = []
            }, Quotas.MessageContext);

            public void Receive(uint type, byte[] body, uint requestId)
            {
                byte[] bytes = Rent(24 + body.Length);
                using var encoder = new BinaryEncoder(bytes, 0, bytes.Length - 1, Quotas.MessageContext);
                encoder.WriteUInt32(null, type);
                encoder.WriteUInt32(null, (uint)(24 + body.Length));
                encoder.WriteUInt32(null, 1);
                encoder.WriteUInt32(null, 1);
                encoder.WriteUInt32(null, ++m_sequence);
                encoder.WriteUInt32(null, requestId);
                encoder.WriteRawBytes(body, 0, body.Length);
                int length = encoder.Close();
                OnMessageReceived(Socket, new ArraySegment<byte>(bytes, 0, length));
            }

            private byte[] m_lastResponse;
            private uint m_sequence;
        }
    }
}
