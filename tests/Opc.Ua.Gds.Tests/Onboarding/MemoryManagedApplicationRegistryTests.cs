// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;

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
                EncodedTicket: [0xAB],
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
                "urn:acme:device:1", [1, 2, 3], MakeTicket("1")).ConfigureAwait(false);
            NodeId second = await registry.RegisterAsync(
                "urn:acme:device:1", [4, 5, 6], MakeTicket("1")).ConfigureAwait(false);

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
                "urn:acme:device:1", [1], MakeTicket("orig")).ConfigureAwait(false);
            await registry.RegisterAsync(
                "urn:acme:device:1", [2], MakeTicket("new")).ConfigureAwait(false);

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
                "urn:acme:device:1", [1], MakeTicket()).ConfigureAwait(false);

            Assert.That(await registry.UnregisterAsync(nodeId).ConfigureAwait(false), Is.True);
            Assert.That(await registry.UnregisterAsync(nodeId).ConfigureAwait(false), Is.False);
            Assert.That(await registry.FindAsync("urn:acme:device:1").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task FindReturnsNodeIdForKnownUri()
        {
            var registry = new MemoryManagedApplicationRegistry();
            NodeId registered = await registry.RegisterAsync(
                "urn:acme:device:1", [1], MakeTicket()).ConfigureAwait(false);

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
        public async Task RegisterRejectsNullArgs()
        {
            var registry = new MemoryManagedApplicationRegistry();
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await registry.RegisterAsync(string.Empty,
                    [], MakeTicket()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.RegisterAsync("urn:1",
                    null!, MakeTicket()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.RegisterAsync("urn:1",
                    [], null!).ConfigureAwait(false));
        }
    }
}
