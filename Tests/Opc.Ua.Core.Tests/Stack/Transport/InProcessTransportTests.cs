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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Contract / smoke tests for the public
    /// <see cref="InProcessTransport"/> reference implementation of
    /// <see cref="IUaSCByteTransport"/> shipped from
    /// <c>Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs</c>.
    /// </summary>
    [TestFixture]
    [Category("CustomByteTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class InProcessTransportTests
    {
        [Test]
        public async Task PairedInProcessTransportsRoundTripChunksAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);

            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, receiveBufferSize: 8192, telemetry);

            try
            {
                byte[] payload = new byte[64];
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(i & 0xFF);
                }

                await client.SendChunkAsync(payload, CancellationToken.None).ConfigureAwait(false);

                ArraySegment<byte> received = await server
                    .ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(received, Has.Count.EqualTo(payload.Length));
                    byte[] copy = new byte[received.Count];
                    Buffer.BlockCopy(received.Array!, received.Offset, copy, 0, received.Count);
                    Assert.That(copy, Is.EqualTo(payload));
                }
                finally
                {
                    buffers.ReturnBuffer(received.Array, nameof(PairedInProcessTransportsRoundTripChunksAsync));
                }
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public async Task ReceiveAfterCloseReportsBadConnectionClosedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);

            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Close();
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                server.Close();
            }
        }

        [Test]
        public async Task CancelledLimitedRentDoesNotConsumeQueuedChunkAsync()
        {
            const int bufferSize = 8192;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var limited = new LimitingBufferManager(
                new FastBufferManager("inproc-limited", bufferSize, telemetry),
                new BufferManagerMemoryLimiter(bufferSize));
            var buffers = new BufferManager(limited);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, bufferSize, telemetry);
            byte[] held = limited.TakeBuffer(
                bufferSize,
                nameof(CancelledLimitedRentDoesNotConsumeQueuedChunkAsync));
            bool heldReturned = false;

            try
            {
                byte[] payload = [1, 2, 3, 4];
                await client.SendChunkAsync(payload, CancellationToken.None).ConfigureAwait(false);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

                Assert.That(
                    async () => await server.ReceiveChunkAsync(cts.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());

                limited.ReturnBuffer(
                    held,
                    nameof(CancelledLimitedRentDoesNotConsumeQueuedChunkAsync));
                heldReturned = true;
                ArraySegment<byte> received = await server
                    .ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(received, Has.Count.EqualTo(payload.Length));
                    Assert.That(
                        received.GetArray().AsSpan(0, received.Count).ToArray(),
                        Is.EqualTo(payload));
                }
                finally
                {
                    buffers.ReturnBuffer(
                        received.Array,
                        nameof(CancelledLimitedRentDoesNotConsumeQueuedChunkAsync));
                }
            }
            finally
            {
                if (!heldReturned)
                {
                    limited.ReturnBuffer(
                        held,
                        nameof(CancelledLimitedRentDoesNotConsumeQueuedChunkAsync));
                }
                client.Close();
                server.Close();
            }
        }

        [Test]
        public void CreatePairThrowsOnNullBuffers()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.That(
                () => InProcessTransport.CreatePair(null!, 8192, telemetry),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("buffers"));
        }

        [Test]
        public void CreatePairThrowsOnNullTelemetry()
        {
            var buffers = new BufferManager("inproc-test", 8192, NUnitTelemetryContext.Create());
            Assert.That(
                () => InProcessTransport.CreatePair(buffers, 8192, null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void ImplementationPropertyReturnsUaInProc()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                Assert.That(client.Implementation, Is.EqualTo("UA-INPROC"));
                Assert.That(server.Implementation, Is.EqualTo("UA-INPROC"));
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public void FeaturesPropertyReturnsNone()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                Assert.That(client.Features, Is.EqualTo(TransportChannelFeatures.None));
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public void LocalAndRemoteEndpointsAreNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                Assert.That(client.LocalEndpoint, Is.Null);
                Assert.That(client.RemoteEndpoint, Is.Null);
                Assert.That(server.LocalEndpoint, Is.Null);
                Assert.That(server.RemoteEndpoint, Is.Null);
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public async Task ConnectAsyncThrowsNotSupportedExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                Assert.That(
                    async () => await client.ConnectAsync(
                        new Uri("opc.tcp://localhost:4840"),
                        CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<NotSupportedException>());
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public async Task SendChunkMemoryAfterCloseThrowsBadConnectionClosedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Close();
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client
                        .SendChunkAsync(new ReadOnlyMemory<byte>(new byte[4]), CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                server.Close();
            }
        }

        [Test]
        public async Task SendChunkBufferCollectionRoundTripsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                byte[] seg1 = [1, 2, 3];
                byte[] seg2 = [4, 5, 6];
                var col = new BufferCollection
                {
                    new ArraySegment<byte>(seg1),
                    new ArraySegment<byte>(seg2)
                };

                await client.SendChunkAsync(col, CancellationToken.None).ConfigureAwait(false);

                ArraySegment<byte> received = await server
                    .ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                try
                {
                    Assert.That(received, Has.Count.EqualTo(6));
                    byte[] actual = new byte[received.Count];
                    Buffer.BlockCopy(received.Array!, received.Offset, actual, 0, received.Count);
                    Assert.That(actual, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
                }
                finally
                {
                    buffers.ReturnBuffer(received.Array, nameof(SendChunkBufferCollectionRoundTripsAsync));
                }
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public async Task SendChunkBufferCollectionAfterCloseThrowsBadConnectionClosedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Close();
                var col = new BufferCollection { new ArraySegment<byte>(new byte[4]) };
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client
                        .SendChunkAsync(col, CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                server.Close();
            }
        }

        [Test]
        public async Task ReceiveChunkExceedingBufferSizeThrowsBadTcpMessageTooLargeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Use a standard buffer manager but a tiny receive buffer so the
            // first large chunk definitely exceeds the allocated buffer length.
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, receiveBufferSize: 4, telemetry);

            try
            {
                // Send 1000 bytes — guaranteed to exceed any buffer allocated for
                // receiveBufferSize=4 (ArrayPool rounds up, but not to 1000).
                await client
                    .SendChunkAsync(new ReadOnlyMemory<byte>(new byte[1000]), CancellationToken.None)
                    .ConfigureAwait(false);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public Task ReceiveChunkCancellationThrowsOperationCanceledAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                using var cts = new CancellationTokenSource();

                // Start receive — no data is sent so it blocks.
                ValueTask<ArraySegment<byte>> receiveTask =
                    server.ReceiveChunkAsync(cts.Token);

                // Cancel before any data arrives.
                cts.Cancel();

                Assert.That(
                    async () => await receiveTask.ConfigureAwait(false),
                    Throws.TypeOf<OperationCanceledException>());
            }
            finally
            {
                client.Close();
                server.Close();
            }

            return Task.CompletedTask;
        }

        [Test]
        public void CloseIsIdempotent()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Close();
                Assert.That(client.Close, Throws.Nothing,
                    "Second Close() must be a no-op.");
                Assert.That(client.Close, Throws.Nothing,
                    "Third Close() must also be a no-op.");
            }
            finally
            {
                server.Close();
            }
        }

        [Test]
        public async Task DisposeClosesTransportAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);
            (InProcessTransport client, InProcessTransport server) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Dispose();

                // After Dispose the outbound channel is complete so SendChunkAsync
                // must report BadConnectionClosed.
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client
                        .SendChunkAsync(new ReadOnlyMemory<byte>(new byte[4]), CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                server.Close();
            }
        }
    }
}
