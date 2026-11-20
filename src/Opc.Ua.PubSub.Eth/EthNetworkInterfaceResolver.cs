/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Resolves a preferred network interface name to a
    /// <see cref="NetworkInterface"/> for the Ethernet transport.
    /// </summary>
    public static class EthNetworkInterfaceResolver
    {
        /// <summary>
        /// Resolves the supplied preferred interface name (matched
        /// case-insensitively against <see cref="NetworkInterface.Name"/>
        /// and <see cref="NetworkInterface.Description"/>) to a usable
        /// operational interface.
        /// </summary>
        /// <param name="preferredInterface">
        /// Preferred NIC name, or <see langword="null"/> / empty to pick
        /// the first operational non-loopback interface.
        /// </param>
        /// <returns>
        /// The matching interface, or <see langword="null"/> when none
        /// can be resolved (for example in an environment without the
        /// requested adapter).
        /// </returns>
        public static NetworkInterface? Resolve(string? preferredInterface)
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

            if (!string.IsNullOrEmpty(preferredInterface))
            {
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface candidate = interfaces[i];
                    if (string.Equals(
                            candidate.Name,
                            preferredInterface,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            candidate.Description,
                            preferredInterface,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                return null;
            }

            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface candidate = interfaces[i];
                if (candidate.OperationalStatus == OperationalStatus.Up &&
                    candidate.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
