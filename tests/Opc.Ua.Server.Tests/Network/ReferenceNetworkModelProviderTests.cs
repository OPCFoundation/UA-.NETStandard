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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Network;

namespace Opc.Ua.Server.Tests.Network
{
    /// <summary>
    /// Contract tests for the deterministic in-memory Part 22 provider.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NetworkModel")]
    [Parallelizable]
    public sealed class ReferenceNetworkModelProviderTests
    {
        /// <summary>
        /// Accepts both ends of the normative SR-class identifier range.
        /// </summary>
        [TestCase((byte)0)]
        [TestCase((byte)7)]
        public async Task SrClassIdentifierBoundariesSurviveConstructionAndPublicationAsync(byte id)
        {
            var snapshot = EmptySnapshot() with
            {
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000,
                        SrClasses = [new NetworkSrClassSnapshot(id, 0, 1)]
                    }
                ]
            };
            var provider = new ReferenceNetworkModelProvider(snapshot);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Interfaces[0].SrClasses[0].Id,
                Is.EqualTo(id));

            provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(1) });
            NetworkModelSnapshot actual = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(actual.Interfaces[0].SrClasses[0].Id, Is.EqualTo(id));
            Assert.That(actual.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects invalid identifiers at construction and before replacing an accepted acquisition.
        /// </summary>
        [TestCase((byte)8)]
        [TestCase(byte.MaxValue)]
        public async Task InvalidSrClassIdentifiersDoNotReplaceTheAcquisitionAsync(byte id)
        {
            NetworkModelSnapshot initial = EmptySnapshot();
            var provider = new ReferenceNetworkModelProvider(initial);
            var invalid = initial with
            {
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000,
                        SrClasses = [new NetworkSrClassSnapshot(id, 0, 1)]
                    }
                ]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid),
                Throws.ArgumentException.With.Message.Contains("IDs between zero and seven"));
            Assert.That(() => provider.Publish(invalid),
                Throws.ArgumentException.With.Message.Contains("IDs between zero and seven"));
            NetworkModelSnapshot actual = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(actual.Revision, Is.Zero);
            Assert.That(actual.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(actual.Interfaces.Count, Is.Zero);
        }

        /// <summary>
        /// Accepts an absent stream SR-class identifier and both ends of its defined range.
        /// </summary>
        [TestCase(false, (byte)0)]
        [TestCase(true, (byte)0)]
        [TestCase(true, (byte)7)]
        public async Task StreamSrClassIdBoundariesSurviveConstructionAndPublicationAsync(
            bool includeId, byte id)
        {
            byte? expectedId = includeId ? id : null;
            NetworkModelSnapshot snapshot = EmptySnapshot() with
            {
                Streams =
                [
                    new NetworkStreamSnapshot("stream-1", "test", true)
                    {
                        StreamId = ByteString.From(new byte[8].AsSpan()),
                        State = TsnStreamState.Disabled,
                        SrClassId = includeId ? id : null
                    }
                ]
            };
            var provider = new ReferenceNetworkModelProvider(snapshot);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Streams[0].SrClassId,
                Is.EqualTo(expectedId));

            provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(1) });
            NetworkModelSnapshot actual = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(actual.Streams[0].SrClassId, Is.EqualTo(expectedId));
            Assert.That(actual.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects an out-of-range optional stream SR-class identifier without replacing the acquisition.
        /// </summary>
        [TestCase((byte)8)]
        public async Task PublishRejectsOutOfRangeStreamSrClassIdWithoutReplacingSnapshotAsync(byte id)
        {
            NetworkModelSnapshot initial = EmptySnapshot();
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkModelSnapshot invalid = initial with
            {
                Streams =
                [
                    new NetworkStreamSnapshot("stream-1", "test", true)
                    {
                        StreamId = ByteString.From(new byte[8].AsSpan()),
                        State = TsnStreamState.Disabled,
                        SrClassId = id
                    }
                ]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid),
                Throws.ArgumentException.With.Message.Contains("Stream SR class identifiers"));
            Assert.That(() => provider.Publish(invalid),
                Throws.ArgumentException.With.Message.Contains("Stream SR class identifiers"));
            NetworkModelSnapshot actual = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(actual.Revision, Is.Zero);
            Assert.That(actual.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(actual.Streams.Count, Is.Zero);
        }

        /// <summary>
        /// Applies the stream VLAN tag ranges independently of interface VLAN validation.
        /// </summary>
        [TestCase((ushort)0, (byte)0, true)]
        [TestCase((ushort)4095, (byte)7, true)]
        [TestCase((ushort)4096, (byte)0, false)]
        [TestCase(ushort.MaxValue, (byte)0, false)]
        [TestCase((ushort)1, (byte)8, false)]
        [TestCase((ushort)1, byte.MaxValue, false)]
        public async Task StreamVlanTagBoundariesApplyToConstructionAndPublicationAsync(
            ushort vlanId, byte priority, bool valid)
        {
            NetworkModelSnapshot initial = EmptySnapshot();
            var candidate = initial with
            {
                Streams =
                [
                    new NetworkStreamSnapshot("talker", "test", true)
                    {
                        StreamId = ByteString.From(new byte[8].AsSpan()),
                        State = TsnStreamState.Disabled,
                        VlanTag = new NetworkVlanTagSnapshot(vlanId, priority)
                    }
                ]
            };
            var provider = new ReferenceNetworkModelProvider(initial);
            if (valid)
            {
                var constructed = new ReferenceNetworkModelProvider(candidate);
                NetworkModelSnapshot read = await constructed.ReadAsync().ConfigureAwait(false);
                Assert.That(read.Streams[0].VlanTag, Is.EqualTo(candidate.Streams[0].VlanTag));
                provider.Publish(candidate);
                NetworkModelSnapshot published = await provider.ReadAsync().ConfigureAwait(false);
                Assert.That(published.Streams[0].VlanTag, Is.EqualTo(candidate.Streams[0].VlanTag));
                Assert.That(published.Revision, Is.EqualTo(1));
            }
            else
            {
                Assert.That(() => new ReferenceNetworkModelProvider(candidate), Throws.ArgumentException);
                Assert.That(() => provider.Publish(candidate), Throws.ArgumentException);
                NetworkModelSnapshot retained = await provider.ReadAsync().ConfigureAwait(false);
                Assert.That(retained.Revision, Is.Zero);
                Assert.That(retained.Streams.Count, Is.Zero);
            }
        }

        /// <summary>
        /// Preserves empty acquisitions when optional collections are omitted.
        /// </summary>
        [Test]
        public async Task DefaultCollectionsRemainEmptyAcrossReadAndPublishAsync()
        {
            var initial = new NetworkModelSnapshot
            {
                Timestamp = UnixEpoch,
                StatusCode = StatusCodes.BadNotConnected
            };
            var provider = new ReferenceNetworkModelProvider(initial);

            NetworkModelSnapshot first = await provider.ReadAsync().ConfigureAwait(false);
            provider.Publish(initial with { Timestamp = UnixEpoch.AddSeconds(1) });
            NetworkModelSnapshot second = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(first.Interfaces.Count, Is.Zero);
            Assert.That(first.MappingTables.Count, Is.Zero);
            Assert.That(first.Streams.Count, Is.Zero);
            Assert.That(first.Lldp, Is.Null);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(second.Interfaces.Count, Is.Zero);
            Assert.That(second.MappingTables.Count, Is.Zero);
            Assert.That(second.Streams.Count, Is.Zero);
            Assert.That(second.Lldp, Is.Null);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(second.Timestamp, Is.EqualTo(UnixEpoch.AddSeconds(1)));
            Assert.That(first.Revision, Is.Zero);
            Assert.That(second.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// Copies omitted capability collections and accepts edits to an initially empty table.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DefaultNestedCollectionsSurviveAcquisitionAndMappingEditsAsync(bool includePort)
        {
            byte[] destination = [1, 2, 3, 4, 5, 6];
            byte[] streamId = [1, 2, 3, 4, 5, 6, 7, 8];
            var local = new NetworkLldpSnapshot(
                ChassisIdSubtype.MacAddress, "02:00:00:00:00:01", "simulated", "Simulated network");
            if (includePort)
            {
                local = local with
                {
                    Ports =
                    [
                        new NetworkLldpPortSnapshot(
                            "port-1", "eth0", ByteString.From(destination),
                            PortIdSubtype.InterfaceName, "eth0")
                    ]
                };
            }
            var initial = new NetworkModelSnapshot
            {
                Timestamp = UnixEpoch,
                StatusCode = StatusCodes.Good,
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000
                    }
                ],
                MappingTables = [new NetworkMappingTableSnapshot("table-1") { CanEdit = true }],
                Streams =
                [
                    new NetworkStreamSnapshot("talker", "Simulated talker", true)
                    {
                        StreamId = ByteString.From(streamId),
                        State = TsnStreamState.Disabled,
                        Status = new NetworkStreamStatusSnapshot(TsnFailureCode.NoFailure)
                    }
                ],
                Lldp = local
            };
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkModelSnapshot first = await provider.ReadAsync().ConfigureAwait(false);

            provider.Publish(initial with { Timestamp = UnixEpoch.AddSeconds(1) });
            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test", "Control", 7, 63), false).ConfigureAwait(false);
            NetworkModelSnapshot second = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(first.Interfaces[0].LowerLayerIds.Count, Is.Zero);
            Assert.That(first.Interfaces[0].SrClasses.Count, Is.Zero);
            Assert.That(first.MappingTables[0].Entries.Count, Is.Zero);
            Assert.That(first.Streams[0].Interfaces.Count, Is.Zero);
            Assert.That(first.Streams[0].Status!.FailureSystemIdentifiers.Count, Is.Zero);
            Assert.That(first.Lldp!.Ports.Count, Is.EqualTo(includePort ? 1 : 0));
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(second.Interfaces[0].LowerLayerIds.Count, Is.Zero);
            Assert.That(second.Interfaces[0].SrClasses.Count, Is.Zero);
            Assert.That(second.Streams[0].Interfaces.Count, Is.Zero);
            Assert.That(second.Streams[0].Status!.FailureSystemIdentifiers.Count, Is.Zero);
            Assert.That(second.Lldp!.Ports.Count, Is.EqualTo(includePort ? 1 : 0));
            if (includePort)
            {
                Assert.That(first.Lldp!.Ports[0].Neighbors.Count, Is.Zero);
                Assert.That(second.Lldp!.Ports[0].Neighbors.Count, Is.Zero);
            }
            Assert.That(second.MappingTables[0].Entries.Count, Is.EqualTo(1));
            Assert.That(second.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo("Control"));
            Assert.That(second.Timestamp, Is.EqualTo(UnixEpoch.AddSeconds(1)));
            Assert.That(second.Revision, Is.EqualTo(2));
        }

        /// <summary>
        /// Detaches nested input storage and returns independent acquisitions.
        /// </summary>
        [Test]
        public async Task ReadAndPublishReturnIndependentSnapshotsAsync()
        {
            byte[] streamBytes = [1, 2, 3, 4, 5, 6, 7, 8];
            var entry = CreateEntry("urn:test:priority", "BestEffort", 0, 0);
            PriorityMappingEntryType[] entries = [entry];
            string[] lowerLayers = ["lower"];
            NetworkStreamInterfaceSnapshot[] streamInterfaces =
            [
                new NetworkStreamInterfaceSnapshot("eth0", "00:00:00:00:00:01")
            ];
            NetworkInterfaceSnapshot[] interfaces =
            [
                new NetworkInterfaceSnapshot("eth0", "eth0")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000,
                    LowerLayerIds = ArrayOf.Wrapped(lowerLayers)
                }
            ];
            NetworkStreamSnapshot[] streams =
            [
                new NetworkStreamSnapshot("stream-1", "test", true)
                {
                    StreamId = ByteString.From(streamBytes),
                    State = TsnStreamState.Disabled,
                    Interfaces = ArrayOf.Wrapped(streamInterfaces)
                }
            ];
            var initial = new NetworkModelSnapshot
            {
                Timestamp = UnixEpoch,
                StatusCode = StatusCodes.Good,
                Interfaces = ArrayOf.Wrapped(interfaces),
                MappingTables = ArrayOf.Wrapped(
                [
                    new NetworkMappingTableSnapshot("table-1")
                    {
                        CanEdit = true,
                        Entries = ArrayOf.Wrapped(entries)
                    }
                ]),
                Streams = ArrayOf.Wrapped(streams)
            };
            var provider = new ReferenceNetworkModelProvider(initial);

            interfaces[0] = interfaces[0] with { Id = "replaced", IfName = "replaced" };
            entries[0].PriorityLabel = "mutated";
            lowerLayers[0] = "mutated";
            streamBytes[0] = 99;
            streamInterfaces[0] = new NetworkStreamInterfaceSnapshot("replaced", "replaced");

            NetworkModelSnapshot first = await provider.ReadAsync().ConfigureAwait(false);
            NetworkModelSnapshot second = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(first.Revision, Is.Zero);
            Assert.That(first.Interfaces[0].Id, Is.EqualTo("eth0"));
            Assert.That(first.Interfaces[0].LowerLayerIds[0], Is.EqualTo("lower"));
            Assert.That(first.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo("BestEffort"));
            Assert.That(first.Streams[0].StreamId[0], Is.EqualTo(1));
            Assert.That(first.Streams[0].Interfaces[0].Id, Is.EqualTo("eth0"));
            Assert.That(first.Interfaces[0], Is.Not.SameAs(second.Interfaces[0]));
            Assert.That(first.MappingTables[0].Entries[0], Is.Not.SameAs(second.MappingTables[0].Entries[0]));
        }

        /// <summary>
        /// Assigns the next revision and copies LLDP port and neighbor storage.
        /// </summary>
        [Test]
        public async Task PublishAdvancesRevisionAndDetachesNestedLldpSnapshotAsync()
        {
            byte[] destination = [1, 2, 3, 4, 5, 6];
            NetworkLldpNeighborSnapshot[] neighbors =
            [
                new NetworkLldpNeighborSnapshot(
                    "neighbor-1", 1, 2, ChassisIdSubtype.MacAddress, "00:00:00:00:00:01",
                    PortIdSubtype.MacAddress, "00:00:00:00:00:02")
            ];
            NetworkLldpPortSnapshot[] ports =
            [
                new NetworkLldpPortSnapshot("port-1", "eth0", ByteString.From(destination),
                    PortIdSubtype.InterfaceName, "eth0")
                {
                    Neighbors = ArrayOf.Wrapped(neighbors)
                }
            ];
            var snapshot = EmptySnapshot() with
            {
                Revision = 0,
                Lldp = new NetworkLldpSnapshot(
                    ChassisIdSubtype.MacAddress, "00:00:00:00:00:01", "switch", "reference")
                {
                    Ports = ArrayOf.Wrapped(ports)
                }
            };
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());

            provider.Publish(snapshot);
            destination[0] = 99;
            neighbors[0] = new NetworkLldpNeighborSnapshot(
                "replaced", 0, 0, ChassisIdSubtype.MacAddress, string.Empty, PortIdSubtype.MacAddress, string.Empty);

            NetworkModelSnapshot actual = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(actual.Revision, Is.EqualTo(1));
            Assert.That(actual.Lldp!.Ports[0].DestinationMacAddress[0], Is.EqualTo(1));
            Assert.That(actual.Lldp.Ports[0].Neighbors[0].Id, Is.EqualTo("neighbor-1"));
        }

        /// <summary>
        /// Rejects malformed LLDP identities without replacing the accepted acquisition.
        /// </summary>
        [TestCase("missing destination")]
        [TestCase("short destination")]
        [TestCase("long destination")]
        [TestCase("missing port identity")]
        [TestCase("local chassis subtype")]
        [TestCase("local port subtype")]
        [TestCase("remote chassis subtype")]
        [TestCase("remote port subtype")]
        public async Task MalformedLldpAcquisitionDoesNotReplaceCurrentSnapshotAsync(string invalidField)
        {
            var neighbor = new NetworkLldpNeighborSnapshot(
                "remote", 1, 1, ChassisIdSubtype.MacAddress, "02:00:00:00:00:02",
                PortIdSubtype.InterfaceName, "port1");
            var port = new NetworkLldpPortSnapshot(
                "local", "eth0", ByteString.From(new byte[] { 1, 128, 194, 0, 0, 14 }.AsSpan()),
                PortIdSubtype.InterfaceName, "eth0");
            port = invalidField switch
            {
                "missing destination" => port with { DestinationMacAddress = default },
                "short destination" => port with { DestinationMacAddress = ByteString.From(new byte[5].AsSpan()) },
                "long destination" => port with { DestinationMacAddress = ByteString.From(new byte[7].AsSpan()) },
                "missing port identity" => port with { PortId = " " },
                "local port subtype" => port with { PortIdSubtype = (PortIdSubtype)int.MaxValue },
                _ => port
            };
            neighbor = invalidField switch
            {
                "remote chassis subtype" => neighbor with { ChassisIdSubtype = (ChassisIdSubtype)int.MaxValue },
                "remote port subtype" => neighbor with { PortIdSubtype = (PortIdSubtype)int.MaxValue },
                _ => neighbor
            };
            var lldp = new NetworkLldpSnapshot(
                invalidField == "local chassis subtype" ? (ChassisIdSubtype)int.MaxValue : ChassisIdSubtype.MacAddress,
                "02:00:00:00:00:01", "reference", "simulated endpoint")
            {
                Ports = [port with { Neighbors = [neighbor] }]
            };
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());

            Assert.That(() => provider.Publish(EmptySnapshot() with { Lldp = lldp }), Throws.ArgumentException);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Lldp, Is.Null);
        }

        /// <summary>
        /// Emits the initial acquisition and coalesces pending changes to the latest revision.
        /// </summary>
        [Test]
        public async Task WatchAsyncEmitsInitialAndLatestCompleteSnapshotsAsync()
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());
            await using IAsyncEnumerator<NetworkModelSnapshot> updates =
                provider.WatchAsync().GetAsyncEnumerator();

            Assert.That(await updates.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(updates.Current.Revision, Is.Zero);

            provider.Publish(SnapshotWithTable(canEdit: true));
            provider.Publish(SnapshotWithTable(
                true, CreateEntry("urn:test:priority", "BestEffort", 0, 0)) with { Revision = 1 });

            Assert.That(await updates.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(updates.Current.Revision, Is.EqualTo(2));
            Assert.That(updates.Current.MappingTables[0].Entries.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Ends an active observer when its cancellation token is canceled.
        /// </summary>
        [Test]
        public async Task WatchAsyncHonorsCancellationAsync()
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());
            using var cancellation = new CancellationTokenSource();
            await using IAsyncEnumerator<NetworkModelSnapshot> updates =
                provider.WatchAsync(cancellation.Token).GetAsyncEnumerator();

            Assert.That(await updates.MoveNextAsync().ConfigureAwait(false), Is.True);
            cancellation.Cancel();

            await Assert.ThatAsync(
                async () => await updates.MoveNextAsync().ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects a malformed stream identity without publishing it.
        /// </summary>
        [Test]
        [TestCase(0)]
        [TestCase(7)]
        [TestCase(9)]
        public async Task PublishRejectsMalformedStreamIdentityWithoutReplacingCurrentSnapshotAsync(int length)
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());
            byte[] malformedStreamId = new byte[length];
            NetworkModelSnapshot malformed = EmptySnapshot() with
            {
                Streams = ArrayOf.Wrapped(
                [
                    new NetworkStreamSnapshot("stream-1", "invalid", true)
                    {
                        StreamId = ByteString.From(malformedStreamId.AsSpan()),
                        State = TsnStreamState.Disabled
                    }
                ])
            };

            Assert.That(() => provider.Publish(malformed), Throws.ArgumentException);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Streams.Count, Is.Zero);
        }

        /// <summary>
        /// Rejects malformed failure-system identifier rows without replacing the accepted acquisition.
        /// </summary>
        [Test]
        public async Task PublishRejectsMalformedFailureSystemIdentifierRowsWithoutReplacingSnapshotAsync()
        {
            ByteString firstIdentifier = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.AsSpan());
            ByteString secondIdentifier = ByteString.From(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }.AsSpan());
            NetworkStreamSnapshot stream = new("stream-1", "test", true)
            {
                StreamId = ByteString.From(new byte[8].AsSpan()),
                State = TsnStreamState.Disabled,
                Status = new NetworkStreamStatusSnapshot(TsnFailureCode.NoFailure)
                {
                    FailureSystemIdentifiers = [firstIdentifier, secondIdentifier]
                }
            };
            NetworkModelSnapshot initial = EmptySnapshot() with { Streams = [stream] };
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkStreamSnapshot malformedStream = stream with
            {
                Status = stream.Status! with
                {
                    FailureSystemIdentifiers =
                    [
                        ByteString.From(new byte[7].AsSpan()),
                        ByteString.From(new byte[9].AsSpan())
                    ]
                }
            };
            NetworkModelSnapshot malformed = initial with
            {
                Timestamp = initial.Timestamp.AddSeconds(1),
                Streams = [malformedStream]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(malformed),
                Throws.ArgumentException.With.Message.Contains("Failure-system identifiers"));
            Assert.That(() => provider.Publish(malformed),
                Throws.ArgumentException.With.Message.Contains("Failure-system identifiers"));
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(current.Streams[0].Status!.FailureSystemIdentifiers.ToArray(),
                Is.EqualTo(new[] { firstIdentifier, secondIdentifier }));
        }

        /// <summary>
        /// Rejects malformed stream MAC address fields without replacing the accepted acquisition.
        /// </summary>
        [TestCase(5, 0)]
        [TestCase(0, 6)]
        [TestCase(6, 5)]
        public async Task PublishRejectsInvalidStreamMacAddressesWithoutReplacingSnapshotAsync(
            int destinationLength, int sourceLength)
        {
            NetworkStreamSnapshot stream = new("stream-1", "test", true)
            {
                StreamId = ByteString.From(new byte[8].AsSpan()),
                State = TsnStreamState.Disabled,
                DestinationAddress = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6 }.AsSpan()),
                SourceAddress = ByteString.From(new byte[] { 7, 8, 9, 10, 11, 12 }.AsSpan())
            };
            NetworkModelSnapshot initial = EmptySnapshot() with { Streams = [stream] };
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkStreamSnapshot malformedStream = stream with
            {
                DestinationAddress = ByteString.From(
                    Enumerable.Range(1, destinationLength).Select(value => (byte)value).ToArray().AsSpan()),
                SourceAddress = ByteString.From(
                    Enumerable.Range(7, sourceLength).Select(value => (byte)value).ToArray().AsSpan())
            };
            NetworkModelSnapshot malformed = initial with
            {
                Timestamp = initial.Timestamp.AddSeconds(1),
                Streams = [malformedStream]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(malformed),
                Throws.ArgumentException.With.Message.Contains("fixed-size fields"));
            Assert.That(() => provider.Publish(malformed),
                Throws.ArgumentException.With.Message.Contains("fixed-size fields"));
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(current.Streams[0].DestinationAddress.ToArray(),
                Is.EqualTo(stream.DestinationAddress.ToArray()));
            Assert.That(current.Streams[0].SourceAddress.ToArray(), Is.EqualTo(stream.SourceAddress.ToArray()));
        }

        /// <summary>
        /// Rejects a zero traffic interval denominator without replacing the accepted acquisition.
        /// </summary>
        [Test]
        public async Task PublishRejectsZeroTrafficDenominatorWithoutReplacingSnapshotAsync()
        {
            NetworkStreamSnapshot stream = new("stream-1", "test", true)
            {
                StreamId = ByteString.From(new byte[8].AsSpan()),
                State = TsnStreamState.Disabled,
                Traffic = new NetworkTrafficSnapshot(1, 512, 0, 1)
            };
            NetworkModelSnapshot initial = EmptySnapshot() with { Streams = [stream] };
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkStreamSnapshot malformedStream = stream with
            {
                Traffic = new NetworkTrafficSnapshot(1, 512, 1, 0)
            };
            NetworkModelSnapshot malformed = initial with
            {
                Timestamp = initial.Timestamp.AddSeconds(1),
                Streams = [malformedStream]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(malformed),
                Throws.ArgumentException.With.Message.Contains("traffic/VLAN values"));
            Assert.That(() => provider.Publish(malformed),
                Throws.ArgumentException.With.Message.Contains("traffic/VLAN values"));
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(current.Streams[0].Traffic, Is.EqualTo(stream.Traffic));
        }

        /// <summary>
        /// Accepts the lowest and highest non-zero traffic interval denominators.
        /// </summary>
        [TestCase(0u, 1u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        public async Task TrafficIntervalDenominatorBoundariesSurviveConstructionAndPublicationAsync(
            uint numerator, uint denominator)
        {
            NetworkStreamSnapshot stream = new("stream-1", "test", true)
            {
                StreamId = ByteString.From(new byte[8].AsSpan()),
                State = TsnStreamState.Disabled,
                Traffic = new NetworkTrafficSnapshot(1, 512, numerator, denominator)
            };
            NetworkModelSnapshot initial = EmptySnapshot() with { Streams = [stream] };
            var provider = new ReferenceNetworkModelProvider(initial);

            provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.EqualTo(1));
            Assert.That(current.Streams[0].Traffic, Is.EqualTo(stream.Traffic));
        }

        /// <summary>
        /// Requires UTC acquisition timestamps and retains the previous revision on rejection.
        /// </summary>
        [Test]
        public async Task PublishRejectsNonUtcAcquisitionTimestampAsync()
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());

            Assert.That(
                () => provider.Publish(EmptySnapshot() with
                {
                    Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)
                }),
                Throws.ArgumentException);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Timestamp, Is.EqualTo(UnixEpoch));
            Assert.That(current.Revision, Is.Zero);
        }

        /// <summary>
        /// Confirms a mapping addition only after updating the provider acquisition.
        /// </summary>
        [Test]
        public async Task EditPriorityMappingReturnsGoodAndUpdatesSnapshotAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "BestEffort", 0, 0), delete: false)
                .ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(snapshot.Revision, Is.EqualTo(1));
            Assert.That(snapshot.MappingTables[0].Entries.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects an equal-timestamp acquisition read before an accepted mapping edit.
        /// </summary>
        [Test]
        public async Task PublishRejectsEqualTimestampAcquisitionCapturedBeforeEditAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));
            NetworkModelSnapshot stale = await provider.ReadAsync().ConfigureAwait(false);

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "Control", 1, 1), delete: false)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(() => provider.Publish(stale), Throws.ArgumentException);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Revision, Is.EqualTo(1));
            Assert.That(current.MappingTables[0].Entries, Has.Count.EqualTo(1));
            Assert.That(current.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo("Control"));
            Assert.That(current.Timestamp, Is.EqualTo(stale.Timestamp));
        }

        /// <summary>
        /// Rejects a pre-edit acquisition even when its timestamp is later than current state.
        /// </summary>
        [Test]
        public async Task PublishRejectsNewerTimestampAcquisitionCapturedBeforeEditAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));
            NetworkModelSnapshot stale = await provider.ReadAsync().ConfigureAwait(false);
            stale = stale with { Timestamp = stale.Timestamp.AddSeconds(1) };

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "Control", 1, 1), delete: false)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(stale.Timestamp, Is.GreaterThan(UnixEpoch));
            Assert.That(() => provider.Publish(stale), Throws.ArgumentException);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Revision, Is.EqualTo(1));
            Assert.That(current.MappingTables[0].Entries, Has.Count.EqualTo(1));
            Assert.That(current.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo("Control"));
            Assert.That(current.Timestamp, Is.EqualTo(UnixEpoch));
        }

        /// <summary>
        /// Replaces the complete acquisition when based on the current provider revision.
        /// </summary>
        [Test]
        public async Task PublishAtCurrentRevisionReplacesTheCompleteAcquisitionAsync()
        {
            var provider = new ReferenceNetworkModelProvider(
                SnapshotWithTable(true, CreateEntry("urn:old", "Old", 0, 0)));
            await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:edit", "Edited", 1, 1), delete: false).ConfigureAwait(false);
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);
            NetworkMappingTableSnapshot[] tables = current.MappingTables.Span.ToArray();
            tables[0] = tables[0] with { Entries = [] };

            provider.Publish(current with
            {
                Timestamp = current.Timestamp.AddSeconds(1),
                MappingTables = ArrayOf.Wrapped(tables)
            });
            NetworkModelSnapshot published = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(published.Revision, Is.EqualTo(2));
            Assert.That(published.MappingTables[0].Entries, Has.Count.EqualTo(0));
        }

        /// <summary>
        /// Accepts the normative unused PCP and DSCP sentinels.
        /// </summary>
        [Test]
        public async Task EditPriorityMappingAcceptsUnusedValueSentinelsAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1",
                CreateEntry("urn:test:priority", "BestEffort", byte.MaxValue, uint.MaxValue),
                delete: false).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Rejects undefined standard enum values during construction and preserves the accepted snapshot.
        /// </summary>
        [TestCase("interface admin")]
        [TestCase("interface oper")]
        [TestCase("duplex")]
        [TestCase("negotiation")]
        [TestCase("stream state")]
        [TestCase("failure code")]
        [TestCase("talker status")]
        [TestCase("listener status")]
        [TestCase("local chassis subtype")]
        [TestCase("port subtype")]
        [TestCase("neighbor chassis subtype")]
        [TestCase("neighbor port subtype")]
        public async Task UndefinedEnumValuesDoNotReplaceTheAcceptedSnapshotAsync(string valuePath)
        {
            NetworkModelSnapshot initial = SnapshotWithAllEnumValues();
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkModelSnapshot invalid = WithInvalidEnumValue(initial, valuePath) with
            {
                Timestamp = initial.Timestamp.AddSeconds(1)
            };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid), Throws.ArgumentException);
            Assert.That(() => provider.Publish(invalid), Throws.ArgumentException);
            NetworkModelSnapshot retained = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(retained.Revision, Is.Zero);
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(retained.Interfaces.Count, Is.EqualTo(initial.Interfaces.Count));
            Assert.That(retained.Streams.Count, Is.EqualTo(initial.Streams.Count));
            Assert.That(retained.Lldp, Is.Not.Null);
        }

        /// <summary>
        /// Rejects malformed mapping entries during construction and preserves the accepted table.
        /// </summary>
        [TestCase("empty URI")]
        [TestCase("empty label")]
        [TestCase("PCP range")]
        [TestCase("DSCP range")]
        [TestCase("duplicate key")]
        public async Task InvalidPriorityMappingEntriesDoNotReplaceTheAcceptedSnapshotAsync(string invalidField)
        {
            PriorityMappingEntryType entry = CreateEntry("urn:test:priority", "BestEffort", 0, 0);
            NetworkModelSnapshot initial = SnapshotWithTable(true, entry);
            var provider = new ReferenceNetworkModelProvider(initial);
            var invalidEntry = new PriorityMappingEntryType
            {
                MappingUri = entry.MappingUri,
                PriorityLabel = entry.PriorityLabel,
                PriorityValue_PCP = entry.PriorityValue_PCP,
                PriorityValue_DSCP = entry.PriorityValue_DSCP
            };
            PriorityMappingEntryType[] invalidEntries;
            switch (invalidField)
            {
                case "empty URI":
                    invalidEntry.MappingUri = " ";
                    invalidEntries = [invalidEntry];
                    break;
                case "empty label":
                    invalidEntry.PriorityLabel = string.Empty;
                    invalidEntries = [invalidEntry];
                    break;
                case "PCP range":
                    invalidEntry.PriorityValue_PCP = 8;
                    invalidEntries = [invalidEntry];
                    break;
                case "DSCP range":
                    invalidEntry.PriorityValue_DSCP = 64;
                    invalidEntries = [invalidEntry];
                    break;
                default:
                    invalidEntries = [entry, invalidEntry];
                    break;
            }
            NetworkModelSnapshot invalid = initial with
            {
                Timestamp = initial.Timestamp.AddSeconds(1),
                MappingTables =
                [
                    initial.MappingTables[0] with { Entries = ArrayOf.Wrapped(invalidEntries) }
                ]
            };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid), Throws.ArgumentException);
            Assert.That(() => provider.Publish(invalid), Throws.ArgumentException);
            NetworkModelSnapshot retained = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(retained.Revision, Is.Zero);
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(retained.MappingTables[0].Entries.Count, Is.EqualTo(1));
            Assert.That(retained.MappingTables[0].Entries[0].MappingUri, Is.EqualTo(entry.MappingUri));
            Assert.That(retained.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo(entry.PriorityLabel));
        }

        /// <summary>
        /// Reports distinct statuses for duplicate additions and missing deletions.
        /// </summary>
        [Test]
        public async Task EditPriorityMappingUsesNormativeDuplicateAndMissingEntryStatusesAsync()
        {
            PriorityMappingEntryType entry = CreateEntry("urn:test:priority", "BestEffort", 0, 0);
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true, entry));

            StatusCode duplicate = await provider.EditPriorityMappingAsync("table-1", entry, delete: false)
                .ConfigureAwait(false);
            StatusCode missing = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "missing", 0, 0), delete: true)
                .ConfigureAwait(false);

            Assert.That(duplicate, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
            Assert.That(missing, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        /// <summary>
        /// Commits only one of two concurrent additions with the same mapping key.
        /// </summary>
        [Test]
        public async Task ConcurrentDuplicateMappingAddsCommitOnlyOnceAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));
            PriorityMappingEntryType first = CreateEntry("urn:test:priority", "BestEffort", 0, 0);
            PriorityMappingEntryType second = CreateEntry("urn:test:priority", "BestEffort", 0, 0);

            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int ready = 0;
            Task<StatusCode> firstAdd = ScheduleAdd(first);
            Task<StatusCode> secondAdd = ScheduleAdd(second);
            StatusCode[] results = await Task.WhenAll(firstAdd, secondAdd).ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(results, Does.Contain(StatusCodes.Good));
            Assert.That(results, Does.Contain(StatusCodes.BadIndexRangeInvalid));
            Assert.That(snapshot.Revision, Is.EqualTo(1));
            Assert.That(snapshot.MappingTables[0].Entries.Count, Is.EqualTo(1));

            Task<StatusCode> ScheduleAdd(PriorityMappingEntryType entry)
            {
                return Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref ready) == 2)
                    {
                        start.SetResult(true);
                    }
                    await start.Task.ConfigureAwait(false);
                    return await provider.EditPriorityMappingAsync("table-1", entry, delete: false)
                        .ConfigureAwait(false);
                });
            }
        }

        /// <summary>
        /// Rejects PCP and DSCP values immediately above their permitted ranges.
        /// </summary>
        [TestCase(8, 0u)]
        [TestCase(byte.MaxValue, 64u)]
        public async Task EditPriorityMappingRejectsOutOfRangePcpAndDscpAsync(byte pcp, uint dscp)
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true));

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "BestEffort", pcp, dscp), delete: false)
                .ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(snapshot.Revision, Is.Zero);
            Assert.That(snapshot.MappingTables[0].Entries.Count, Is.Zero);
        }

        /// <summary>
        /// Distinguishes unsupported edits from an unknown table.
        /// </summary>
        [Test]
        public async Task EditPriorityMappingRejectsReadOnlyAndUnknownTablesAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: false));
            PriorityMappingEntryType entry = CreateEntry("urn:test:priority", "BestEffort", 0, 0);

            StatusCode readOnly = await provider.EditPriorityMappingAsync("table-1", entry, delete: false)
                .ConfigureAwait(false);
            StatusCode unknown = await provider.EditPriorityMappingAsync("missing", entry, delete: false)
                .ConfigureAwait(false);

            Assert.That(readOnly, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(unknown, Is.EqualTo(StatusCodes.BadNotFound));
        }

        /// <summary>
        /// Preserves provider failure status and does not commit an edit.
        /// </summary>
        [Test]
        public async Task EditPriorityMappingPropagatesUnavailableProviderStatusAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(canEdit: true) with
            {
                StatusCode = StatusCodes.BadCommunicationError
            });

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test:priority", "BestEffort", 0, 0), delete: false)
                .ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(snapshot.MappingTables[0].Entries.Count, Is.Zero);
        }

        /// <summary>
        /// Rejects older acquisition time without advancing the current revision.
        /// </summary>
        [Test]
        public async Task PublishRejectsStaleAcquisitionWithoutAdvancingRevisionAsync()
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot() with { Revision = 100 });
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
            provider.Publish(EmptySnapshot() with { Timestamp = UnixEpoch.AddSeconds(1) });
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(() => provider.Publish(current with { Timestamp = UnixEpoch }), Throws.ArgumentException);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(snapshot.Revision, Is.EqualTo(1));
            Assert.That(snapshot.Timestamp, Is.EqualTo(UnixEpoch.AddSeconds(1)));
        }

        /// <summary>
        /// Uses both URI and label when deleting an entry.
        /// </summary>
        [Test]
        public async Task DeleteMappingRemovesOnlyMatchingCompositeKeyAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(true,
                CreateEntry("urn:first", "Control", 7, 63),
                CreateEntry("urn:second", "Control", 0, 0)));

            StatusCode deleted = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:first", "Control", 255, uint.MaxValue), true).ConfigureAwait(false);
            StatusCode unknown = await provider.EditPriorityMappingAsync(
                "missing", CreateEntry("urn:first", "Control", 255, uint.MaxValue), true).ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await provider.ReadAsync().ConfigureAwait(false);

            Assert.That(deleted, Is.EqualTo(StatusCodes.Good));
            Assert.That(unknown, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(snapshot.MappingTables[0].Entries.Count, Is.EqualTo(1));
            Assert.That(snapshot.MappingTables[0].Entries[0].MappingUri, Is.EqualTo("urn:second"));
            Assert.That(snapshot.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// Retains the exact accepted range endpoints and unused sentinels.
        /// </summary>
        [TestCase(0, 0u)]
        [TestCase(7, 63u)]
        [TestCase(byte.MaxValue, 0u)]
        [TestCase(0, uint.MaxValue)]
        [TestCase(255, uint.MaxValue)]
        public async Task MappingRangeBoundariesAreAcceptedAsync(byte pcp, uint dscp)
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(true));

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test", "Control", pcp, dscp), false).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            PriorityMappingEntryType actual =
                (await provider.ReadAsync().ConfigureAwait(false)).MappingTables[0].Entries[0];
            Assert.That(actual.PriorityValue_PCP, Is.EqualTo(pcp));
            Assert.That(actual.PriorityValue_DSCP, Is.EqualTo(dscp));
        }

        /// <summary>
        /// Rejects empty mapping identities without advancing the revision.
        /// </summary>
        [TestCase("", "Control")]
        [TestCase("urn:test", "")]
        [TestCase(" ", "Control")]
        public async Task EmptyMappingKeysAreRejectedWithoutMutationAsync(string uri, string label)
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(true));

            StatusCode result = await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry(uri, label, 0, 0), false).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        /// <summary>
        /// Produces equivalent detached acquisitions through fluent and direct construction.
        /// </summary>
        [Test]
        public async Task FluentBuilderAndDirectConstructionProduceIndependentEquivalentAcquisitionsAsync()
        {
            var definition = new NetworkInterfaceSnapshot("physical", "eth0")
            {
                AdminStatus = InterfaceAdminStatus.Up,
                OperStatus = InterfaceOperStatus.Up,
                Speed = 1_000_000_000,
                Ethernet = new NetworkEthernetSnapshot(1000, Duplex.Full, 1522),
                VlanTagCapable = true
            };
            var builder = new NetworkModelBuilder().AddInterface(definition);
            ReferenceNetworkModelProvider fluent = builder.Build(UnixEpoch, StatusCodes.Good);
            var direct = new ReferenceNetworkModelProvider(EmptySnapshot() with { Interfaces = [definition] });
            builder.AddInterface(new NetworkInterfaceSnapshot("vlan", "eth0.2")
            {
                AdminStatus = InterfaceAdminStatus.Up,
                OperStatus = InterfaceOperStatus.Up,
                Speed = 1_000_000_000,
                VlanId = 2,
                LowerLayerIds = ["physical"]
            });

            NetworkModelSnapshot first = await fluent.ReadAsync().ConfigureAwait(false);
            NetworkModelSnapshot second = await direct.ReadAsync().ConfigureAwait(false);

            Assert.That(first.Interfaces.Count, Is.EqualTo(1));
            Assert.That(first.Interfaces[0], Is.EqualTo(second.Interfaces[0]));
            Assert.That(first.Interfaces[0], Is.Not.SameAs(second.Interfaces[0]));
            Assert.That((await builder.Build(UnixEpoch, StatusCodes.Good).ReadAsync()
                .ConfigureAwait(false)).Interfaces.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Accepts the permitted VLAN range endpoints.
        /// </summary>
        [TestCase((ushort)0)]
        [TestCase((ushort)4094)]
        public async Task VlanBoundariesAreAcceptedAsync(ushort vlan)
        {
            ReferenceNetworkModelProvider provider = new NetworkModelBuilder()
                .AddInterface(new NetworkInterfaceSnapshot("vlan", "eth0.2")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000,
                    VlanId = vlan,
                    SrClasses = [new NetworkSrClassSnapshot(6, 7, vlan)]
                }).Build(UnixEpoch, StatusCodes.Good);

            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Interfaces[0].VlanId, Is.EqualTo(vlan));
        }

        /// <summary>
        /// Rejects out-of-range VLAN and SR-class priority values.
        /// </summary>
        [TestCase((ushort)4095, (byte)0)]
        [TestCase((ushort)0, (byte)8)]
        [TestCase(ushort.MaxValue, (byte)0)]
        public void InvalidInterfaceAndSrClassRangesAreRejected(ushort vlan, byte priority)
        {
            var builder = new NetworkModelBuilder()
                .AddInterface(new NetworkInterfaceSnapshot("vlan", "eth0.2")
                {
                    AdminStatus = InterfaceAdminStatus.Up,
                    OperStatus = InterfaceOperStatus.Up,
                    Speed = 1_000_000_000,
                    VlanId = vlan,
                    SrClasses = [new NetworkSrClassSnapshot(6, priority, vlan)]
                });

            Assert.That(() => builder.Build(UnixEpoch, StatusCodes.Good), Throws.ArgumentException);
        }

        /// <summary>
        /// Rejects interface VLAN identifiers above the provider's supported range.
        /// </summary>
        [TestCase((ushort)4095)]
        [TestCase(ushort.MaxValue)]
        public async Task InvalidInterfaceVlanIdDoesNotReplaceAcquisitionAsync(ushort vlanId)
        {
            NetworkModelSnapshot initial = EmptySnapshot();
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkInterfaceSnapshot networkInterface = new("vlan", "eth0.2")
            {
                AdminStatus = InterfaceAdminStatus.Up,
                OperStatus = InterfaceOperStatus.Up,
                Speed = 1_000_000_000,
                VlanId = vlanId
            };
            NetworkModelSnapshot invalid = initial with { Interfaces = [networkInterface] };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid),
                Throws.ArgumentException.With.Message.Contains("Interface VLAN identifiers"));
            Assert.That(() => provider.Publish(invalid),
                Throws.ArgumentException.With.Message.Contains("Interface VLAN identifiers"));
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(current.Interfaces.Count, Is.Zero);
        }

        /// <summary>
        /// Rejects SR-class VLAN identifiers above the provider's supported range.
        /// </summary>
        [TestCase((ushort)4095)]
        [TestCase(ushort.MaxValue)]
        public async Task InvalidSrClassVidDoesNotReplaceAcquisitionAsync(ushort vid)
        {
            NetworkModelSnapshot initial = EmptySnapshot();
            var provider = new ReferenceNetworkModelProvider(initial);
            NetworkInterfaceSnapshot networkInterface = new("physical", "eth0")
            {
                AdminStatus = InterfaceAdminStatus.Up,
                OperStatus = InterfaceOperStatus.Up,
                Speed = 1_000_000_000,
                SrClasses = [new NetworkSrClassSnapshot(6, 3, vid)]
            };
            NetworkModelSnapshot invalid = initial with { Interfaces = [networkInterface] };

            Assert.That(() => new ReferenceNetworkModelProvider(invalid),
                Throws.ArgumentException.With.Message.Contains("SR class VLAN identifiers"));
            Assert.That(() => provider.Publish(invalid),
                Throws.ArgumentException.With.Message.Contains("SR class VLAN identifiers"));
            NetworkModelSnapshot current = await provider.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(current.Interfaces.Count, Is.Zero);
        }

        /// <summary>
        /// Restarts from retained data while assigning a new provider revision sequence.
        /// </summary>
        [Test]
        public async Task ProviderRestartRetainsExplicitSnapshotButResetsGenerationAsync()
        {
            var original = new ReferenceNetworkModelProvider(SnapshotWithTable(true));
            await original.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test", "Control", 7, 63), false).ConfigureAwait(false);
            NetworkModelSnapshot retained = await original.ReadAsync().ConfigureAwait(false);
            var restarted = new ReferenceNetworkModelProvider(retained);

            NetworkModelSnapshot current = await restarted.ReadAsync().ConfigureAwait(false);

            Assert.That(current.Revision, Is.Zero);
            Assert.That(current.MappingTables[0].Entries[0].PriorityLabel, Is.EqualTo("Control"));
            Assert.That(retained.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects canceled operations before changing provider state.
        /// </summary>
        [Test]
        public async Task CancelledReadAndEditDoNotChangeAcquisitionAsync()
        {
            var provider = new ReferenceNetworkModelProvider(SnapshotWithTable(true));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(async () => await provider.ReadAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.EditPriorityMappingAsync(
                "table-1", CreateEntry("urn:test", "Control", 7, 63), false, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That((await provider.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        /// <summary>
        /// Registers the same manager factory through instance and container-resolved overloads.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FluentRegistrationUsesTheSameFactoryAsDirectConstruction(bool resolveFromContainer)
        {
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());
            var services = new ServiceCollection();
            services.AddSingleton<INetworkModelProvider>(provider);
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "NetworkModelRegistration";
            });
            IOpcUaServerBuilder returned = resolveFromContainer
                ? builder.AddNetworkModel(container => container.GetRequiredService<INetworkModelProvider>())
                : builder.AddNetworkModel(provider);
            using ServiceProvider container = services.BuildServiceProvider();
            OpcUaServerNodeManagerRegistration registration = container
                .GetServices<OpcUaServerNodeManagerRegistration>().Single();
            ArrayOf<IAsyncNodeManagerFactory> factories = registration.ResolveAsyncFactories(
                container, new ApplicationConfiguration());
            var direct = new NetworkModelNodeManagerFactory(provider, _ => false);

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(factories.Count, Is.EqualTo(1));
            Assert.That(factories[0], Is.TypeOf<NetworkModelNodeManagerFactory>());
            Assert.That(factories[0].NamespacesUris.ToArray(), Is.EqualTo(direct.NamespacesUris.ToArray()));
        }

        /// <summary>
        /// Rejects missing registration inputs and a provider factory that returns null.
        /// </summary>
        [Test]
        public void FluentRegistrationRejectsMissingInputsAndNullFactoryResults()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            var provider = new ReferenceNetworkModelProvider(EmptySnapshot());

            Assert.That(() => NetworkModelServerBuilderExtensions.AddNetworkModel(null!, provider),
                Throws.ArgumentNullException);
            Assert.That(() => builder.AddNetworkModel((INetworkModelProvider)null!), Throws.ArgumentNullException);
            Assert.That(() => builder.AddNetworkModel((Func<IServiceProvider, INetworkModelProvider>)null!),
                Throws.ArgumentNullException);
            builder.AddNetworkModel(_ => null!);
            using ServiceProvider container = services.BuildServiceProvider();
            OpcUaServerNodeManagerRegistration registration = container
                .GetServices<OpcUaServerNodeManagerRegistration>().Single();
            Assert.That(() => registration.ResolveAsyncFactories(container, new ApplicationConfiguration()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Creates an entry with independently specified identity and priority fields.
        /// </summary>
        private static PriorityMappingEntryType CreateEntry(string uri, string label, byte pcp, uint dscp)
        {
            return new PriorityMappingEntryType
            {
                MappingUri = uri,
                PriorityLabel = label,
                PriorityValue_PCP = pcp,
                PriorityValue_DSCP = dscp
            };
        }

        /// <summary>
        /// Creates a valid snapshot with every enum-bearing value present.
        /// </summary>
        private static NetworkModelSnapshot SnapshotWithAllEnumValues()
        {
            return EmptySnapshot() with
            {
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000,
                        Ethernet = new NetworkEthernetSnapshot(1000, Duplex.Full, 1522)
                        {
                            NegotiationStatus = NegotiationStatus.InProgress
                        }
                    }
                ],
                Streams =
                [
                    new NetworkStreamSnapshot("talker", "test", true)
                    {
                        StreamId = ByteString.From(new byte[8].AsSpan()),
                        State = TsnStreamState.Disabled,
                        Status = new NetworkStreamStatusSnapshot(TsnFailureCode.NoFailure)
                        {
                            TalkerStatus = TsnTalkerStatus.Ready,
                            ListenerStatus = TsnListenerStatus.Ready
                        }
                    }
                ],
                Lldp = new NetworkLldpSnapshot(
                    ChassisIdSubtype.MacAddress, "02:00:00:00:00:01", "switch", "reference")
                {
                    Ports =
                    [
                        new NetworkLldpPortSnapshot(
                            "port", "eth0", ByteString.From(new byte[6].AsSpan()),
                            PortIdSubtype.InterfaceName, "eth0")
                        {
                            Neighbors =
                            [
                                new NetworkLldpNeighborSnapshot(
                                    "neighbor", 1, 1, ChassisIdSubtype.MacAddress, "02:00:00:00:00:02",
                                    PortIdSubtype.InterfaceName, "port1")
                            ]
                        }
                    ]
                }
            };
        }

        /// <summary>
        /// Replaces one enum-bearing snapshot field with an undefined value.
        /// </summary>
        private static NetworkModelSnapshot WithInvalidEnumValue(NetworkModelSnapshot snapshot, string valuePath)
        {
            if (valuePath is "interface admin" or "interface oper" or "duplex" or "negotiation")
            {
                NetworkInterfaceSnapshot networkInterface = snapshot.Interfaces[0];
                networkInterface = valuePath switch
                {
                    "interface admin" => networkInterface with
                    {
                        AdminStatus = (InterfaceAdminStatus)int.MaxValue
                    },
                    "interface oper" => networkInterface with
                    {
                        OperStatus = (InterfaceOperStatus)int.MaxValue
                    },
                    "duplex" => networkInterface with
                    {
                        Ethernet = networkInterface.Ethernet! with { Duplex = (Duplex)int.MaxValue }
                    },
                    _ => networkInterface with
                    {
                        Ethernet = networkInterface.Ethernet! with
                        {
                            NegotiationStatus = (NegotiationStatus)int.MaxValue
                        }
                    }
                };
                return snapshot with { Interfaces = [networkInterface] };
            }
            if (valuePath is "stream state" or "failure code" or "talker status" or "listener status")
            {
                NetworkStreamSnapshot stream = snapshot.Streams[0];
                NetworkStreamStatusSnapshot status = stream.Status!;
                stream = valuePath switch
                {
                    "stream state" => stream with { State = (TsnStreamState)int.MaxValue },
                    "failure code" => stream with
                    {
                        Status = status with { FailureCode = (TsnFailureCode)int.MaxValue }
                    },
                    "talker status" => stream with
                    {
                        Status = status with { TalkerStatus = (TsnTalkerStatus)int.MaxValue }
                    },
                    _ => stream with { Status = status with { ListenerStatus = (TsnListenerStatus)int.MaxValue } }
                };
                return snapshot with { Streams = [stream] };
            }

            NetworkLldpSnapshot lldp = snapshot.Lldp!;
            if (valuePath == "local chassis subtype")
            {
                return snapshot with { Lldp = lldp with { ChassisIdSubtype = (ChassisIdSubtype)int.MaxValue } };
            }
            NetworkLldpPortSnapshot port = lldp.Ports[0];
            if (valuePath == "port subtype")
            {
                port = port with { PortIdSubtype = (PortIdSubtype)int.MaxValue };
            }
            else
            {
                NetworkLldpNeighborSnapshot neighbor = lldp.Ports[0].Neighbors[0];
                neighbor = valuePath == "neighbor chassis subtype"
                    ? neighbor with { ChassisIdSubtype = (ChassisIdSubtype)int.MaxValue }
                    : neighbor with { PortIdSubtype = (PortIdSubtype)int.MaxValue };
                port = port with { Neighbors = [neighbor] };
            }
            return snapshot with { Lldp = lldp with { Ports = [port] } };
        }

        /// <summary>
        /// Creates an empty acquisition with a deterministic UTC timestamp.
        /// </summary>
        private static NetworkModelSnapshot EmptySnapshot()
        {
            return new NetworkModelSnapshot
            {
                Timestamp = UnixEpoch,
                StatusCode = StatusCodes.Good,
                Interfaces = [],
                MappingTables = [],
                Streams = []
            };
        }

        /// <summary>
        /// Creates one mapping table with explicit editing capability.
        /// </summary>
        private static NetworkModelSnapshot SnapshotWithTable(
            bool canEdit,
            params PriorityMappingEntryType[] entries)
        {
            return EmptySnapshot() with
            {
                MappingTables = ArrayOf.Wrapped(
                [
                    new NetworkMappingTableSnapshot("table-1")
                    {
                        CanEdit = canEdit,
                        Entries = ArrayOf.Wrapped(entries)
                    }
                ])
            };
        }

        /// <summary>Fixed acquisition origin for provider contract tests.</summary>
        private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
