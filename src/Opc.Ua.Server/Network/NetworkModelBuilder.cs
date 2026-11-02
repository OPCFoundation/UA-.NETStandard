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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
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

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Composes an explicit simulated acquisition without reading or changing host networking.
    /// </summary>
    public sealed class NetworkModelBuilder
    {
        /// <summary>
        /// Creates an empty acquisition builder; capabilities are added explicitly.
        /// </summary>
        public NetworkModelBuilder()
        {
        }

        /// <summary>
        /// Adds a physical or layered interface with its optional capabilities.
        /// </summary>
        public NetworkModelBuilder AddInterface(NetworkInterfaceSnapshot networkInterface)
        {
            ArgumentNullException.ThrowIfNull(networkInterface);
            m_interfaces.Add(networkInterface);
            return this;
        }

        /// <summary>
        /// Adds a priority table that interfaces can reference by stable identity.
        /// </summary>
        public NetworkModelBuilder AddMappingTable(NetworkMappingTableSnapshot table)
        {
            ArgumentNullException.ThrowIfNull(table);
            m_tables.Add(table);
            return this;
        }

        /// <summary>
        /// Adds a talker or listener with its traffic, reservation and interface parameters.
        /// </summary>
        public NetworkModelBuilder AddStream(NetworkStreamSnapshot stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            m_streams.Add(stream);
            return this;
        }

        /// <summary>
        /// Configures the LLDP acquisition, or omits LLDP when null.
        /// </summary>
        public NetworkModelBuilder WithLldp(NetworkLldpSnapshot? lldp)
        {
            m_lldp = lldp;
            return this;
        }

        /// <summary>
        /// Validates and copies the acquisition into a new independent in-memory provider.
        /// </summary>
        public ReferenceNetworkModelProvider Build(DateTime timestamp, StatusCode statusCode)
        {
            return new ReferenceNetworkModelProvider(new NetworkModelSnapshot
            {
                Timestamp = timestamp,
                StatusCode = statusCode,
                Interfaces = ArrayOf.Wrapped(m_interfaces.ToArray()),
                MappingTables = ArrayOf.Wrapped(m_tables.ToArray()),
                Streams = ArrayOf.Wrapped(m_streams.ToArray()),
                Lldp = m_lldp
            });
        }

        /// <summary>Interface definitions retained until Build copies them.</summary>
        private readonly List<NetworkInterfaceSnapshot> m_interfaces = [];

        /// <summary>Mapping-table definitions retained until Build copies them.</summary>
        private readonly List<NetworkMappingTableSnapshot> m_tables = [];

        /// <summary>Stream definitions retained until Build copies them.</summary>
        private readonly List<NetworkStreamSnapshot> m_streams = [];

        /// <summary>Optional LLDP acquisition.</summary>
        private NetworkLldpSnapshot? m_lldp;
    }
}
