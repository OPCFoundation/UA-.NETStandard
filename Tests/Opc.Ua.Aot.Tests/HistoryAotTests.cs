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

#nullable enable

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
                result.HistoryData.TryGetValue<HistoryData>(out HistoryData? data))
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
                result.HistoryData.TryGetValue<HistoryData>(out HistoryData? data))
                .IsTrue();
            await Assert.That(data!.DataValues.Count).IsGreaterThan(0);
        }
    }
}

