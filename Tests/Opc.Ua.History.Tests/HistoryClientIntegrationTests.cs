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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// End-to-end integration tests that drive the
    /// <see cref="HistoryClient"/> fluent client over the wire against the
    /// live in-process <c>ReferenceServer</c> hosted by
    /// <see cref="TestFixture"/>. The reference server historizes
    /// <c>Scalar_Static_Int32</c>, <c>Scalar_Static_Float</c>, and
    /// <c>Scalar_Static_Double</c> with 1001 seed samples each via the
    /// fluent <c>HistorianBuilder</c>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HistoryClientIntegrationTests : TestFixture
    {
        // Use Scalar_Static_Double for history. Scalar_Static_Int32 has explicit
        // RolePermissions that grant anonymous Browse|Read|Write but NOT
        // ReadHistory/InsertHistory, so anonymous sessions get
        // BadUserAccessDenied on its history endpoints. Double has no
        // RolePermissions set, so the role-permission gate doesn't run.
        private NodeId m_doubleNodeId;

        [OneTimeSetUp]
        public void ResolveHistorizedNode()
        {
            ushort ns = (ushort)Session.NamespaceUris.GetIndex(
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            m_doubleNodeId = new NodeId("Scalar_Static_Double", ns);
        }

        [Test]
        public async Task ReadRawReturnsSeededValuesAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, now.AddDays(-1), now, maxValuesPerNode: 100))
            {
                values.Add(dv);
            }

            Assert.That(values, Is.Not.Empty,
                "ReferenceServer historizes Scalar_Static_Double with 1001 seed samples; raw read must return at least some.");
        }

        [Test]
        public async Task ReadProcessedAverageReturnsBucketsAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId,
                ObjectIds.AggregateFunction_Average,
                now.AddHours(-1),
                now,
                processingInterval: 60_000))
            {
                values.Add(dv);
            }

            Assert.That(values, Is.Not.Empty,
                "1-minute Average buckets over the last hour must produce at least one bucket.");
        }

        [Test]
        public async Task InsertReplaceRoundTripAsync()
        {
            var client = new HistoryClient(Session);
            DateTime ts = DateTime.UtcNow.AddSeconds(7); // unique future timestamp; no seed conflict

            var insertValue = new DataValue(
                new Variant(123.45),
                StatusCodes.Good,
                sourceTimestamp: ts,
                serverTimestamp: ts);

            IList<StatusCode> insertStatuses = await client.InsertAsync(
                m_doubleNodeId, new[] { insertValue });
            Assert.That(insertStatuses, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertStatuses[0]), Is.True,
                $"Insert failed with status 0x{(uint)insertStatuses[0].Code:X8}");

            // Read back from a tight window around the inserted timestamp.
            var roundTrip = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            Assert.That(roundTrip, Is.Not.Empty);
            DataValue echoed = roundTrip.First(v => v.SourceTimestamp == ts);
            double actual = Convert.ToDouble(echoed.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(123.45));

            // Replace the value at the same timestamp.
            var replaceValue = new DataValue(
                new Variant(999.99),
                StatusCodes.Good,
                sourceTimestamp: ts,
                serverTimestamp: ts);
            IList<StatusCode> replaceStatuses = await client.ReplaceAsync(
                m_doubleNodeId, new[] { replaceValue });
            Assert.That(StatusCode.IsGood(replaceStatuses[0]), Is.True);

            roundTrip.Clear();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            DataValue replaced = roundTrip.First(v => v.SourceTimestamp == ts);
            double replacedValue = Convert.ToDouble(replaced.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(replacedValue, Is.EqualTo(999.99));
        }

        [Test]
        public async Task GetServerCapabilitiesReportsHistoricalAccessAsync()
        {
            var client = new HistoryClient(Session);
            HistoryServerCapabilitiesInfo caps = await client.GetServerCapabilitiesAsync();

            Assert.That(caps.AccessHistoryData, Is.True,
                "InMemoryHistorianProvider exposes AccessHistoryData; capability rollup must reflect it.");
            Assert.That(caps.InsertData, Is.True);
            Assert.That(caps.ReplaceData, Is.True);
            Assert.That(caps.DeleteRaw, Is.True);
        }
    }
}
