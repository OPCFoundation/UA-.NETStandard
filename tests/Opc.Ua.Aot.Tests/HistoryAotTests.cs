/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for history read operations.
    /// </summary>
    /// <remarks>
    /// The ReferenceServer historizes <c>Scalar_Static_Int32</c>,
    /// <c>Scalar_Static_Float</c> and <c>Scalar_Static_Double</c> via
    /// the fluent <c>HistorianBuilder</c> in
    /// <c>ReferenceNodeManager.EnableHistoryArchiving</c>. Each variable
    /// is seeded with 1001 samples by <c>SeedHistoricalNode</c>; these
    /// tests exercise the wire path against that seed data.
    /// </remarks>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class HistoryAotTests(AotTestFixture fixture)
    {
        private static readonly NodeId s_historizedNodeId
            = NodeId.Parse("ns=2;s=Scalar_Static_Double");

        [Test]
        public async Task HistoryReadRawAsync()
        {
            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-24),
                EndTime = DateTime.UtcNow,
                NumValuesPerNode = 100,
                IsReadModified = false,
                ReturnBounds = false
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = s_historizedNodeId
                }
            ];

            HistoryReadResponse response =
                await fixture.Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    false,
                    nodesToRead,
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(nodesToRead.Count);

            HistoryReadResult result = response.Results[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(result.HistoryData.IsNull).IsFalse();
            await Assert.That(
                result.HistoryData.TryGetValue(out HistoryData? data))
                .IsTrue();
            await Assert.That(data!.DataValues.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task HistoryReadProcessedAsync()
        {
            var details = new ReadProcessedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-24),
                EndTime = DateTime.UtcNow,
                ProcessingInterval = 60_000, // 1 minute buckets
                AggregateType = [ObjectIds.AggregateFunction_Average]
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = s_historizedNodeId
                }
            ];

            HistoryReadResponse response =
                await fixture.Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    false,
                    nodesToRead,
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(nodesToRead.Count);

            HistoryReadResult result = response.Results[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(
                result.HistoryData.TryGetValue(out HistoryData? data))
                .IsTrue();
            await Assert.That(data!.DataValues.Count).IsGreaterThan(0);
        }
    }
}
