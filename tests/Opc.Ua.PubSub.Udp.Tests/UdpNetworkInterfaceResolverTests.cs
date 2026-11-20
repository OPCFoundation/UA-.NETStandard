/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Net.NetworkInformation;
using System.Net.Sockets;
using NUnit.Framework;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Validates <see cref="UdpNetworkInterfaceResolver"/> name, IP, and
    /// default resolution. Many of the cases depend on the host's network
    /// configuration; the fixture skips gracefully when no IPv4-capable
    /// interface is available.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class UdpNetworkInterfaceResolverTests
    {
        [Test]
        public void Resolve_NullPreferred_ReturnsFirstUpInterface()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (resolved is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }
            Assert.That(resolved!.Supports(NetworkInterfaceComponent.IPv4), Is.True);
        }

        [Test]
        public void Resolve_EmptyPreferred_TreatedAsNull()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                string.Empty,
                AddressFamily.InterNetwork);
            if (resolved is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }
            Assert.That(resolved!.Supports(NetworkInterfaceComponent.IPv4), Is.True);
        }

        [Test]
        public void Resolve_ByName_MatchesInterface()
        {
            NetworkInterface? any = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (any is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }

            NetworkInterface? byName = UdpNetworkInterfaceResolver.Resolve(
                any!.Name,
                AddressFamily.InterNetwork);
            Assert.That(byName, Is.Not.Null);
            Assert.That(byName!.Id, Is.EqualTo(any.Id));
        }

        [Test]
        public void Resolve_ByDescription_MatchesInterface()
        {
            NetworkInterface? any = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (any is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }

            NetworkInterface? byDescription = UdpNetworkInterfaceResolver.Resolve(
                any!.Description,
                AddressFamily.InterNetwork);
            Assert.That(byDescription, Is.Not.Null);
            Assert.That(byDescription!.Id, Is.EqualTo(any.Id));
        }

        [Test]
        public void Resolve_ByIp_MatchesInterface()
        {
            NetworkInterface? any = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (any is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }

            string? ip = null;
            foreach (UnicastIPAddressInformation entry in any!.GetIPProperties().UnicastAddresses)
            {
                if (entry.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ip = entry.Address.ToString();
                    break;
                }
            }
            if (ip is null)
            {
                Assert.Ignore("Resolved interface has no IPv4 unicast address.");
            }

            NetworkInterface? byIp = UdpNetworkInterfaceResolver.Resolve(
                ip,
                AddressFamily.InterNetwork);
            Assert.That(byIp, Is.Not.Null);
            Assert.That(byIp!.Id, Is.EqualTo(any.Id));
        }

        [Test]
        public void Resolve_UnknownName_FallsBackToDefault()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                "this-nic-does-not-exist-xyz",
                AddressFamily.InterNetwork);
            if (resolved is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }
            Assert.That(resolved!.Supports(NetworkInterfaceComponent.IPv4), Is.True);
        }

        [Test]
        public void Resolve_UnknownIp_FallsBackToDefault()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                "192.0.2.123",
                AddressFamily.InterNetwork);
            if (resolved is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
            }
            Assert.That(resolved!.Supports(NetworkInterfaceComponent.IPv4), Is.True);
        }

        [Test]
        public void Resolve_UnknownAddressFamily_ReturnsNull()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.AppleTalk);
            Assert.That(resolved, Is.Null);
        }
    }
}
