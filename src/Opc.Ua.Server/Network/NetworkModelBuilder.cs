// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            if (networkInterface is null)
            {
                throw new ArgumentNullException(nameof(networkInterface));
            }
            m_interfaces.Add(networkInterface);
            return this;
        }

        /// <summary>
        /// Adds a priority table that interfaces can reference by stable identity.
        /// </summary>
        public NetworkModelBuilder AddMappingTable(NetworkMappingTableSnapshot table)
        {
            if (table is null)
            {
                throw new ArgumentNullException(nameof(table));
            }
            m_tables.Add(table);
            return this;
        }

        /// <summary>
        /// Adds a talker or listener with its traffic, reservation and interface parameters.
        /// </summary>
        public NetworkModelBuilder AddStream(NetworkStreamSnapshot stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
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
