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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Coverage-push tests for the in-memory historian provider and the
    /// fluent <see cref="HistorianBuilder"/>. Targets paths previously
    /// exercised only indirectly via the dispatcher integration tests.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianCoverageTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public async Task ReadRawReverseTimeReturnsValuesNewestFirstAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("rev.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>();
            for (int i = 0; i < 5; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime.AddSeconds(10),
                    EndTime = BaseTime.AddSeconds(-1),
                    IsForward = false,
                    MaxValues = 0
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(5));
            Assert.That(page.Values[0].Value.SourceTimestamp,
                Is.EqualTo(BaseTime.AddSeconds(4)));
            Assert.That(page.Values[4].Value.SourceTimestamp,
                Is.EqualTo(BaseTime.AddSeconds(0)));
        }

        [Test]
        public async Task ReadModifiedReturnsReplacedEntriesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("mod.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime ts = BaseTime.AddSeconds(5);
            await provider.InsertAsync(context, nodeId, [MakeValue(ts, 1)], CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(context, nodeId, [MakeValue(ts, 2)], CancellationToken.None).ConfigureAwait(false);
            await provider.ReplaceAsync(context, nodeId, [MakeValue(ts, 3)], CancellationToken.None).ConfigureAwait(false);

            HistorianPage<ModifiedDataValue> page = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                    MaxValues = 0
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.GreaterThanOrEqualTo(2),
                "Each Replace should produce a modified-history entry recording the prior value.");
        }

        [Test]
        public void HistorianBuilderUseInMemoryReturnsSameInstanceAsProvider()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            InMemoryHistorianProvider provider = builder.UseInMemory();

            Assert.That(builder.Provider, Is.SameAs(provider));
        }

        [Test]
        public void HistorianBuilderUseProviderThrowsOnNull()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);

            Assert.That(
                () => builder.UseProvider(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void HistorianBuilderRegisterForNodeBindsExactNodeId()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            InMemoryHistorianProvider provider = builder.UseInMemory();
            var nodeId = new NodeId("bind.test", NamespaceIndex);

            builder.RegisterForNode(nodeId);

            IHistorianProvider resolved =
                ((IHistorianRegistryProvider)server).HistorianRegistry.Resolve(nodeId);
            Assert.That(resolved, Is.SameAs(provider));
        }

        [Test]
        public void HistorianBuilderRegisterAsDefaultMakesProviderFallback()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            InMemoryHistorianProvider provider = builder.UseInMemory();

            builder.RegisterAsDefault();

            IHistorianProvider resolved =
                ((IHistorianRegistryProvider)server).HistorianRegistry.Resolve(
                    new NodeId("unknown", 7));
            Assert.That(resolved, Is.SameAs(provider));
        }

        [Test]
        public void HistorianBuilderThrowsWhenServerLacksRegistry()
        {
            // A plain mock IServerInternal does not implement IHistorianRegistryProvider.
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(new Mock<ITelemetryContext>().Object);

            Assert.That(
                () => new HistorianBuilder(server.Object),
                Throws.TypeOf<InvalidOperationException>());
        }

        private static IServerInternal CreateServerWithRegistry()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:hist");

            var mockTelemetry = new Mock<ITelemetryContext>();
            var registry = new HistorianProviderRegistry(nsTable);

            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);

            return mockServer.Object;
        }

        private static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DataValue MakeValue(DateTime sourceTimestamp, double value)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, sourceTimestamp: sourceTimestamp, serverTimestamp: sourceTimestamp);
        }

        private static HistorianOperationContext CreateContext()
        {
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(
                systemContext, opContext, null, HistoryUpdateType.Insert);
        }
    }
}
