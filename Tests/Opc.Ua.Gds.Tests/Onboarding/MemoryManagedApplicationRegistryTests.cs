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
using Opc.Ua.Gds.Server.Onboarding;

#nullable enable

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests for <see cref="MemoryManagedApplicationRegistry"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class MemoryManagedApplicationRegistryTests
    {
        private static TicketRecord MakeTicket(string serial = "SN-1")
        {
            return new TicketRecord(
                TicketId: $"ticket-{serial}",
                EncodedTicket: new byte[] { 0xAB },
                Metadata: new TicketMetadata(
                    Kind: TicketKind.DeviceIdentity,
                    ManufacturerName: "Acme",
                    ModelName: "Pump",
                    SerialNumber: serial,
                    ProductInstanceUri: $"urn:acme:device:{serial}"),
                CreatedAt: DateTimeOffset.UtcNow);
        }

        [Test]
        public async Task RegisterAssignsStableNodeId()
        {
            var registry = new MemoryManagedApplicationRegistry(applicationNamespaceIndex: 2);

            NodeId first = await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 1, 2, 3 }, MakeTicket("1")).ConfigureAwait(false);
            NodeId second = await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 4, 5, 6 }, MakeTicket("1")).ConfigureAwait(false);

            Assert.That(first, Is.Not.EqualTo(NodeId.Null));
            Assert.That(second, Is.EqualTo(first),
                "Re-registering the same URI must preserve the NodeId.");
            Assert.That(first.NamespaceIndex, Is.EqualTo((ushort)2));
        }

        [Test]
        public async Task RegisterReplacesCertificateAndTicketOnRegister()
        {
            var registry = new MemoryManagedApplicationRegistry();

            await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 1 }, MakeTicket("orig")).ConfigureAwait(false);
            await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 2 }, MakeTicket("new")).ConfigureAwait(false);

            var seen = new List<ManagedApplication>();
            await foreach (ManagedApplication app in registry.ListAsync().ConfigureAwait(false))
            {
                seen.Add(app);
            }

            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(seen[0].Certificate, Is.EqualTo(new byte[] { 2 }));
            Assert.That(seen[0].Ticket.Metadata.SerialNumber, Is.EqualTo("new"));
        }

        [Test]
        public async Task UnregisterByNodeIdRemoves()
        {
            var registry = new MemoryManagedApplicationRegistry();

            NodeId nodeId = await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 1 }, MakeTicket()).ConfigureAwait(false);

            Assert.That(await registry.UnregisterAsync(nodeId).ConfigureAwait(false), Is.True);
            Assert.That(await registry.UnregisterAsync(nodeId).ConfigureAwait(false), Is.False);
            Assert.That(await registry.FindAsync("urn:acme:device:1").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task FindReturnsNodeIdForKnownUri()
        {
            var registry = new MemoryManagedApplicationRegistry();
            NodeId registered = await registry.RegisterAsync(
                "urn:acme:device:1", new byte[] { 1 }, MakeTicket()).ConfigureAwait(false);

            NodeId? found = await registry.FindAsync("urn:acme:device:1").ConfigureAwait(false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found, Is.EqualTo(registered));
        }

        [Test]
        public async Task FindReturnsNullForUnknownUri()
        {
            var registry = new MemoryManagedApplicationRegistry();
            NodeId? found = await registry.FindAsync("urn:nobody").ConfigureAwait(false);
            Assert.That(found, Is.Null);
        }

        [Test]
        public void RegisterRejectsNullArgs()
        {
            var registry = new MemoryManagedApplicationRegistry();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await registry.RegisterAsync("",
                    Array.Empty<byte>(), MakeTicket()).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.RegisterAsync("urn:1",
                    null!, MakeTicket()).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.RegisterAsync("urn:1",
                    Array.Empty<byte>(), null!).ConfigureAwait(false));
        }
    }
}
