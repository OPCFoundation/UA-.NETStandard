/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public void ConditionRefreshOnInvalidSubscriptionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConditionRefreshAsync(
                    subscriptionId: 0xFFFFFFFFu,
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "ConditionRefresh method NodeId routing failed.");
        }

        [Test]
        public void EnableOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.EnableAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "Enable method NodeId routing failed.");
        }

        [Test]
        public void AcknowledgeOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
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
        public void SilenceOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.SilenceAsync(
                    conditionId: new NodeId(0xDEADBEEF),
                    ct: CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode,
                Is.Not.EqualTo((uint)StatusCodes.BadMethodInvalid),
                "Silence method NodeId routing failed.");
        }

        [Test]
        public void TimedShelveOnUnknownConditionReturnsBadStatus()
        {
            AlarmClient client = Session.GetAlarmClient(Telemetry);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
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