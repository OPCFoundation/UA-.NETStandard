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

#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task
#pragma warning disable CA2000 // Dispose of fixture lifetimes via TearDown

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// End-to-end integration tests that drive <see cref="HistoryClient"/>
    /// over the wire against the live <c>ReferenceServer</c>. The reference
    /// server historizes <c>Scalar_Static_Int32</c>, <c>Scalar_Static_Float</c>,
    /// and <c>Scalar_Static_Double</c> with 1001 seed samples each.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Historian")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HistoryClientIntegrationTests
    {
        private ServerFixture<ReferenceServer> m_serverFixture;
#pragma warning disable NUnit1032
        private ClientFixture m_clientFixture;
#pragma warning restore NUnit1032
        private ReferenceServer m_server;
        private ISession m_session;
        private string m_pkiRoot;
        private Uri m_url;
        private NodeId m_int32NodeId;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true,
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot);

            m_clientFixture = new ClientFixture(telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot);
            m_url = new Uri(Utils.UriSchemeOpcTcp + "://localhost:"
                + m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            try
            {
                m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.None);
            }
            catch (Exception e)
            {
                Assert.Ignore("Historian integration setup failed: " + e.Message);
            }

            ushort ns = (ushort)m_session.NamespaceUris.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            m_int32NodeId = new NodeId("Scalar_Static_Int32", ns);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync();
                m_session.Dispose();
                m_session = null;
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync();
            }
            m_clientFixture?.Dispose();
            m_server?.Dispose();
        }

        [Test]
        public async Task ReadRawReturnsSeededValuesAsync()
        {
            var client = new HistoryClient(m_session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_int32NodeId, now.AddDays(-1), now, maxValuesPerNode: 100))
            {
                values.Add(dv);
            }

            Assert.That(values, Is.Not.Empty,
                "ReferenceServer historizes Scalar_Static_Int32 with 1001 seed samples; raw read must return at least some.");
        }

        [Test]
        public async Task ReadProcessedAverageReturnsBucketsAsync()
        {
            var client = new HistoryClient(m_session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_int32NodeId,
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
            var client = new HistoryClient(m_session);
            DateTime ts = DateTime.UtcNow.AddSeconds(7); // unique future timestamp; no seed conflict

            var insertValue = new DataValue
            {
                WrappedValue = new Variant(12345),
                SourceTimestamp = ts,
                ServerTimestamp = ts,
                StatusCode = StatusCodes.Good,
            };

            IList<StatusCode> insertStatuses = await client.InsertAsync(
                m_int32NodeId, new[] { insertValue });
            Assert.That(insertStatuses, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertStatuses[0]), Is.True,
                $"Insert failed with status 0x{(uint)insertStatuses[0].Code:X8}");

            // Read back from a tight window around the inserted timestamp.
            var roundTrip = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_int32NodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            Assert.That(roundTrip, Is.Not.Empty);
            DataValue echoed = roundTrip.First(v => v.SourceTimestamp == ts);
            int actual = Convert.ToInt32(echoed.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(12345));

            // Replace the value at the same timestamp.
            var replaceValue = new DataValue
            {
                WrappedValue = new Variant(99999),
                SourceTimestamp = ts,
                ServerTimestamp = ts,
                StatusCode = StatusCodes.Good,
            };
            IList<StatusCode> replaceStatuses = await client.ReplaceAsync(
                m_int32NodeId, new[] { replaceValue });
            Assert.That(StatusCode.IsGood(replaceStatuses[0]), Is.True);

            roundTrip.Clear();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_int32NodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            DataValue replaced = roundTrip.First(v => v.SourceTimestamp == ts);
            int replacedValue = Convert.ToInt32(replaced.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(replacedValue, Is.EqualTo(99999));
        }

        [Test]
        public async Task GetServerCapabilitiesReportsHistoricalAccessAsync()
        {
            var client = new HistoryClient(m_session);
            HistoryServerCapabilitiesInfo caps = await client.GetServerCapabilitiesAsync();

            Assert.That(caps.AccessHistoryData, Is.True,
                "InMemoryHistorianProvider exposes AccessHistoryData; capability rollup must reflect it.");
            Assert.That(caps.InsertData, Is.True);
            Assert.That(caps.ReplaceData, Is.True);
            Assert.That(caps.DeleteRaw, Is.True);
        }
    }
}
