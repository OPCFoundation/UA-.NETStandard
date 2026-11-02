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
using Opc.Ua.Server.Network;

namespace Opc.Ua.Samples.Network
{
    /// <summary>
    /// Repeatable synthetic acquisitions; no operation contacts a network driver or switch.
    /// </summary>
    internal static class NetworkScenario
    {
        /// <summary>
        /// Creates physical and VLAN interfaces, shared mappings, both stream directions and LLDP.
        /// </summary>
        internal static ReferenceNetworkModelProvider Create()
        {
            return new NetworkModelBuilder()
                .AddMappingTable(new NetworkMappingTableSnapshot("default")
                {
                    CanEdit = true,
                    Entries =
                    [
                        new PriorityMappingEntryType
                        {
                            MappingUri = "urn:opcfoundation:network-sample:priority",
                            PriorityLabel = "Control",
                            PriorityValue_PCP = 6,
                            PriorityValue_DSCP = 48
                        }
                    ]
                })
                .AddInterface(new NetworkInterfaceSnapshot("physical", "eth0")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000,
                    PhysicalAddress = "02:00:00:00:00:01",
                    MappingTableId = "default",
                    VlanTagCapable = true,
                    Ethernet = new NetworkEthernetSnapshot(1000, Duplex.Full, 1522)
                    {
                        NegotiationStatus = NegotiationStatus.Complete
                    },
                    SrClasses = [new NetworkSrClassSnapshot(6, 3, 2)]
                })
                .AddInterface(new NetworkInterfaceSnapshot("vlan", "eth0.2")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000,
                    LowerLayerIds = ["physical"],
                    VlanId = 2
                })
                .AddStream(CreateStream(talker: true))
                .AddStream(CreateStream(talker: false))
                .WithLldp(new NetworkLldpSnapshot(
                    ChassisIdSubtype.MacAddress, "02:00:00:00:00:01",
                    "Simulated endpoint", "Part 22 reference provider; no hardware control")
                {
                    Ports =
                    [
                        new NetworkLldpPortSnapshot(
                            "port0", "eth0", ByteString.From(new byte[] { 1, 128, 194, 0, 0, 14 }.AsSpan()),
                            PortIdSubtype.InterfaceName, "eth0")
                    ]
                })
                .Build(Epoch, StatusCodes.Good);
        }

        /// <summary>
        /// Moves both streams through configuring, operational, failure and recovery states.
        /// A neighbor appears while operational and is removed during failure.
        /// </summary>
        internal static NetworkModelSnapshot Advance(NetworkModelSnapshot current, int step)
        {
            int phase = step % 4;
            TsnStreamState state = phase switch
            {
                0 => TsnStreamState.Configuring,
                1 or 3 => TsnStreamState.Operational,
                _ => TsnStreamState.Error
            };
            NetworkStreamSnapshot[] streams = current.Streams.Span.ToArray();
            for (int index = 0; index < streams.Length; index++)
            {
                streams[index] = streams[index] with
                {
                    State = state,
                    Status = new NetworkStreamStatusSnapshot(
                        phase == 2 ? TsnFailureCode.InsufficientBandwidth : TsnFailureCode.NoFailure)
                    {
                        TalkerStatus = streams[index].IsTalker
                            ? phase == 2 ? TsnTalkerStatus.Failed : TsnTalkerStatus.Ready
                            : null,
                        ListenerStatus = !streams[index].IsTalker
                            ? phase == 2 ? TsnListenerStatus.Failed : TsnListenerStatus.Ready
                            : null,
                        FailureSystemIdentifiers = phase == 2
                            ? [ByteString.From(new byte[] { 2, 0, 0, 0, 0, 2, 0, 1 }.AsSpan())]
                            : []
                    }
                };
            }
            NetworkLldpSnapshot lldp = current.Lldp
                ?? throw new InvalidOperationException("The scenario requires LLDP.");
            NetworkLldpPortSnapshot port = lldp.Ports[0] with
            {
                Neighbors = phase is 1 or 3
                    ?
                    [
                        new NetworkLldpNeighborSnapshot(
                            "switch", (uint)step, 1, ChassisIdSubtype.MacAddress,
                            "02:00:00:00:00:02", PortIdSubtype.InterfaceName, "port1")
                        {
                            SystemName = "Simulated switch"
                        }
                    ]
                    : []
            };
            return current with
            {
                Timestamp = Epoch.AddSeconds(step + 1),
                Streams = ArrayOf.Wrapped(streams),
                Lldp = lldp with { Ports = [port] }
            };
        }

        /// <summary>
        /// Creates one complete simulated TSN stream.
        /// </summary>
        private static NetworkStreamSnapshot CreateStream(bool talker)
        {
            return new NetworkStreamSnapshot(talker ? "talker" : "listener", "Control", talker)
            {
                StreamId = ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1, 0, 1 }.AsSpan()),
                State = TsnStreamState.Disabled,
                AccumulatedLatency = 1000,
                SrClassId = 6,
                Traffic = new NetworkTrafficSnapshot(1, 512, 1, 8000),
                SourceAddress = ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                DestinationAddress = ByteString.From(new byte[] { 145, 224, 240, 0, 0, 1 }.AsSpan()),
                VlanTag = new NetworkVlanTagSnapshot(2, 3),
                Interfaces = [new NetworkStreamInterfaceSnapshot("port0", "02:00:00:00:00:01")
                {
                    InterfaceName = "eth0",
                    Offset = 100
                }]
            };
        }

        /// <summary>Deterministic acquisition origin, unrelated to host time.</summary>
        private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
