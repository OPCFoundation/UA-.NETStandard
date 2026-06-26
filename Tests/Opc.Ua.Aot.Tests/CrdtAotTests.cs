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
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Opc.Ua.Server.Distributed.Crdt;

// CA2007: AOT tests run without a SynchronizationContext.
#pragma warning disable CA2007

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that exercise the CRDT active/active building
    /// blocks, ensuring they are reachable and functional under NativeAOT.
    /// </summary>
    public class CrdtAotTests
    {
        [Test]
        public async Task CrdtKeyValueStoreRoundTripsUnderAotAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var store = new CrdtSharedKeyValueStore(
                ReplicaId.FromUInt64(1),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);

            var value = new ByteString(new byte[] { 10, 20, 30 });
            await store.SetAsync("session/aot", value);

            (bool found, ByteString stored) = await store.TryGetAsync("session/aot");

            await Assert.That(found).IsTrue();
            await Assert.That(stored.ToArray()).IsEquivalentTo(new byte[] { 10, 20, 30 });
        }

        [Test]
        public async Task CrdtOptionsConfigureTransportUnderAotAsync()
        {
            var options = new CrdtAddressSpaceOptions
            {
                ReplicaId = ReplicaId.New()
            };
            options.UseUdpGossip(System.Net.IPAddress.Loopback, 0);

            await Assert.That(options.TransportFactory).IsNotNull();
        }
    }
}
