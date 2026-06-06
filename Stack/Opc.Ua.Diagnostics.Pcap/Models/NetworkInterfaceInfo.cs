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

using System.Collections.Generic;

namespace Opc.Ua.Diagnostics.Pcap.Models
{
    /// <summary>
    /// Describes a local network interface returned by
    /// <c>list_interfaces</c>.
    /// </summary>
    public sealed class NetworkInterfaceInfo
    {
        /// <summary>
        /// Device name as understood by libpcap (e.g.
        /// <c>\Device\NPF_{guid}</c>).
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Friendly name (e.g. <c>Ethernet</c>).
        /// </summary>
        public string? FriendlyName { get; init; }

        /// <summary>
        /// Free-form description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Bound IP addresses.
        /// </summary>
        public IReadOnlyList<string> Addresses { get; init; } = [];

        /// <summary>
        /// The link-layer type (e.g. <c>Ethernet</c>).
        /// </summary>
        public string? LinkType { get; init; }

        /// <summary>
        /// Whether this is the loopback interface.
        /// </summary>
        public bool IsLoopback { get; init; }
    }
}
