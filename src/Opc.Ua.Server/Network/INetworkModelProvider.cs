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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Supplies Part 22 device state without coupling the server to a network driver.
    /// The application owns the provider and its persistence. Reads return independent,
    /// coherent snapshots; revisions increase during one provider lifetime.
    /// </summary>
    public interface INetworkModelProvider
    {
        /// <summary>
        /// Reads all currently available capabilities. Unavailable data carries bad quality;
        /// unsupported optional capabilities are omitted. Failures must not return healthy defaults.
        /// </summary>
        ValueTask<NetworkModelSnapshot> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Observes an initial coherent acquisition and subsequent changes. Implementations may
        /// coalesce intermediate snapshots, but every yielded snapshot reflects a complete revision.
        /// </summary>
        IAsyncEnumerable<NetworkModelSnapshot> WatchAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits a mapping edit before returning its result. Implementations serialize edits
        /// with acquisition and persist accepted edits when persistence is configured.
        /// </summary>
        ValueTask<StatusCode> EditPriorityMappingAsync(
            string tableId,
            PriorityMappingEntryType entry,
            bool delete,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One coherent acquisition of the supported Part 22 model.
    /// </summary>
    public sealed record NetworkModelSnapshot
    {
        /// <summary>
        /// Provider generation, increasing after each change.
        /// </summary>
        public long Revision { get; init; }

        /// <summary>
        /// UTC acquisition timestamp, not the time at which the server publishes the snapshot.
        /// </summary>
        public required DateTime Timestamp { get; init; }

        /// <summary>
        /// Quality shared by values in this acquisition.
        /// </summary>
        public required StatusCode StatusCode { get; init; }

        /// <summary>
        /// Physical and virtual interfaces with stable application-owned identities.
        /// </summary>
        public ArrayOf<NetworkInterfaceSnapshot> Interfaces { get; init; }

        /// <summary>
        /// Independently addressable priority-mapping tables.
        /// </summary>
        public ArrayOf<NetworkMappingTableSnapshot> MappingTables { get; init; }

        /// <summary>
        /// Configured talker and listener streams.
        /// </summary>
        public ArrayOf<NetworkStreamSnapshot> Streams { get; init; }

        /// <summary>
        /// LLDP acquisition, omitted when no LLDP source is configured.
        /// </summary>
        public NetworkLldpSnapshot? Lldp { get; init; }
    }

    /// <summary>
    /// IETF interface parameters. IfName becomes the instance BrowseName.
    /// </summary>
    public sealed record NetworkInterfaceSnapshot(string Id, string IfName)
    {
        /// <summary>
        /// Administrative state reported by the provider, not a writable command.
        /// </summary>
        public required InterfaceAdminStatus AdminStatus { get; init; }

        /// <summary>
        /// Observed operational state.
        /// </summary>
        public required InterfaceOperStatus OperStatus { get; init; }

        /// <summary>
        /// IETF speed in bit/s.
        /// </summary>
        public required ulong Speed { get; init; }

        /// <summary>
        /// Optional physical address.
        /// </summary>
        public string? PhysicalAddress { get; init; }

        /// <summary>
        /// Stable identities of lower-layer interfaces.
        /// </summary>
        public ArrayOf<string> LowerLayerIds { get; init; }

        /// <summary>
        /// Explicit table identity; null inherits mappings from lower layers.
        /// </summary>
        public string? MappingTableId { get; init; }

        /// <summary>
        /// IEEE Ethernet port parameters, on a separate component because IEEE speed uses Mbit/s.
        /// </summary>
        public NetworkEthernetSnapshot? Ethernet { get; init; }

        /// <summary>
        /// VLAN-tagging capability, omitted when unknown.
        /// </summary>
        public bool? VlanTagCapable { get; init; }

        /// <summary>
        /// VLAN identity, present only for a VLAN interface.
        /// </summary>
        public ushort? VlanId { get; init; }

        /// <summary>
        /// Stream-reservation classes supported by this interface.
        /// </summary>
        public ArrayOf<NetworkSrClassSnapshot> SrClasses { get; init; }
    }

    /// <summary>
    /// A named priority-mapping table and its supported mutation capability.
    /// </summary>
    public sealed record NetworkMappingTableSnapshot(string Id)
    {
        /// <summary>
        /// Whether AddPriorityMappingEntry and DeletePriorityMappingEntry are supported.
        /// Authorization remains a separate server policy.
        /// </summary>
        public bool CanEdit { get; init; }

        /// <summary>
        /// Mapping entries, keyed by MappingUri and PriorityLabel.
        /// </summary>
        public ArrayOf<PriorityMappingEntryType> Entries { get; init; }
    }

    /// <summary>
    /// IEEE port parameters; Speed is measured in Mbit/s.
    /// </summary>
    public sealed record NetworkEthernetSnapshot(ulong Speed, Duplex Duplex, ushort MaxFrameLength)
    {
        /// <summary>
        /// Optional auto-negotiation observation.
        /// </summary>
        public NegotiationStatus? NegotiationStatus { get; init; }
    }

    /// <summary>
    /// IEEE stream-reservation class parameters.
    /// </summary>
    public sealed record NetworkSrClassSnapshot(byte Id, byte Priority, ushort Vid);

    /// <summary>
    /// IEEE TSN stream parameters, separate from a real reservation or control operation.
    /// </summary>
    public sealed record NetworkStreamSnapshot(string Id, string Name, bool IsTalker)
    {
        /// <summary>
        /// Eight stream-identity bytes.
        /// </summary>
        public required ByteString StreamId { get; init; }

        /// <summary>
        /// Provider-observed lifecycle state.
        /// </summary>
        public required TsnStreamState State { get; init; }

        /// <summary>
        /// Optional accumulated latency in nanoseconds.
        /// </summary>
        public uint? AccumulatedLatency { get; init; }

        /// <summary>
        /// Optional reservation class.
        /// </summary>
        public byte? SrClassId { get; init; }

        /// <summary>
        /// Traffic specification, omitted when unsupported.
        /// </summary>
        public NetworkTrafficSnapshot? Traffic { get; init; }

        /// <summary>
        /// Reservation status, omitted when unsupported.
        /// </summary>
        public NetworkStreamStatusSnapshot? Status { get; init; }

        /// <summary>
        /// Interface-specific configuration.
        /// </summary>
        public ArrayOf<NetworkStreamInterfaceSnapshot> Interfaces { get; init; }

        /// <summary>
        /// Optional stream destination MAC address, exactly six bytes.
        /// </summary>
        public ByteString DestinationAddress { get; init; }

        /// <summary>
        /// Optional stream source MAC address, exactly six bytes.
        /// </summary>
        public ByteString SourceAddress { get; init; }

        /// <summary>
        /// Optional VLAN tag.
        /// </summary>
        public NetworkVlanTagSnapshot? VlanTag { get; init; }
    }

    /// <summary>
    /// Traffic interval and maximum frame limits.
    /// </summary>
    public sealed record NetworkTrafficSnapshot(ushort MaxIntervalFrames, uint MaxFrameSize, uint Numerator,
        uint Denominator);

    /// <summary>
    /// Stream reservation diagnostics. Each failure-system identifier has eight bytes.
    /// </summary>
    public sealed record NetworkStreamStatusSnapshot(TsnFailureCode FailureCode)
    {
        /// <summary>
        /// Talker observation when supported.
        /// </summary>
        public TsnTalkerStatus? TalkerStatus { get; init; }

        /// <summary>
        /// Listener observation when supported.
        /// </summary>
        public TsnListenerStatus? ListenerStatus { get; init; }

        /// <summary>
        /// Failure-system identifiers.
        /// </summary>
        public ArrayOf<ByteString> FailureSystemIdentifiers { get; init; }
    }

    /// <summary>
    /// Stream configuration on one interface, including direction-specific optional offsets.
    /// </summary>
    public sealed record NetworkStreamInterfaceSnapshot(string Id, string MacAddress)
    {
        /// <summary>
        /// IETF ifName where known.
        /// </summary>
        public string? InterfaceName { get; init; }

        /// <summary>
        /// Talker TimeAwareOffset or listener ReceiveOffset in nanoseconds.
        /// </summary>
        public uint? Offset { get; init; }
    }

    /// <summary>
    /// VLAN-tag fields with the Part 22 value ranges.
    /// </summary>
    public sealed record NetworkVlanTagSnapshot(ushort VlanId, byte PriorityCodePoint);

    /// <summary>
    /// LLDP local system and ports supplied by one acquisition.
    /// </summary>
    public sealed record NetworkLldpSnapshot(
        ChassisIdSubtype ChassisIdSubtype, string ChassisId, string SystemName, string SystemDescription)
    {
        /// <summary>
        /// LLDP ports and their current neighbors.
        /// </summary>
        public ArrayOf<NetworkLldpPortSnapshot> Ports { get; init; }
    }

    /// <summary>
    /// LLDP port identity and neighbors. IfName links the port to an IETF interface by name.
    /// </summary>
    public sealed record NetworkLldpPortSnapshot(
        string Id, string IfName, ByteString DestinationMacAddress, PortIdSubtype PortIdSubtype, string PortId)
    {
        /// <summary>
        /// Optional port description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Current neighbors; removal from the acquisition removes their address-space nodes.
        /// </summary>
        public ArrayOf<NetworkLldpNeighborSnapshot> Neighbors { get; init; }
    }

    /// <summary>
    /// Mandatory LLDP neighbor data with optional descriptive information.
    /// </summary>
    public sealed record NetworkLldpNeighborSnapshot(
        string Id, uint TimeMark, uint RemoteIndex, ChassisIdSubtype ChassisIdSubtype,
        string ChassisId, PortIdSubtype PortIdSubtype, string PortId)
    {
        /// <summary>
        /// Optional neighbor system name.
        /// </summary>
        public string? SystemName { get; init; }

        /// <summary>
        /// Optional neighbor system description.
        /// </summary>
        public string? SystemDescription { get; init; }
    }
}
