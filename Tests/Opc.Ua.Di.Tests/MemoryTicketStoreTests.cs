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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Onboarding;

#nullable enable

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="MemoryTicketStore"/> — in-memory
    /// Onboarding ticket repository.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class MemoryTicketStoreTests
    {
        private static TicketMetadata Metadata(
            string serial = "SN-1",
            string productUri = "urn:acme:device:SN-1")
        {
            return new TicketMetadata(
                Kind: TicketKind.DeviceIdentity,
                ManufacturerName: "Acme",
                ModelName: "Pump",
                SerialNumber: serial,
                ProductInstanceUri: productUri);
        }

        [Test]
        public async Task AddStoresTicket()
        {
            var store = new MemoryTicketStore();
            byte[] payload = new byte[] { 1, 2, 3 };

            await store.AddAsync("ticket-1", payload, Metadata());
            TicketRecord? fetched = await store.GetAsync("ticket-1");

            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.TicketId, Is.EqualTo("ticket-1"));
            Assert.That(fetched.EncodedTicket, Is.EqualTo(payload));
            Assert.That(fetched.Metadata.SerialNumber, Is.EqualTo("SN-1"));
        }

        [Test]
        public async Task AddClonesPayloadBytes()
        {
            var store = new MemoryTicketStore();
            byte[] payload = new byte[] { 1, 2, 3 };
            await store.AddAsync("ticket-1", payload, Metadata());

            // Mutate the original after add — store contents must
            // remain unchanged.
            payload[0] = 99;
            TicketRecord? fetched = await store.GetAsync("ticket-1");
            Assert.That(fetched!.EncodedTicket[0], Is.EqualTo((byte)1));
        }

        [Test]
        public async Task AddWithExistingIdReplaces()
        {
            var store = new MemoryTicketStore();
            await store.AddAsync("t1", new byte[] { 1 }, Metadata("S1"));
            await store.AddAsync("t1", new byte[] { 2 }, Metadata("S2"));

            TicketRecord? fetched = await store.GetAsync("t1");
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.EncodedTicket[0], Is.EqualTo((byte)2));
            Assert.That(fetched.Metadata.SerialNumber, Is.EqualTo("S2"));
        }

        [Test]
        public void AddRejectsNullArgs()
        {
            var store = new MemoryTicketStore();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await store.AddAsync("", Array.Empty<byte>(), Metadata()));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await store.AddAsync("t1", null!, Metadata()));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await store.AddAsync("t1", Array.Empty<byte>(), null!));
        }

        [Test]
        public async Task RemoveReturnsTrueOnlyWhenPresent()
        {
            var store = new MemoryTicketStore();
            await store.AddAsync("t1", new byte[] { 1 }, Metadata());

            Assert.That(await store.RemoveAsync("t1"), Is.True);
            Assert.That(await store.RemoveAsync("t1"), Is.False);
            Assert.That(await store.GetAsync("t1"), Is.Null);
        }

        [Test]
        public async Task GetReturnsNullForUnknown()
        {
            var store = new MemoryTicketStore();
            TicketRecord? r = await store.GetAsync("never");
            Assert.That(r, Is.Null);
        }

        [Test]
        public async Task ListEnumeratesAllTickets()
        {
            var store = new MemoryTicketStore();
            await store.AddAsync("t1", new byte[] { 1 }, Metadata("S1"));
            await store.AddAsync("t2", new byte[] { 2 }, Metadata("S2"));
            await store.AddAsync("t3", new byte[] { 3 }, Metadata("S3"));

            var seen = new List<string>();
            await foreach (TicketRecord r in store.ListAsync())
            {
                seen.Add(r.TicketId);
            }

            Assert.That(seen, Has.Count.EqualTo(3));
            Assert.That(seen, Does.Contain("t1").And.Contain("t2").And.Contain("t3"));
        }

        [Test]
        public async Task FindByProductInstanceUriFindsMatch()
        {
            var store = new MemoryTicketStore();
            await store.AddAsync("t1", new byte[] { 1 },
                Metadata(productUri: "urn:acme:device:1"));
            await store.AddAsync("t2", new byte[] { 2 },
                Metadata(productUri: "urn:acme:device:2"));

            TicketRecord? r = await store.FindByProductInstanceUriAsync(
                "urn:acme:device:2");
            Assert.That(r, Is.Not.Null);
            Assert.That(r!.TicketId, Is.EqualTo("t2"));
        }

        [Test]
        public async Task FindByProductInstanceUriReturnsNullForUnknown()
        {
            var store = new MemoryTicketStore();
            await store.AddAsync("t1", new byte[] { 1 }, Metadata());

            TicketRecord? r = await store.FindByProductInstanceUriAsync(
                "urn:nobody");
            Assert.That(r, Is.Null);
        }
    }
}
