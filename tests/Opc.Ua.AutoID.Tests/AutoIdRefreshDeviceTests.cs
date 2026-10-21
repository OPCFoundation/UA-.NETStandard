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
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Pins the service calls of <see cref="AutoIdClient.RefreshDeviceAsync"/> on a strict session
    /// mock: one Read of the NodeIds the snapshot carries and no Browse.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdRefreshDeviceTests
    {
        [Test]
        public async Task OneReadOfTheSnapshotNodeIdsAndNoBrowseAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            var timestamp = new DateTimeUtc(new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc));
            var requested = new List<NodeId>();
            SetupRead(
                session,
                requested,
                new DataValue(new Variant((int)DeviceStatusEnumeration.Scanning)),
                new DataValue(new Variant("tag-1")),
                new DataValue(new Variant(timestamp)),
                new DataValue(Variant.Null));
            AutoIdDeviceSnapshot snapshot = Snapshot(withOptionalVariables: true);

            AutoIdDeviceSnapshot refreshed = await client.RefreshDeviceAsync(snapshot).ConfigureAwait(false);

            NodeId[] expected = [s_status, s_lastData, s_lastTimestamp, s_location];
            Assert.Multiple(() =>
            {
                Assert.That(requested, Is.EqualTo(expected));
                Assert.That(refreshed.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Scanning));
                Assert.That(refreshed.LastScanData.TryGetValue(out string text) ? text : null, Is.EqualTo("tag-1"));
                Assert.That(refreshed.LastScanStatus, Is.EqualTo(StatusCodes.Good));
                Assert.That(refreshed.LastScanTimestamp, Is.EqualTo(timestamp));
                Assert.That(refreshed.DeviceLocation, Is.Null);
                Assert.That(refreshed.DeviceName, Is.EqualTo(snapshot.DeviceName));
                Assert.That(refreshed.Methods, Is.EqualTo(snapshot.Methods));
                Assert.That(refreshed.LastScanDataId, Is.EqualTo(s_lastData));
                Assert.That(
                    snapshot.DeviceStatus,
                    Is.EqualTo(DeviceStatusEnumeration.Idle),
                    "the input is not changed");
            });
            VerifyOneReadOnly(session);
        }

        [Test]
        public async Task ADeviceWithoutOptionalVariablesIsRefreshedWithOneVariableAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            var requested = new List<NodeId>();
            SetupRead(session, requested, new DataValue(new Variant((int)DeviceStatusEnumeration.Error)));

            AutoIdDeviceSnapshot refreshed = await client.RefreshDeviceAsync(Snapshot(withOptionalVariables: false))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(requested, Has.Count.EqualTo(1).And.Contains(s_status));
                Assert.That(refreshed.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Error));
                Assert.That(refreshed.LastScanStatus, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(refreshed.LastScanData.IsNull, Is.True);
            });
            VerifyOneReadOnly(session);
        }

        [Test]
        public async Task AStaleDeviceStatusNodeIdThrowsTheStatusOfTheReadAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            SetupRead(
                session,
                [],
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));

            await Assert.ThatAsync(
                async () => await client.RefreshDeviceAsync(Snapshot(withOptionalVariables: true))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
            VerifyOneReadOnly(session);
        }

        [Test]
        public async Task AStaleOptionalVariableSurfacesItsStatusWithoutThrowingAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            SetupRead(
                session,
                [],
                new DataValue(new Variant((int)DeviceStatusEnumeration.Idle)),
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied));
            AutoIdDeviceSnapshot snapshot = Snapshot(withOptionalVariables: true) with
            {
                LastScanData = new Variant("old"),
                LastScanTimestamp = new DateTimeUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                DeviceLocation = new Location()
            };

            AutoIdDeviceSnapshot refreshed = await client.RefreshDeviceAsync(snapshot).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refreshed.DeviceStatus, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(refreshed.LastScanStatus, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(refreshed.LastScanData.IsNull, Is.True);
                Assert.That(refreshed.LastScanTimestamp, Is.Default);
                Assert.That(refreshed.DeviceLocation, Is.Null);
            });
            VerifyOneReadOnly(session);
        }

        [Test]
        public async Task AResultCountMismatchIsRejectedAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            SetupRead(session, [], new DataValue(new Variant((int)DeviceStatusEnumeration.Idle)));

            await Assert.ThatAsync(
                async () => await client.RefreshDeviceAsync(Snapshot(withOptionalVariables: true))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
            VerifyOneReadOnly(session);
        }

        [Test]
        public async Task InvalidSnapshotsAreRejectedWithoutAServiceCallAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();

            await Assert.MultipleAsync(async () =>
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await client.RefreshDeviceAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.ThrowsAsync<ArgumentException>(
                    async () => await client.RefreshDeviceAsync(new AutoIdDeviceSnapshot { NodeId = s_device })
                        .ConfigureAwait(false)).ConfigureAwait(false);
            }).ConfigureAwait(false);
            session.VerifyGet(value => value.MessageContext, Times.Once);
            session.VerifyNoOtherCalls();
        }

        [Test]
        public async Task CancellationReachesTheReadAsync()
        {
            (Mock<ISession> session, AutoIdClient client) = Create();
            using var cancellation = new CancellationTokenSource();
            CancellationToken observed = default;
            session.Setup(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> _,
                        CancellationToken token) =>
                    {
                        observed = token;
                        return new ValueTask<ReadResponse>(
                            Task.FromCanceled<ReadResponse>(new CancellationToken(true)));
                    });
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await client.RefreshDeviceAsync(Snapshot(withOptionalVariables: false), cancellation.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(observed, Is.EqualTo(cancellation.Token));
        }

        private static (Mock<ISession> Session, AutoIdClient Client) Create()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context);
            return (session, new AutoIdClient(session.Object, telemetry));
        }

        private static AutoIdDeviceSnapshot Snapshot(bool withOptionalVariables)
        {
            return new AutoIdDeviceSnapshot
            {
                NodeId = s_device,
                DeviceStatusId = s_status,
                LastScanDataId = withOptionalVariables ? s_lastData : NodeId.Null,
                LastScanTimestampId = withOptionalVariables ? s_lastTimestamp : NodeId.Null,
                DeviceLocationId = withOptionalVariables ? s_location : NodeId.Null,
                Methods = [new QualifiedName(BrowseNames.Scan, 2)],
                DeviceName = "Reader",
                DeviceStatus = DeviceStatusEnumeration.Idle,
                LastScanStatus = withOptionalVariables ? StatusCodes.UncertainInitialValue : StatusCodes.BadNotFound
            };
        }

        private static void SetupRead(Mock<ISession> session, List<NodeId> requested, params DataValue[] results)
        {
            session.Setup(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> nodes,
                        CancellationToken _) =>
                    {
                        foreach (ReadValueId node in nodes)
                        {
                            Assert.That(node.AttributeId, Is.EqualTo(Attributes.Value));
                            requested.Add(node.NodeId);
                        }
                        return new ValueTask<ReadResponse>(
                            new ReadResponse { ResponseHeader = new ResponseHeader(), Results = results.ToArrayOf() });
                    });
        }

        private static void VerifyOneReadOnly(Mock<ISession> session)
        {
            session.Verify(
                value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            session.VerifyGet(value => value.MessageContext, Times.AtLeastOnce);
            // A strict mock already fails any Browse; this also rules out every other service call.
            session.VerifyNoOtherCalls();
        }

        private static readonly NodeId s_device = new("Reader", 2);
        private static readonly NodeId s_status = new("Reader.DeviceStatus", 2);
        private static readonly NodeId s_lastData = new("Reader.LastScanData", 2);
        private static readonly NodeId s_lastTimestamp = new("Reader.LastScanTimestamp", 2);
        private static readonly NodeId s_location = new("Reader.DeviceLocation", 2);
    }
}
