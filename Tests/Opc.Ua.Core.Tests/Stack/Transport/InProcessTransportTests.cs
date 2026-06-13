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
                    buffers.ReturnBuffer(received.Array!, nameof(PairedInProcessTransportsRoundTripChunksAsync));
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
    }
}
