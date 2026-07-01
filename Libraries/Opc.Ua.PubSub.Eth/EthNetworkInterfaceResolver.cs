/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
