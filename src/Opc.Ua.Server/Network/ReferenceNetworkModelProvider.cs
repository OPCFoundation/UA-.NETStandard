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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// In-memory simulated network. No operation changes host networking or reserves bandwidth.
    /// State is lost on restart; applications can retain a snapshot explicitly.
    /// </summary>
    public sealed class ReferenceNetworkModelProvider : INetworkModelProvider
    {
        /// <summary>
        /// Creates a simulator using an explicit acquisition rather than invented hardware data.
        /// </summary>
        public ReferenceNetworkModelProvider(NetworkModelSnapshot initial)
        {
            if (initial is null)
            {
                throw new ArgumentNullException(nameof(initial));
            }
            ValidateSnapshot(initial);
            NetworkModelSnapshot copy = Copy(initial);
            ValidateSnapshot(copy);
            m_snapshot = copy with { Revision = 0 };
        }

        /// <inheritdoc/>
        public ValueTask<NetworkModelSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<NetworkModelSnapshot>(Copy(m_snapshot));
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<NetworkModelSnapshot> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Channel<NetworkModelSnapshot> channel = Channel.CreateBounded<NetworkModelSnapshot>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            lock (m_gate)
            {
                m_watchers.Add(channel);
                channel.Writer.TryWrite(Copy(m_snapshot));
            }
            try
            {
                await foreach (NetworkModelSnapshot snapshot in channel.Reader
                    .ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return Copy(snapshot);
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_watchers.Remove(channel);
                }
                channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Replaces the simulated acquisition when the snapshot is based on the current provider revision.
        /// The provider assigns the next revision after accepting the replacement.
        /// </summary>
        public void Publish(NetworkModelSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            ValidateSnapshot(snapshot);
            NetworkModelSnapshot copy = Copy(snapshot);
            ValidateSnapshot(copy);
            lock (m_gate)
            {
                if (copy.Revision != m_snapshot.Revision)
                {
                    throw new ArgumentException(
                        "A snapshot must be based on the current provider revision.", nameof(snapshot));
                }
                if (copy.Timestamp < m_snapshot.Timestamp)
                {
                    throw new ArgumentException(
                        "A snapshot cannot precede the current acquisition timestamp.", nameof(snapshot));
                }
                m_snapshot = copy with { Revision = checked(m_snapshot.Revision + 1) };
                NotifyWatchers();
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> EditPriorityMappingAsync(
            string tableId,
            PriorityMappingEntryType entry,
            bool delete,
            CancellationToken cancellationToken = default)
        {
            if (tableId is null)
            {
                throw new ArgumentNullException(nameof(tableId));
            }
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            cancellationToken.ThrowIfCancellationRequested();
            PriorityMappingEntryType entryCopy = (PriorityMappingEntryType)entry.Clone();
            lock (m_gate)
            {
                if (StatusCode.IsBad(m_snapshot.StatusCode))
                {
                    return new ValueTask<StatusCode>(m_snapshot.StatusCode);
                }
                for (int index = 0; index < m_snapshot.MappingTables.Count; index++)
                {
                    NetworkMappingTableSnapshot table = m_snapshot.MappingTables[index];
                    if (!string.Equals(table.Id, tableId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!table.CanEdit)
                    {
                        return Result(StatusCodes.BadNotSupported);
                    }
                    List<PriorityMappingEntryType> entries = table.Entries.ToList();
                    int existing = entries.FindIndex(candidate =>
                        candidate.MappingUri == entryCopy.MappingUri &&
                        candidate.PriorityLabel == entryCopy.PriorityLabel);
                    if (delete)
                    {
                        if (existing < 0)
                        {
                            return Result(StatusCodes.BadBrowseNameInvalid);
                        }
                        entries.RemoveAt(existing);
                    }
                    else
                    {
                        if (existing >= 0)
                        {
                            return Result(StatusCodes.BadIndexRangeInvalid);
                        }
                        StatusCode validation = NetworkModelSnapshotValidator.ValidatePriorityMappingEntry(entryCopy);
                        if (StatusCode.IsBad(validation))
                        {
                            return new ValueTask<StatusCode>(validation);
                        }
                        entries.Add(entryCopy);
                    }
                    NetworkMappingTableSnapshot[] tables = m_snapshot.MappingTables.Span.ToArray();
                    tables[index] = table with { Entries = ArrayOf.Wrapped(entries.ToArray()) };
                    m_snapshot = m_snapshot with
                    {
                        Revision = checked(m_snapshot.Revision + 1),
                        MappingTables = ArrayOf.Wrapped(tables)
                    };
                    NotifyWatchers();
                    return Result(StatusCodes.Good);
                }
                return Result(StatusCodes.BadNotFound);
            }
        }

        /// <summary>
        /// Wraps a protocol result without allocating a task.
        /// </summary>
        private static ValueTask<StatusCode> Result(StatusCode code)
        {
            return new ValueTask<StatusCode>(code);
        }

        /// <summary>
        /// Rejects invalid identities, value ranges and fixed-size fields before a snapshot
        /// becomes visible to concurrent readers.
        /// </summary>
        private static void ValidateSnapshot(NetworkModelSnapshot snapshot)
        {
            if (!NetworkModelSnapshotValidator.IsUtcTimestamp(snapshot.Timestamp))
            {
                throw new ArgumentException("Snapshot acquisition timestamps must be UTC.", nameof(snapshot));
            }
            var interfaceIds = new HashSet<string>(StringComparer.Ordinal);
            var interfaceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (NetworkInterfaceSnapshot networkInterface in snapshot.Interfaces)
            {
                if (networkInterface is null ||
                    string.IsNullOrWhiteSpace(networkInterface.Id) ||
                    string.IsNullOrWhiteSpace(networkInterface.IfName) ||
                    !interfaceIds.Add(networkInterface.Id) ||
                    !interfaceNames.Add(networkInterface.IfName))
                {
                    throw new ArgumentException("Interface identities and ifNames must be non-empty and unique.",
                        nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.IsDefinedEnum(networkInterface.AdminStatus) ||
                    !NetworkModelSnapshotValidator.IsDefinedEnum(networkInterface.OperStatus) ||
                    (networkInterface.Ethernet is { } ethernet &&
                        (!NetworkModelSnapshotValidator.IsDefinedEnum(ethernet.Duplex) ||
                            (ethernet.NegotiationStatus is { } negotiation &&
                                !NetworkModelSnapshotValidator.IsDefinedEnum(negotiation)))))
                {
                    throw new ArgumentException("Interface enum values must be defined.", nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.IsValidInterfaceVlanId(networkInterface.VlanId))
                {
                    throw new ArgumentException(
                        "Interface VLAN identifiers must be between zero and 4094.", nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.AreUniqueSrClassIds(networkInterface.SrClasses))
                {
                    throw new ArgumentException(
                        "SR class identifiers must be unique within an interface.", nameof(snapshot));
                }
                foreach (NetworkSrClassSnapshot srClass in networkInterface.SrClasses)
                {
                    if (srClass is null || srClass.Id > 7 ||
                        srClass.Priority > 7)
                    {
                        throw new ArgumentException(
                            "SR classes require unique IDs between zero and seven and valid PCP/VLAN values.",
                            nameof(snapshot));
                    }
                    if (!NetworkModelSnapshotValidator.IsValidSrClassVid(srClass.Vid))
                    {
                        throw new ArgumentException(
                            "SR class VLAN identifiers must be between zero and 4094.", nameof(snapshot));
                    }
                }
            }

            var tableIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NetworkMappingTableSnapshot table in snapshot.MappingTables)
            {
                if (table is null || string.IsNullOrWhiteSpace(table.Id) || !tableIds.Add(table.Id))
                {
                    throw new ArgumentException("Mapping table identities must be non-empty and unique.",
                        nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.AreValidPriorityMappingEntries(table.Entries))
                {
                    throw new ArgumentException(
                        "Priority mapping entries must have unique non-empty keys and valid PCP/DSCP values.",
                        nameof(snapshot));
                }
            }

            var streamIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NetworkStreamSnapshot stream in snapshot.Streams)
            {
                if (stream is null ||
                    string.IsNullOrWhiteSpace(stream.Id) ||
                    string.IsNullOrWhiteSpace(stream.Name) ||
                    !streamIds.Add(stream.Id) ||
                    !NetworkModelSnapshotValidator.IsValidStreamId(stream.StreamId) ||
                    !NetworkModelSnapshotValidator.AreValidStreamMacAddresses(
                        stream.SourceAddress, stream.DestinationAddress) ||
                    (stream.VlanTag is { } tag && (tag.VlanId > 4095 || tag.PriorityCodePoint > 7)) ||
                    (stream.Traffic is { } traffic && !NetworkModelSnapshotValidator.IsValidTraffic(traffic)))
                {
                    throw new ArgumentException(
                        "Stream identities, fixed-size fields and traffic/VLAN values must be valid.",
                        nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.IsDefinedEnum(stream.State))
                {
                    throw new ArgumentException("Stream state values must be defined.", nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.IsValidSrClassId(stream.SrClassId))
                {
                    throw new ArgumentException(
                        "Stream SR class identifiers must be between zero and seven.", nameof(snapshot));
                }
                if (stream.Status is { } status)
                {
                    if (!NetworkModelSnapshotValidator.IsDefinedEnum(status.FailureCode) ||
                        (status.TalkerStatus is { } talker &&
                            !NetworkModelSnapshotValidator.IsDefinedEnum(talker)) ||
                        (status.ListenerStatus is { } listener &&
                            !NetworkModelSnapshotValidator.IsDefinedEnum(listener)))
                    {
                        throw new ArgumentException("Stream status values must be defined.", nameof(snapshot));
                    }
                    if (!NetworkModelSnapshotValidator.AreValidFailureSystemIdentifiers(
                        status.FailureSystemIdentifiers))
                    {
                        throw new ArgumentException(
                            "Failure-system identifiers must contain eight bytes.", nameof(snapshot));
                    }
                }
                if (!NetworkModelSnapshotValidator.AreUniqueStreamInterfaceIds(stream.Interfaces))
                {
                    throw new ArgumentException(
                        "Stream interface identities must be non-empty and unique.", nameof(snapshot));
                }
                foreach (NetworkStreamInterfaceSnapshot streamInterface in stream.Interfaces)
                {
                    if (streamInterface is null ||
                        string.IsNullOrWhiteSpace(streamInterface.MacAddress))
                    {
                        throw new ArgumentException(
                            "Stream interface identities must be non-empty and unique.",
                            nameof(snapshot));
                    }
                }
            }

            if (snapshot.Lldp is { } lldp)
            {
                if (string.IsNullOrWhiteSpace(lldp.ChassisId) ||
                    string.IsNullOrWhiteSpace(lldp.SystemName) ||
                    string.IsNullOrWhiteSpace(lldp.SystemDescription))
                {
                    throw new ArgumentException("LLDP local-system identifiers must be non-empty.",
                        nameof(snapshot));
                }
                if (!NetworkModelSnapshotValidator.IsDefinedEnum(lldp.ChassisIdSubtype))
                {
                    throw new ArgumentException("LLDP chassis identifier subtype must be defined.", nameof(snapshot));
                }
                var portIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (NetworkLldpPortSnapshot port in lldp.Ports)
                {
                    if (port is null ||
                        string.IsNullOrWhiteSpace(port.Id) ||
                        string.IsNullOrWhiteSpace(port.IfName) ||
                        string.IsNullOrWhiteSpace(port.PortId) ||
                        !portIds.Add(port.Id) ||
                        !NetworkModelSnapshotValidator.IsValidLldpDestinationMacAddress(
                            port.DestinationMacAddress))
                    {
                        throw new ArgumentException("LLDP port identities and MAC addresses must be valid.",
                            nameof(snapshot));
                    }
                    if (!NetworkModelSnapshotValidator.IsDefinedEnum(port.PortIdSubtype))
                    {
                        throw new ArgumentException("LLDP port identifier subtype must be defined.", nameof(snapshot));
                    }
                    if (!NetworkModelSnapshotValidator.AreValidLldpManagementAddressTxPorts(
                        port.ManagementAddressTxPort))
                    {
                        throw new ArgumentException(
                            "LLDP transmit management addresses must contain their mandatory fields and defined " +
                            "interface subtypes.", nameof(snapshot));
                    }
                    var neighborIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (NetworkLldpNeighborSnapshot neighbor in port.Neighbors)
                    {
                        if (neighbor is null ||
                            string.IsNullOrWhiteSpace(neighbor.Id) ||
                            !neighborIds.Add(neighbor.Id) ||
                            string.IsNullOrWhiteSpace(neighbor.ChassisId) ||
                            string.IsNullOrWhiteSpace(neighbor.PortId))
                        {
                            throw new ArgumentException(
                                "LLDP neighbor identities must be non-empty and unique per port.",
                                nameof(snapshot));
                        }
                        if (!NetworkModelSnapshotValidator.AreValidLldpManagementAddresses(
                                neighbor.ManagementAddress) ||
                            !NetworkModelSnapshotValidator.AreValidLldpTlvs(neighbor.RemoteUnknownTlv))
                        {
                            throw new ArgumentException(
                                "LLDP remote management addresses and unknown TLVs must be valid.",
                                nameof(snapshot));
                        }
                        if (!NetworkModelSnapshotValidator.IsDefinedEnum(neighbor.ChassisIdSubtype) ||
                            !NetworkModelSnapshotValidator.IsDefinedEnum(neighbor.PortIdSubtype))
                        {
                            throw new ArgumentException(
                                "LLDP neighbor identifier subtypes must be defined.", nameof(snapshot));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Publishes an independent copy of the complete latest revision to active observers.
        /// </summary>
        private void NotifyWatchers()
        {
            foreach (Channel<NetworkModelSnapshot> channel in m_watchers.ToArray())
            {
                if (!channel.Writer.TryWrite(Copy(m_snapshot)))
                {
                    m_watchers.Remove(channel);
                }
            }
        }

        /// <summary>
        /// Separates all mutable generated structures and array storage from provider ownership.
        /// </summary>
        private static NetworkModelSnapshot Copy(NetworkModelSnapshot snapshot)
        {
            NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                interfaces[index] = interfaces[index] with
                {
                    LowerLayerIds = ArrayOf.Wrapped(interfaces[index].LowerLayerIds.Span.ToArray()),
                    Ethernet = interfaces[index].Ethernet is { } ethernet ? ethernet with { } : null,
                    SrClasses = ArrayOf.Wrapped(interfaces[index].SrClasses.Span.ToArray())
                };
            }
            NetworkMappingTableSnapshot[] tables = snapshot.MappingTables.Span.ToArray();
            for (int index = 0; index < tables.Length; index++)
            {
                PriorityMappingEntryType[] entries = tables[index].Entries.Span.ToArray();
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    entries[entryIndex] = (PriorityMappingEntryType)entries[entryIndex].Clone();
                }
                tables[index] = tables[index] with { Entries = ArrayOf.Wrapped(entries) };
            }
            NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
            for (int index = 0; index < streams.Length; index++)
            {
                streams[index] = Copy(streams[index]);
            }
            NetworkLldpSnapshot? lldp = snapshot.Lldp is { } snapshotLldp
                ? Copy(snapshotLldp)
                : null;
            return snapshot with
            {
                Interfaces = ArrayOf.Wrapped(interfaces),
                MappingTables = ArrayOf.Wrapped(tables),
                Streams = ArrayOf.Wrapped(streams),
                Lldp = lldp
            };
        }

        /// <summary>
        /// Copies the mutable nested collections in a stream acquisition.
        /// </summary>
        private static NetworkStreamSnapshot Copy(NetworkStreamSnapshot stream)
        {
            NetworkStreamInterfaceSnapshot[] interfaces = stream.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                interfaces[index] = interfaces[index] with { };
            }
            return stream with
            {
                StreamId = ByteString.From(stream.StreamId.ToArray()),
                Traffic = stream.Traffic is { } traffic ? traffic with { } : null,
                Status = stream.Status is { } status
                    ? status with
                    {
                        FailureSystemIdentifiers = CopyByteStrings(status.FailureSystemIdentifiers)
                    }
                    : null,
                Interfaces = ArrayOf.Wrapped(interfaces),
                DestinationAddress = ByteString.From(stream.DestinationAddress.ToArray()),
                SourceAddress = ByteString.From(stream.SourceAddress.ToArray()),
                VlanTag = stream.VlanTag is { } vlanTag ? vlanTag with { } : null
            };
        }

        /// <summary>
        /// Copies the LLDP acquisition and all current port neighbors.
        /// </summary>
        private static NetworkLldpSnapshot Copy(NetworkLldpSnapshot lldp)
        {
            NetworkLldpPortSnapshot[] ports = lldp.Ports.Span.ToArray();
            for (int portIndex = 0; portIndex < ports.Length; portIndex++)
            {
                NetworkLldpNeighborSnapshot[] neighbors = ports[portIndex].Neighbors.Span.ToArray();
                for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                {
                    neighbors[neighborIndex] = Copy(neighbors[neighborIndex]);
                }
                ports[portIndex] = ports[portIndex] with
                {
                    DestinationMacAddress = ByteString.From(ports[portIndex].DestinationMacAddress.ToArray()),
                    ManagementAddressTxPort = CopyManagementAddressTxPorts(
                        ports[portIndex].ManagementAddressTxPort),
                    Neighbors = ArrayOf.Wrapped(neighbors)
                };
            }
            return lldp with { Ports = ArrayOf.Wrapped(ports) };
        }

        /// <summary>
        /// Copies mutable encoded management-address structures.
        /// </summary>
        private static ArrayOf<LldpManagementAddressTxPortType> CopyManagementAddressTxPorts(
            ArrayOf<LldpManagementAddressTxPortType> values)
        {
            LldpManagementAddressTxPortType[] copies = values.Span.ToArray();
            for (int index = 0; index < copies.Length; index++)
            {
                LldpManagementAddressTxPortType value = copies[index];
                copies[index] = new LldpManagementAddressTxPortType
                {
                    AddressSubtype = value.AddressSubtype,
                    ManAddress = value.ManAddress,
                    TxEnable = value.TxEnable,
                    AddrLen = value.AddrLen,
                    IfSubtype = value.IfSubtype,
                    IfId = value.IfId
                };
            }
            return ArrayOf.Wrapped(copies);
        }

        /// <summary>
        /// Copies mutable encoded management addresses and TLV payload storage.
        /// </summary>
        private static NetworkLldpNeighborSnapshot Copy(NetworkLldpNeighborSnapshot neighbor)
        {
            LldpManagementAddressType[] addresses = neighbor.ManagementAddress.Span.ToArray();
            for (int index = 0; index < addresses.Length; index++)
            {
                LldpManagementAddressType value = addresses[index];
                addresses[index] = new LldpManagementAddressType
                {
                    AddressSubtype = value.AddressSubtype,
                    Address = value.Address,
                    IfSubtype = value.IfSubtype,
                    IfId = value.IfId
                };
            }
            LldpTlvType[] tlvs = neighbor.RemoteUnknownTlv.Span.ToArray();
            for (int index = 0; index < tlvs.Length; index++)
            {
                LldpTlvType value = tlvs[index];
                tlvs[index] = new LldpTlvType
                {
                    TlvType = value.TlvType,
                    TlvInfo = ByteString.From(value.TlvInfo.ToArray().AsSpan())
                };
            }
            return neighbor with
            {
                ManagementAddress = ArrayOf.Wrapped(addresses),
                RemoteUnknownTlv = ArrayOf.Wrapped(tlvs)
            };
        }

        /// <summary>
        /// Copies each byte string into independent storage.
        /// </summary>
        private static ArrayOf<ByteString> CopyByteStrings(ArrayOf<ByteString> values)
        {
            ByteString[] copies = values.Span.ToArray();
            for (int index = 0; index < copies.Length; index++)
            {
                copies[index] = ByteString.From(copies[index].ToArray());
            }
            return ArrayOf.Wrapped(copies);
        }

        /// <summary>Protects the current simulated acquisition.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Provider-owned acquisition.</summary>
        private NetworkModelSnapshot m_snapshot;

        /// <summary>Active snapshot observers, serialized with state publication by <see cref="m_gate"/>.</summary>
        private readonly List<Channel<NetworkModelSnapshot>> m_watchers = [];
    }
}
