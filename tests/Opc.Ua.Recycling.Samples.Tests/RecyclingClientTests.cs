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
using System.Text;
using System.Threading;
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
        /// Empty, singleton and grown histories preserve the latest source/stage results and all other state.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5000)]
        public void LiveSnapshotBoundsHistoryWithoutChangingOtherCommittedFields(int count)
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration());
            CommittedSnapshot snapshot = engine.CreateRun("live-history");
            snapshot = snapshot with
            {
                History = Enumerable.Range(0, count).Select(index => new MaterialExecution(
                    index + 1, index, (index % 3) - 1, [index], [index + 1],
                    default, default, default, default, default)).ToArrayOf()
            };
            Model.SnapshotDataType full = RecyclingModelMapping.ToWire(snapshot);
            Model.SnapshotDataType live = RecyclingRuntime.CreateLiveSnapshot(snapshot);
            Assert.That(live.History.Count, Is.EqualTo(Math.Min(count, 3)));
            Assert.That(live.History.Span.ToArray().Select(item => item.Id),
                Is.EqualTo(snapshot.History.Span.ToArray().GroupBy(item => item.Stage)
                    .Select(group => group.Last()).OrderBy(item => item.Id).Select(item => item.Id)));
            Assert.That(snapshot.History.Count, Is.EqualTo(count));
            live.History = full.History;
            Assert.That(live.IsEqual(full), Is.True);
        }

        /// <summary>
        /// Live values retain each stage's latest result without repeatedly transporting the genealogy archive.
        /// Full snapshot and paged genealogy reads retain every committed execution.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task LiveSubscriptionPreservesControlsAndLatestResultsWithoutReplayingHistoryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            for (int tick = 0; tick < 100; tick++)
            {
                await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            }
            RecyclingCoordinatorClient client = await environment.ConnectAsync("operator", timeout.Token)
                .ConfigureAwait(false);
            CommittedSnapshot full = await client.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(full.History.Count, Is.GreaterThan(full.Stages.Count));
            Assert.That(full.History, Is.EqualTo(environment.Coordinator.Current.History));
            await using IAsyncEnumerator<CoordinatorNotification> changes = client.WatchAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            CoordinatorNotification live;
            do
            {
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                live = changes.Current;
            }
            while (!live.IsAvailable || live.Snapshot.Revision < full.Revision);
            Assert.That(live.Snapshot.History.Count, Is.LessThanOrEqualTo(full.Stages.Count + 1));
            Assert.That(live.Snapshot.History.Span.ToArray().Select(item => item.Id),
                Is.EqualTo(full.History.Span.ToArray().GroupBy(item => item.Stage)
                    .Select(group => group.Last()).OrderBy(item => item.Id).Select(item => item.Id)));
            ArrayOf<MaterialExecution> history = await client.ReadHistoryAsync(0, 32, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(history, Is.EqualTo(full.History.Span.ToArray().Take(32).ToArrayOf()));
            foreach (bool enabled in new[] { false, true })
            {
                CommittedSnapshot before = await client.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false);
                RecyclingCommandOutcome outcome = await client.ExecuteAsync(new RecyclingCommand
                {
                    RequestId = Guid.NewGuid().ToString("D"),
                    LineId = environment.ServerOptions.LineId,
                    RunId = before.RunId,
                    ExpectedRevision = before.Revision,
                    Kind = enabled ? RecyclingCommandKind.EnableProcess : RecyclingCommandKind.DisableProcess,
                    Process = RecyclingProcess.Collection
                }, timeout.Token).ConfigureAwait(false);
                Assert.That(outcome.Status, Is.EqualTo(StatusCodes.Good));
                do
                {
                    Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                    live = changes.Current;
                }
                while (!live.IsAvailable || live.Snapshot.Revision < outcome.Revision);
                Assert.That(live.Snapshot.Stages.Span.ToArray()
                    .Where(stage => stage.Process == RecyclingProcess.Collection)
                    .All(stage => stage.Enabled == enabled), Is.True);
            }
            Assert.That((await client.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false)).History,
                Is.EqualTo(full.History));
        }

        /// <summary>
        /// Notification cloning must retain the enabled flag in nested generated stage structures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotClonePreservesEquipmentControlFlags(bool enabled)
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration());
            CommittedSnapshot snapshot = engine.CreateRun("clone-control");
            snapshot = snapshot with
            {
                Stages = snapshot.Stages.Span.ToArray()
                    .Select(stage => stage with { Enabled = enabled }).ToArrayOf()
            };
            Model.SnapshotDataType wire = RecyclingModelMapping.ToWire(snapshot);
            Model.SnapshotDataType clone = CoreUtils.Clone(wire)!;
            Assert.That(clone.Stages.Count, Is.EqualTo(snapshot.Stages.Count));
            Assert.That(clone.Stages.Span.ToArray().All(stage => stage.Enabled == enabled), Is.True);
            Assert.That(clone.Revision, Is.EqualTo(wire.Revision));
        }

        /// <summary>
        /// A new committed revision and equipment state must pass the default monitored-item change filter.
        /// </summary>
        [Test]
        public void SnapshotEquipmentControlChangePassesMonitoredItemFilter()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration());
            CommittedSnapshot before = engine.CreateRun("filter-control");
            CommittedSnapshot after = before with
            {
                Revision = before.Revision + 1,
                Stages = before.Stages.Span.ToArray()
                    .Select(stage => stage with { Enabled = false }).ToArrayOf()
            };
            var previous = new DataValue(Variant.FromStructure(RecyclingModelMapping.ToWire(before)));
            var current = new DataValue(Variant.FromStructure(RecyclingModelMapping.ToWire(after)));
            Assert.That(Server.MonitoredItem.ValueChanged(
                current, ServiceResult.Good, previous, ServiceResult.Good, null!, 0), Is.True);
        }

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
        /// Both owners embed identical bytes and retain the pre-extraction XML across Git newline conversions.
        /// </summary>
        [TestCase("\n", "C6DEB4B037F05F83E541E1851C82FE166A3D22E163B732544C5AB9C570777D51")]
        [TestCase("\r\n", "7E331A9A4370B2F70C5135140DC83D8D92AFAF40641D2607AF17D8CF6B8DDA7A")]
        public async Task ModelResourceBytesAndLogicalNameRemainCompatibleWithExistingJournals(
            string checkoutNewLine, string expectedOriginalHash)
        {
            await using Stream clientModel = typeof(RecyclingCoordinatorClient).Assembly
                .GetManifestResourceStream("Recycling.Model.xml")!;
            await using Stream serverModel = typeof(RecyclingCoordinator).Assembly
                .GetManifestResourceStream("Recycling.Model.xml")!;
            Assert.That(clientModel, Is.Not.Null);
            Assert.That(serverModel, Is.Not.Null);
            using var clientBytes = new MemoryStream();
            using var serverBytes = new MemoryStream();
            await clientModel.CopyToAsync(clientBytes).ConfigureAwait(false);
            await serverModel.CopyToAsync(serverBytes).ConfigureAwait(false);
            Assert.That(serverBytes.ToArray(), Is.EqualTo(clientBytes.ToArray()));

            // Git text=auto changes checkout newlines. Normalize only this content pin, never the runtime hash.
            string canonicalModel = Encoding.UTF8.GetString(clientBytes.ToArray())
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            byte[] canonicalHash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalModel));
            Assert.That(Convert.ToHexString(canonicalHash),
                Is.EqualTo("C6DEB4B037F05F83E541E1851C82FE166A3D22E163B732544C5AB9C570777D51"));

            byte[] originalCheckoutBytes = Encoding.UTF8.GetBytes(
                canonicalModel.Replace("\n", checkoutNewLine, StringComparison.Ordinal));
            Assert.That(Convert.ToHexString(SHA256.HashData(originalCheckoutBytes)),
                Is.EqualTo(expectedOriginalHash));
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
