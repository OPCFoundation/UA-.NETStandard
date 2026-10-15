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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Guards the shared-model boundary and faithful monitored-value quality projection.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingClientTests
    {
        /// <summary>
        /// Uncertain and Bad qualities that must never establish usable production state.
        /// </summary>
        private static IEnumerable<StatusCode> NonGoodStatuses =>
            [StatusCodes.Uncertain, StatusCodes.UncertainLastUsableValue, StatusCodes.BadNoCommunication];

        /// <summary>
        /// Consumers share one generated model without depending on the executable or server policy.
        /// </summary>
        [Test]
        public void ClientOwnsModelAndContractsWithoutDependingOnServer()
        {
            System.Reflection.Assembly client = typeof(RecyclingCoordinatorClient).Assembly;
            System.Reflection.Assembly server = typeof(RecyclingCoordinator).Assembly;
            Assert.That(client.GetName().Name, Is.EqualTo("Opc.Ua.Recycling.Client"));
            Assert.That(typeof(Model.SnapshotDataType).Assembly, Is.SameAs(client));
            Assert.That(typeof(Model.RecyclingCoordinatorState).Assembly, Is.SameAs(client));
            Assert.That(typeof(Model.RecyclingCoordinatorTypeClient).Assembly, Is.SameAs(client));
            Assert.That(typeof(CommittedSnapshot).Assembly, Is.SameAs(client));
            Assert.That(typeof(RecyclingCommand).Assembly, Is.SameAs(client));
            Assert.That(typeof(RecyclingModelMapping).Assembly, Is.SameAs(client));
            Assert.That(typeof(IRecyclingMembershipClient).Assembly, Is.SameAs(client));
            Assert.That(typeof(RecyclingAuthorization).Assembly, Is.SameAs(server));
            Assert.That(typeof(RecyclingCheckpoint).Assembly, Is.SameAs(server));
            Assert.That(typeof(RecyclingPrincipal).Assembly, Is.SameAs(server));
            Assert.That(typeof(RecyclingCommandReceipt).Assembly, Is.SameAs(server));
            Assert.That(client.GetReferencedAssemblies().Select(static reference => reference.Name),
                Does.Not.Contain("RecyclingServer").And.Not.Contain("Opc.Ua.Server"));
            Assert.That(server.GetType(typeof(Model.SnapshotDataType).FullName!), Is.Null);
        }

        /// <summary>
        /// Both resource owners retain the exact pre-extraction XML used by durable configuration hashing.
        /// </summary>
        [Test]
        public async Task ModelResourceBytesAndLogicalNameRemainCompatibleWithExistingJournals()
        {
            await using Stream clientModel = typeof(RecyclingCoordinatorClient).Assembly
                .GetManifestResourceStream("Recycling.Model.xml")!;
            await using Stream serverModel = typeof(RecyclingCoordinator).Assembly
                .GetManifestResourceStream("Recycling.Model.xml")!;
            Assert.That(clientModel, Is.Not.Null);
            Assert.That(serverModel, Is.Not.Null);
            byte[] clientHash = await SHA256.HashDataAsync(clientModel).ConfigureAwait(false);
            byte[] serverHash = await SHA256.HashDataAsync(serverModel).ConfigureAwait(false);
            Assert.That(Convert.ToHexString(clientHash),
                Is.EqualTo("7E331A9A4370B2F70C5135140DC83D8D92AFAF40641D2607AF17D8CF6B8DDA7A"));
            Assert.That(serverHash, Is.EqualTo(clientHash));
        }

        /// <summary>
        /// Existing callers retain their three-argument construction and positional deconstruction.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void LegacyNotificationConstructionAndDeconstructionRemainAvailable(bool available)
        {
            var snapshot = new CommittedSnapshot { SourceTimestampUtc = DateTime.UnixEpoch.AddMinutes(1) };
            var notification = new CoordinatorNotification(snapshot, available, "legacy");
            (CommittedSnapshot actual, bool isAvailable, string detail) = notification;
            Assert.That(actual, Is.SameAs(snapshot));
            Assert.That(isAvailable, Is.EqualTo(available));
            Assert.That(detail, Is.EqualTo("legacy"));
            Assert.That(notification.StatusCode,
                Is.EqualTo(available ? StatusCodes.Good : StatusCodes.BadNoCommunication));
            Assert.That(notification.SourceTimestampUtc, Is.EqualTo(snapshot.SourceTimestampUtc));
            Assert.That(notification.ServerTimestampUtc, Is.EqualTo(DateTime.UnixEpoch));
        }

        /// <summary>
        /// An unavailable observation never replaces the last usable committed image or masks its raw quality.
        /// </summary>
        [TestCaseSource(nameof(NonGoodStatuses))]
        public void NonGoodNotificationRetainsRawMetadataAndLastGoodSnapshot(StatusCode status)
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var snapshot = new CommittedSnapshot { RunId = "run", Revision = 23 };
            CommittedSnapshot? last = snapshot;
            var value = new DataValue(
                Variant.Null, status.SetOverflow(true),
                DateTime.UnixEpoch.AddMinutes(2), DateTime.UnixEpoch.AddMinutes(3));
            CoordinatorNotification? notification =
                RecyclingCoordinatorClient.CreateNotification(value, context, ref last);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification!.IsAvailable, Is.False);
            Assert.That(notification.Snapshot, Is.SameAs(snapshot));
            Assert.That(last, Is.SameAs(snapshot));
            Assert.That(notification.StatusCode, Is.EqualTo(value.StatusCode));
            Assert.That(notification.Detail, Is.EqualTo(value.StatusCode.ToString()));
            Assert.That(notification.SourceTimestampUtc, Is.EqualTo(value.SourceTimestamp.ToDateTime()));
            Assert.That(notification.ServerTimestampUtc, Is.EqualTo(value.ServerTimestamp.ToDateTime()));
        }

        /// <summary>
        /// Initial missing data remains unavailable and does not establish a fabricated last-Good image.
        /// </summary>
        [Test]
        public void FirstUncertainNotificationDoesNotEstablishUsableState()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            CommittedSnapshot? last = null;
            var value = new DataValue(Variant.Null, StatusCodes.UncertainInitialValue);
            CoordinatorNotification? notification =
                RecyclingCoordinatorClient.CreateNotification(value, context, ref last);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification!.IsAvailable, Is.False);
            Assert.That(notification.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(notification.Snapshot.RunId, Is.Empty);
            Assert.That(last, Is.Null);
        }

        /// <summary>
        /// Good observations preserve quality information bits and original timestamps independently of domain time.
        /// </summary>
        [Test]
        public void GoodNotificationPreservesInformationBitsAndOriginalTransportTimestamps()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            CommittedSnapshot? last = null;
            var snapshot = new CommittedSnapshot
            {
                RunId = "run",
                Scenario = "healthy",
                ConfigurationId = "configuration",
                SourceTimestampUtc = DateTime.UnixEpoch.AddMinutes(1),
                Revision = 24
            };
            var value = new DataValue(
                Variant.FromStructure(RecyclingModelMapping.ToWire(snapshot)),
                StatusCodes.GoodClamped.SetOverflow(true),
                DateTime.UnixEpoch.AddMinutes(2), DateTime.UnixEpoch.AddMinutes(3));
            CoordinatorNotification? notification =
                RecyclingCoordinatorClient.CreateNotification(value, context, ref last);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification!.IsAvailable, Is.True);
            Assert.That(notification.Detail, Is.Empty);
            Assert.That(notification.StatusCode, Is.EqualTo(value.StatusCode));
            Assert.That(notification.SourceTimestampUtc, Is.EqualTo(value.SourceTimestamp.ToDateTime()));
            Assert.That(notification.ServerTimestampUtc, Is.EqualTo(value.ServerTimestamp.ToDateTime()));
            Assert.That(notification.Snapshot.SourceTimestampUtc, Is.EqualTo(snapshot.SourceTimestampUtc));
            Assert.That(notification.Snapshot.Revision, Is.EqualTo(snapshot.Revision));
            Assert.That(last, Is.SameAs(notification.Snapshot));
        }

        /// <summary>
        /// Ordering rejects older same-run images while allowing a new run's lower revision.
        /// </summary>
        [TestCase("run", 9, false)]
        [TestCase("run", 10, true)]
        [TestCase("run", 11, true)]
        [TestCase("new-run", 0, true)]
        public void NotificationOrderingIsRunScoped(string runId, long revision, bool accepted)
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var original = new CommittedSnapshot { RunId = "run", Revision = 10 };
            CommittedSnapshot? last = original;
            var snapshot = new CommittedSnapshot
            {
                RunId = runId,
                Scenario = "healthy",
                ConfigurationId = "configuration",
                Revision = revision
            };
            var value = new DataValue(Variant.FromStructure(RecyclingModelMapping.ToWire(snapshot)), StatusCodes.Good);
            CoordinatorNotification? notification =
                RecyclingCoordinatorClient.CreateNotification(value, context, ref last);
            if (accepted)
            {
                Assert.That(notification, Is.Not.Null);
                Assert.That(notification!.Snapshot.RunId, Is.EqualTo(runId));
                Assert.That(notification.Snapshot.Revision, Is.EqualTo(revision));
                Assert.That(last, Is.SameAs(notification.Snapshot));
            }
            else
            {
                Assert.That(notification, Is.Null);
                Assert.That(last, Is.SameAs(original));
            }
        }

        /// <summary>
        /// Good quality is not sufficient when the payload is not a valid typed snapshot.
        /// </summary>
        [Test]
        public void GoodNotificationWithWrongPayloadFailsWithoutReplacingLastGoodSnapshot()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var original = new CommittedSnapshot { RunId = "run", Revision = 10 };
            CommittedSnapshot? last = original;
            var value = new DataValue(Variant.From("not a snapshot"), StatusCodes.Good);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                RecyclingCoordinatorClient.CreateNotification(value, context, ref last))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(last, Is.SameAs(original));
        }
    }
}
