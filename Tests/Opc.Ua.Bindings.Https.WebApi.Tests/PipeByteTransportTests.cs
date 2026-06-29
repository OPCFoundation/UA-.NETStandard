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

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Unit tests for the internal <see cref="PipeByteTransport"/>. A
    /// minimal <see cref="ConnectionContext"/> stub backed by in-memory
    /// <see cref="Pipe"/>s is used so no live Kestrel host is needed.
    /// </summary>
    [TestFixture]
    [Category("PipeByteTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class PipeByteTransportTests
    {
        private const int kBufferSize = 8192;
        private const int kMinChunkSize = 8; // UASC header length

        private BufferManager m_bufferManager = null!;
        private TelemetryStub m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = new TelemetryStub();
            m_bufferManager = new BufferManager("PipeByteTransportTests", kBufferSize, m_telemetry);
        }

        /// <summary>
        /// <see cref="PipeByteTransport"/> must expose implementation info
        /// and the Reconnect feature flag.
        /// </summary>
        [Test]
        public void ImplementationAndFeaturesAreCorrect()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            Assert.That(transport.Implementation, Is.EqualTo("UA-KESTREL-TCP"));
            Assert.That(transport.Features, Is.EqualTo(TransportChannelFeatures.Reconnect));
        }

        /// <summary>
        /// Constructor must reject a null <see cref="ConnectionContext"/>.
        /// </summary>
        [Test]
        public void ConstructorRejectsNullConnectionContext()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _ = new PipeByteTransport(null!, m_bufferManager, kBufferSize, m_telemetry));
        }

        /// <summary>
        /// Constructor must reject a null <see cref="BufferManager"/>.
        /// </summary>
        [Test]
        public void ConstructorRejectsNullBufferManager()
        {
            using var ctx = new TestConnectionContext();
            Assert.Throws<ArgumentNullException>(() =>
                _ = new PipeByteTransport(ctx, null!, kBufferSize, m_telemetry));
        }

        /// <summary>
        /// <see cref="PipeByteTransport.ConnectAsync"/> must always throw
        /// <see cref="NotSupportedException"/> because the transport is
        /// server-side only (built from an accepted Kestrel connection).
        /// </summary>
        [Test]
        public void ConnectAsyncThrowsNotSupported()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            Assert.ThrowsAsync<NotSupportedException>(
                async () => await transport.ConnectAsync(
                    new Uri("opc.tcp://localhost:4840"),
                    CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// LocalEndpoint and RemoteEndpoint are forwarded from the
        /// <see cref="ConnectionContext"/>.
        /// </summary>
        [Test]
        public void EndpointPropertiesDelegateToConnectionContext()
        {
            using var ctx = new TestConnectionContext();
            ctx.LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 4840);
            ctx.RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            Assert.That(transport.LocalEndpoint, Is.EqualTo(ctx.LocalEndPoint));
            Assert.That(transport.RemoteEndpoint, Is.EqualTo(ctx.RemoteEndPoint));
        }

        /// <summary>
        /// A single-chunk send followed by a read must round-trip the data.
        /// </summary>
        [Test]
        public async Task SendChunkAsyncRoundTripsDataAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            byte[] chunk = BuildValidChunk(size: 32);

            // Write to the server→client pipe (simulates data the "client" sees).
            await transport.SendChunkAsync(
                new ReadOnlyMemory<byte>(chunk),
                CancellationToken.None).ConfigureAwait(false);

            // Read back from the output side of the test context.
            ReadResult result = await ctx.ServerOutput.ReadAsync(CancellationToken.None)
                .ConfigureAwait(false);
            byte[] received = result.Buffer.ToArray();
            ctx.ServerOutput.AdvanceTo(result.Buffer.End);

            Assert.That(received, Is.EqualTo(chunk));
        }

        /// <summary>
        /// A <see cref="BufferCollection"/>-based send must concatenate
        /// all segments into a contiguous stream.
        /// </summary>
        [Test]
        public async Task SendChunkAsyncBufferCollectionRoundTripsDataAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            byte[] seg1 = new byte[16];
            byte[] seg2 = new byte[12];
            for (int i = 0; i < seg1.Length; i++)
            {
                seg1[i] = (byte)i;
            }

            for (int i = 0; i < seg2.Length; i++)
            {
                seg2[i] = (byte)(i + 100);
            }

            var buffers = new BufferCollection
            {
                new ArraySegment<byte>(seg1),
                new ArraySegment<byte>(seg2)
            };

            await transport.SendChunkAsync(buffers, CancellationToken.None).ConfigureAwait(false);

            ReadResult result = await ctx.ServerOutput.ReadAsync(CancellationToken.None)
                .ConfigureAwait(false);
            byte[] received = result.Buffer.ToArray();
            ctx.ServerOutput.AdvanceTo(result.Buffer.End);

            Assert.That(received, Has.Length.EqualTo(seg1.Length + seg2.Length));
            for (int i = 0; i < seg1.Length; i++)
            {
                Assert.That(received[i], Is.EqualTo(seg1[i]));
            }

            for (int i = 0; i < seg2.Length; i++)
            {
                Assert.That(received[seg1.Length + i], Is.EqualTo(seg2[i]));
            }
        }

        /// <summary>
        /// Null <see cref="BufferCollection"/> must throw
        /// <see cref="ArgumentNullException"/>.
        /// </summary>
        [Test]
        public void SendChunkAsyncNullBufferCollectionThrowsArgumentNull()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await transport.SendChunkAsync(
                    (BufferCollection)null!,
                    CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// <see cref="PipeByteTransport.ReceiveChunkAsync"/> must return the
        /// exact chunk written into the input pipe.
        /// </summary>
        [Test]
        public async Task ReceiveChunkAsyncReturnsCompleteChunkAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            byte[] chunk = BuildValidChunk(size: 32);
            await WriteToServerInputAsync(ctx, chunk).ConfigureAwait(false);

            ArraySegment<byte> received = await transport
                .ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(received, Has.Count.EqualTo(chunk.Length));
                byte[] copy = new byte[received.Count];
                Buffer.BlockCopy(received.Array!, received.Offset, copy, 0, received.Count);
                Assert.That(copy, Is.EqualTo(chunk));
            }
            finally
            {
                m_bufferManager.ReturnBuffer(received.Array!, nameof(ReceiveChunkAsyncReturnsCompleteChunkAsync));
            }
        }

        /// <summary>
        /// When the declared size in the UASC header exceeds
        /// <c>receiveBufferSize</c> the transport must throw with
        /// <see cref="StatusCodes.BadTcpMessageTooLarge"/>.
        /// </summary>
        [Test]
        public async Task ReceiveChunkAsyncRejectsOversizedChunkAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            // Build a header that claims size = receiveBufferSize + 1.
            byte[] header = BuildValidChunk(size: kBufferSize + 1);
            await WriteToServerInputAsync(ctx, header).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
        }

        /// <summary>
        /// A UASC header with <c>size &lt; 8</c> must be rejected with
        /// <see cref="StatusCodes.BadTcpMessageTypeInvalid"/>.
        /// </summary>
        [Test]
        public async Task ReceiveChunkAsyncRejectsChunkSizeBelowMinimumAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            // size = 4 in the header (< minimum 8 byte UASC header)
            byte[] bad = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(bad, TcpMessageType.Hello); // message type
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(4), 4);        // size = 4

            await WriteToServerInputAsync(ctx, bad).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
        }

        /// <summary>
        /// Completing the input pipe before a full message header arrives
        /// must yield <see cref="StatusCodes.BadConnectionClosed"/>.
        /// </summary>
        [Test]
        public async Task ReceiveChunkAsyncReportsClosedPipeAsBadConnectionClosedAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            // Complete the pipe without writing anything.
            await ctx.ServerInput.CompleteAsync().ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        /// <summary>
        /// Completing the input pipe mid-chunk (after header but before all
        /// body bytes) must also yield <see cref="StatusCodes.BadConnectionClosed"/>.
        /// </summary>
        [Test]
        public async Task ReceiveChunkAsyncReportsIncompleteBodyAsClosedAsync()
        {
            using var ctx = new TestConnectionContext();
            using var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);

            // Write only the 8-byte header (claims 64 bytes).
            byte[] header = BuildValidChunk(size: 64);
            // Only write the header bytes
            PipeWriter writer = ctx.ServerInput;
            Memory<byte> dest = writer.GetMemory(8);
            header.AsSpan(0, 8).CopyTo(dest.Span);
            writer.Advance(8);
            await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        /// <summary>
        /// <see cref="PipeByteTransport.Close"/> must be idempotent —
        /// calling it multiple times must not throw.
        /// </summary>
        [Test]
        public void CloseIsIdempotent()
        {
            using var ctx = new TestConnectionContext();
            var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);
            transport.Close();
            transport.Close();
            transport.Dispose();
        }

        /// <summary>
        /// <see cref="PipeByteTransport.Dispose"/> must not throw.
        /// </summary>
        [Test]
        public void DisposeDoesNotThrow()
        {
            using var ctx = new TestConnectionContext();
            var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);
            Assert.DoesNotThrow(transport.Dispose);
        }

        /// <summary>
        /// Sending on a closed transport must throw
        /// <see cref="StatusCodes.BadConnectionClosed"/>.
        /// </summary>
        [Test]
        public void SendChunkAsyncOnClosedTransportThrowsBadConnectionClosed()
        {
            using var ctx = new TestConnectionContext();
            var transport = new PipeByteTransport(ctx, m_bufferManager, kBufferSize, m_telemetry);
            transport.Close();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(
                    new ReadOnlyMemory<byte>(new byte[8]),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        /// <summary>
        /// Helper: build a valid UASC chunk with a Hello message type and
        /// the given total <paramref name="size"/> (header + body).
        /// </summary>
        private static byte[] BuildValidChunk(int size)
        {
            byte[] buffer = new byte[size];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, TcpMessageType.Hello);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), size);
            for (int i = 8; i < size; i++)
            {
                buffer[i] = (byte)(i & 0xFF);
            }

            return buffer;
        }

        /// <summary>
        /// Write <paramref name="data"/> into the server-side input pipe so
        /// the <see cref="PipeByteTransport"/> sees it via
        /// <c>ReceiveChunkAsync</c>.
        /// </summary>
        private static async Task WriteToServerInputAsync(TestConnectionContext ctx, byte[] data)
        {
            PipeWriter writer = ctx.ServerInput;
            Memory<byte> dest = writer.GetMemory(data.Length);
            data.AsSpan().CopyTo(dest.Span);
            writer.Advance(data.Length);
            await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Minimal <see cref="ConnectionContext"/> backed by in-memory
        /// <see cref="Pipe"/>s. The transport talks to
        /// <c>Transport.Input</c> (reads from <see cref="ServerInput"/>)
        /// and <c>Transport.Output</c> (writes to <see cref="ServerOutput"/>).
        /// </summary>
        private sealed class TestConnectionContext : ConnectionContext, IDisposable
        {
            private readonly Pipe m_inputPipe = new Pipe();
            private readonly Pipe m_outputPipe = new Pipe();

            public TestConnectionContext()
            {
                Transport = new DuplexPipeAdapter(m_inputPipe.Reader, m_outputPipe.Writer);
            }

            /// <summary>
            /// The write end the test uses to push data into the transport.
            /// </summary>
            public PipeWriter ServerInput => m_inputPipe.Writer;

            /// <summary>
            /// The read end the test uses to verify data the transport sent.
            /// </summary>
            public PipeReader ServerOutput => m_outputPipe.Reader;

            public override string ConnectionId { get; set; } = Guid.NewGuid().ToString();
            public override IFeatureCollection Features { get; } = new FeatureCollection();
            public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
            public override IDuplexPipe Transport { get; set; }

            public void Dispose()
            {
                try { m_inputPipe.Writer.Complete(); } catch { }
                try { m_inputPipe.Reader.Complete(); } catch { }
                try { m_outputPipe.Writer.Complete(); } catch { }
                try { m_outputPipe.Reader.Complete(); } catch { }
            }
        }

        /// <summary>
        /// Wraps a <see cref="PipeReader"/> + <see cref="PipeWriter"/> into
        /// an <see cref="IDuplexPipe"/>.
        /// </summary>
        private sealed class DuplexPipeAdapter : IDuplexPipe
        {
            public DuplexPipeAdapter(PipeReader input, PipeWriter output)
            {
                Input = input;
                Output = output;
            }

            public PipeReader Input { get; }
            public PipeWriter Output { get; }
        }

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
