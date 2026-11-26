// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Resolves the <see cref="NetworkInterface"/> a UDP transport
    /// should bind to. Accepts either a NIC name / description or a
    /// literal IP address bound to a local NIC, and falls back to the
    /// first up-and-running interface that supports the requested
    /// <see cref="AddressFamily"/>.
    /// </summary>
    /// <remarks>
    /// Implements the NIC-selection guidance in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.2">
    /// Part 14 §7.3.2.2 UDP multicast / broadcast</see>: the transport
    /// joins multicast groups and sends multicast datagrams on the
    /// resolved interface, so the operating system does not pick an
    /// unrelated route.
    /// </remarks>
    public static class UdpNetworkInterfaceResolver
    {
        /// <summary>
        /// Resolves the network interface matching
        /// <paramref name="preferred"/>. Returns the first up
        /// interface that supports <paramref name="family"/> when
        /// <paramref name="preferred"/> is <see langword="null"/> /
        /// empty / unresolved.
        /// </summary>
        /// <param name="preferred">
        /// NIC name, description, or literal IP address. May be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="family">
        /// Address family the chosen NIC must support
        /// (IPv4 or IPv6).
        /// </param>
        /// <returns>
        /// The matching <see cref="NetworkInterface"/>, or
        /// <see langword="null"/> if no usable interface was found.
        /// </returns>
        public static NetworkInterface? Resolve(string? preferred, AddressFamily family)
        {
            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(preferred))
            {
                NetworkInterface? byIp = TryResolveByIp(interfaces, preferred, family);
                if (byIp is not null)
                {
                    return byIp;
                }
                NetworkInterface? byName = TryResolveByName(interfaces, preferred, family);
                if (byName is not null)
                {
                    return byName;
                }
            }
            return ResolveDefault(interfaces, family);
        }

        private static NetworkInterface? TryResolveByIp(
            NetworkInterface[] interfaces,
            string preferred,
            AddressFamily family)
        {
            if (!IPAddress.TryParse(preferred, out IPAddress? target))
            {
                return null;
            }
            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface candidate = interfaces[i];
                if (!Supports(candidate, family))
                {
                    continue;
                }
                IPInterfaceProperties properties = candidate.GetIPProperties();
                foreach (UnicastIPAddressInformation entry in properties.UnicastAddresses)
                {
                    if (entry.Address.Equals(target))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static NetworkInterface? TryResolveByName(
            NetworkInterface[] interfaces,
            string preferred,
            AddressFamily family)
        {
            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface candidate = interfaces[i];
                if (!Supports(candidate, family))
                {
                    continue;
                }
                if (string.Equals(candidate.Name, preferred, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Description, preferred, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Id, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static NetworkInterface? ResolveDefault(
            NetworkInterface[] interfaces,
            AddressFamily family)
        {
            NetworkInterface? fallback = null;
            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface candidate = interfaces[i];
                if (!Supports(candidate, family))
                {
                    continue;
                }
                if (candidate.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (candidate.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    fallback ??= candidate;
                    continue;
                }
                return candidate;
            }
            return fallback;
        }

        private static bool Supports(NetworkInterface candidate, AddressFamily family)
        {
            try
            {
                return family switch
                {
                    AddressFamily.InterNetwork => candidate.Supports(NetworkInterfaceComponent.IPv4),
                    AddressFamily.InterNetworkV6 => candidate.Supports(NetworkInterfaceComponent.IPv6),
                    _ => false
                };
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        }
    }
}
