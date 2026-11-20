// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Integration smoke tests for AlarmClient against the reference server.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    [Category("AlarmClient")]
    public class AlarmClientIntegrationTests : TestFixture
    {
        [Test]
        public void GetAlarmClientReturnsNonNullInstance()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);
            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public async Task ConditionRefreshOnInvalidSubscriptionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConditionRefreshAsync(
                    subscriptionId: 0xFFFFFFFFu,
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "ConditionRefresh method NodeId routing failed.");
        }

        [Test]
        public async Task EnableOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.EnableAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "Enable method NodeId routing failed.");
        }

        [Test]
        public async Task AcknowledgeOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.AcknowledgeAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    eventId: new ByteString(new byte[] { 1, 2, 3 }),
                    comment: new LocalizedText("test"),
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "Acknowledge method NodeId routing failed.");
        }

        [Test]
        public async Task SilenceOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.SilenceAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "Silence method NodeId routing failed.");
        }

        [Test]
        public async Task TimedShelveOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.TimedShelveAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    shelvingTime: 1000,
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "TimedShelve method NodeId routing failed.");
        }

        [Test]
        public void AlarmEventFilterBuilderProducesValidFilter()
        {
            EventFilter filter = AlarmConditionTypeRecord.EventFilters.Build();

            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.SelectClauses.Count, Is.GreaterThan(0));
            // OfType filter should be present.
            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
        }
    }
}
