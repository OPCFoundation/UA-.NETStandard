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
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Exercises the Data Access workflow through a real session without Scan or ScanStart.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [NonParallelizable]
    public sealed class AutoIdDataAccessEndToEndTests
    {
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_host = await AutoIdHostFixture.StartAsync().ConfigureAwait(false);
            m_session = await m_host.ConnectAsync("AutoIdDataAccess").ConfigureAwait(false);
            m_client = new AutoIdClient(m_session, NUnitTelemetryContext.Create());
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_session.DisposeAsync().ConfigureAwait(false);
            await m_host.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ParametersControlAndCoherentResultsWorkWithoutScanMethodCallsAsync()
        {
            using var provider = new InMemoryRfidReaderProvider(TimeSpan.FromSeconds(10));
            await using AutoIdReader reader = await m_host.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(enableScanControl: true).BuildAsync().ConfigureAwait(false);
            await AssertBrowsableChildAsync(reader.Device, reader.Device.RuntimeParameters!).ConfigureAwait(false);
            await AssertBrowsableChildAsync(reader.Device, reader.Device.ScanActive!).ConfigureAwait(false);
            var parameterChildren = new List<BaseInstanceState>();
            reader.Device.RuntimeParameters!.GetChildren(m_host.Runtime.SystemContext, parameterChildren);
            foreach (BaseInstanceState child in parameterChildren)
            {
                await AssertBrowsableChildAsync(reader.Device.RuntimeParameters, child).ConfigureAwait(false);
                var nestedChildren = new List<BaseInstanceState>();
                child.GetChildren(m_host.Runtime.SystemContext, nestedChildren);
                foreach (BaseInstanceState nestedChild in nestedChildren)
                {
                    await AssertBrowsableChildAsync(child, nestedChild).ConfigureAwait(false);
                }
            }
            AutoIdReaderEntry entry = await m_client.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            AutoIdScanParameterReadResult initial = await m_client.ReadScanParametersAsync(entry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(initial.Status.Duration, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Status.Cycles, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Status.DataAvailable, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Status.CodeType, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Status.CodeTypes, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Parameters.CodeType, Is.EqualTo("EPC"));
                Assert.That(initial.Parameters.CodeTypes.ToArray(), Is.EqualTo(new uint[] { 0 }));
                Assert.That(initial.CodeTypeNamesStatus, Is.EqualTo(StatusCodes.Good));
                Assert.That((initial.CodeTypeNames.ToArray() ?? []).Select(name => name.Text),
                    Is.EqualTo(s_codeTypeNames));
            });
            AutoIdScanParameterStatus written = await m_client.WriteScanParametersAsync(
                entry, initial.Parameters with { Cycles = 0, Duration = 0, DataAvailable = false })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(written.Duration, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.Cycles, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.DataAvailable, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.CodeType, Is.EqualTo(StatusCodes.Good));
                Assert.That(written.CodeTypes, Is.EqualTo(StatusCodes.Good));
            });

            try
            {
                Assert.That(await m_client.SetScanActiveAsync(entry, true), Is.EqualTo(StatusCodes.Good));
                AutoIdScanActiveReadResult active = await m_client.ReadScanActiveAsync(entry);
                Assert.That(active.IsActive, Is.True);
                RfidScanResult result = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(1, 2, 3));
                result.Sighting = [new RfidSighting { Antenna = 4, Strength = -52 }];
                await reader.ReportScanAsync([result]).ConfigureAwait(false);
                AutoIdDeviceSnapshot scan = await m_client.ReadLastScanAsync(entry).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(scan.LastScanStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(scan.LastScanTimestamp, Is.EqualTo(result.Timestamp));
                    Assert.That(scan.LastScanTimestampStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(scan.LastScanAntenna, Is.EqualTo(4));
                    Assert.That(scan.LastScanRssi, Is.EqualTo(-52));
                    Assert.That(scan.LastScanAntennaStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(scan.LastScanRssiStatus, Is.EqualTo(StatusCodes.Good));
                });
                AutoIdDeviceSnapshot refreshed = await m_client.RefreshDeviceAsync(scan).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(refreshed.LastScanStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(refreshed.LastScanAntenna, Is.Null);
                    Assert.That(refreshed.LastScanRssi, Is.Null);
                    Assert.That(refreshed.LastScanAntennaStatus, Is.EqualTo(StatusCodes.BadNotFound));
                    Assert.That(refreshed.LastScanRssiStatus, Is.EqualTo(StatusCodes.BadNotFound));
                });
            }
            finally
            {
                await m_client.SetScanActiveAsync(entry, false).ConfigureAwait(false);
            }
            Assert.That((await m_client.ReadScanActiveAsync(entry)).IsActive, Is.False);
        }

        [Test]
        public async Task MissingParametersAndControlHaveExplicitPerValueErrorsAsync()
        {
            AutoIdReaderEntry reader = await m_client.GetReaderAsync(m_host.RfidReader.NodeId).ConfigureAwait(false);
            AutoIdScanParameterReadResult result = await m_client.ReadScanParametersAsync(reader).ConfigureAwait(false);
            AutoIdScanActiveReadResult active = await m_client.ReadScanActiveAsync(reader).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status.Duration, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(result.Status.Cycles, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(result.Status.CodeType, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(result.Status.CodeTypes, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(active.Status, Is.EqualTo(StatusCodes.BadNotFound));
            });
            Assert.That(await m_client.SetScanActiveAsync(reader, true), Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task ScanActiveObservationUsesTheSharedSubscriptionWithoutOwningItsSessionAsync()
        {
            using var provider = new InMemoryRfidReaderProvider();
            await using AutoIdReader reader = await m_host.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(enableScanControl: true).BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using (IAsyncEnumerator<AutoIdScanActiveReadResult> changes = m_client
                .ObserveScanActiveAsync(m_session.DefaultStreaming, entry, deadline.Token).GetAsyncEnumerator())
            {
                Assert.That(await changes.MoveNextAsync(), Is.True);
                Assert.That(changes.Current.IsActive, Is.False);
                Assert.That(await m_client.SetScanActiveAsync(entry, true), Is.EqualTo(StatusCodes.Good));
                Assert.That(await changes.MoveNextAsync(), Is.True);
                Assert.That(changes.Current.IsActive, Is.True);
                Assert.That(changes.Current.Status, Is.EqualTo(StatusCodes.Good));
                Assert.That(await m_client.SetScanActiveAsync(entry, false), Is.EqualTo(StatusCodes.Good));
                Assert.That(await changes.MoveNextAsync(), Is.True);
                Assert.That(changes.Current.IsActive, Is.False);
            }
            Assert.That((await m_client.ReadScanActiveAsync(entry)).Status, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task InconsistentAcquisitionTimestampsAreRejectedAndBadAntennaStatusIsPreservedAsync()
        {
            using var provider = new InMemoryRfidReaderProvider();
            await using AutoIdReader reader = await m_host.Runtime.Reader(Name(), provider)
                .BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            RfidScanResult result = AutoIdTestData.RfidResult(AutoIdTestData.Bytes(4));
            result.Sighting = [new RfidSighting { Antenna = 1, Strength = -40 }];
            await reader.ReportScanAsync([result]).ConfigureAwait(false);
            var device = (RfidReaderDeviceState)reader.Device;
            device.LastScanAntenna!.Timestamp = DateTimeUtc.MinValue;
            await Assert.ThatAsync(
                async () => await m_client.ReadLastScanAsync(entry).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadDataLost));
            device.LastScanAntenna.Timestamp = result.Timestamp;
            device.LastScanAntenna.StatusCode = StatusCodes.BadUserAccessDenied;
            AutoIdDeviceSnapshot scan = await m_client.ReadLastScanAsync(entry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(scan.LastScanAntennaStatus, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(scan.LastScanAntenna, Is.Null);
                Assert.That(scan.LastScanStatus, Is.EqualTo(StatusCodes.Good));
            });
            result.Sighting = [];
            await reader.ReportScanAsync([result]).ConfigureAwait(false);
            scan = await m_client.ReadLastScanAsync(entry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(scan.LastScanAntennaStatus, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(scan.LastScanRssiStatus, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(scan.LastScanAntenna, Is.Null);
            });
        }

        [Test]
        public async Task RejectedWritesRetainTheirIndividualStatusesAsync()
        {
            using var provider = new InMemoryRfidReaderProvider();
            await using AutoIdReader reader = await m_host.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(enableScanControl: true).BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            AutoIdScanParameterStatus statuses = await m_client.WriteScanParametersAsync(entry,
                new AutoIdScanParameters { Duration = -1, Cycles = 3, CodeType = "invalid", CodeTypes = [0] });
            Assert.Multiple(() =>
            {
                Assert.That(statuses.Duration, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(statuses.Cycles, Is.EqualTo(StatusCodes.Good));
                Assert.That(statuses.CodeType, Is.EqualTo(StatusCodes.BadOutOfRange));
            });
            AutoIdScanParameterReadResult read = await m_client.ReadScanParametersAsync(entry);
            Assert.That(read.Parameters.Cycles, Is.EqualTo(3));
            Assert.That(read.Parameters.Duration, Is.Zero);
        }

        /// <summary>
        /// Checks that configured parameter children are discoverable with their published identity.
        /// </summary>
        private async Task AssertBrowsableChildAsync(NodeState parent, BaseInstanceState child)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> references, ArrayOf<ServiceResult> errors) =
                await m_session.ManagedBrowseAsync(null, null, [parent.NodeId], 0,
                    BrowseDirection.Forward, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                    true, (uint)child.NodeClass).ConfigureAwait(false);
            Assert.That(errors.ToArray(), Has.All.Matches<ServiceResult>(ServiceResult.IsGood));
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(references[0].ToArray(), Has.Some.Matches<ReferenceDescription>(reference =>
                reference != null && reference.BrowseName == child.BrowseName &&
                ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris) == child.NodeId),
                () => $"Expected {parent.BrowseName}/{child.BrowseName} ({child.NodeId}); found " +
                    string.Join(", ", references[0].ToArray()!.Select(reference =>
                        $"{reference.BrowseName} ({reference.NodeId}, {reference.NodeClass})")));
        }

        private QualifiedName Name()
        {
            return new QualifiedName("DataAccess" + Interlocked.Increment(ref m_sequence),
                m_host.Runtime.InstanceNamespaceIndex);
        }

        private AutoIdHostFixture m_host = null!;
        private ManagedSession m_session = null!;
        private AutoIdClient m_client = null!;
        private int m_sequence;
        private static readonly string[] s_codeTypeNames = ["EPC"];
    }
}
