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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// depth compliance tests for Historical Access services.
    /// Covers Read Raw, Delete Value, Insert Value, Modified Values,
    /// Max Nodes Read Continuation Point, ServerTimestamp, and Update Value.
    /// Tests gracefully handle servers that do not support history.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("HistoricalAccessDepth")]
    public class HistoricalAccessDepthTests : TestFixture
    {
        [Test]
        public async Task ReadRaw001ReadWithTimeRangeAndNumValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 100,
                        IsReadModified = false,
                        ReturnBounds = false
                    }),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() { NodeId = nodeId }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported)
            {
                Assert.Fail("Historical access not supported or timestamp issue: " + ex.StatusCode);
            }
        }

        [Test]
        public async Task ReadRaw002ReadWithStartTimeOnlyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await HistoryReadRawAsync(
                    nodeId, DateTime.UtcNow.AddHours(-1), DateTime.MinValue, 10, false).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported)
            {
                Assert.Fail("Historical access not supported or timestamp issue: " + ex.StatusCode);
            }
        }

        [Test]
        public async Task ReadRaw003ReadWithEndTimeOnlyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await HistoryReadRawAsync(
                    nodeId, DateTime.MinValue, DateTime.UtcNow, 10, false).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported)
            {
                Assert.Fail("Historical access not supported or timestamp issue: " + ex.StatusCode);
            }
        }

        [Test]
        public async Task ReadRaw004ReadWithNumValuesOnlyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await HistoryReadRawAsync(
                    nodeId, DateTime.MinValue, DateTime.MinValue, 5, false).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported)
            {
                Assert.Ignore("Historical access not supported or timestamp issue: " + ex.StatusCode);
            }
        }

        [Test]
        public async Task ReadRaw005ReadWithReturnBoundsTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = true
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw006ReadWithReturnBoundsFalseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw007ReadSingleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 1,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw008ReadWithContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (!cp.IsEmpty)
            {
                HistoryReadResponse next = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = cp
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(next.Results.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ReadRaw009ReadWithStartTimeEqualsEndTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime time = DateTime.UtcNow.AddMinutes(-30);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = time,
                    EndTime = time,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw010ReadWithStartAfterEndAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, DateTime.UtcNow, DateTime.UtcNow.AddHours(-2), 100, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw011ReadWithLargeNumValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = endTime.AddDays(-1),
                    EndTime = endTime,
                    NumValuesPerNode = 10000,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw012ReadWithTimestampsToReturnSourceAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Source,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw013ReadWithTimestampsToReturnServerAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Server,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw014ReadWithTimestampsToReturnNeitherAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Neither,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw015ReadWithBoundsAndNumValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 5,
                    IsReadModified = false,
                    ReturnBounds = true
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw016ReadWithNarrowTimeRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = endTime.AddSeconds(-1),
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw017ReadWithWideTimeRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = endTime.AddDays(-30),
                    EndTime = endTime,
                    NumValuesPerNode = 10,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw018ReadWithIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        IndexRange = "0"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw019ReadWithDataEncodingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        DataEncoding = new QualifiedName(null)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRaw020ReadMultipleNodesAsync()
        {
            NodeId nodeId1 = ToNodeId(Constants.HistoricalDouble);
            NodeId nodeId2 = ToNodeId(Constants.HistoricalInt32);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId1 },
                    new() { NodeId = nodeId2 }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReadRaw022ReadWithGoodDataQualityAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 100, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);

            var historyData = ExtensionObject.ToEncodeable(response.Results[0].HistoryData) as HistoryData;
            if (historyData?.DataValues != null)
            {
                foreach (DataValue dv in historyData.DataValues)
                {
                    Assert.That(dv, Is.Not.Null);
                }
            }
        }

        [Test]
        public async Task ReadRaw023ReadReleaseContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (!cp.IsEmpty)
            {
                HistoryReadResponse release = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Both,
                    true,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = cp
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(release.Results.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ReadRawErr001InvalidNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr002NullNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = NodeId.Null
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr003InvalidTimestampsToReturnAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 100,
                        IsReadModified = false,
                        ReturnBounds = false
                    }),
                    (TimestampsToReturn)99,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() { NodeId = nodeId }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task ReadRawErr004EmptyNodesToReadAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 100,
                        IsReadModified = false,
                        ReturnBounds = false
                    }),
                    TimestampsToReturn.Both,
                    false,
                    Array.Empty<HistoryReadValueId>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response, Is.Not.Null);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task ReadRawErr005NullHistoryReadDetailsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() { NodeId = nodeId }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task ReadRawErr006BadIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        IndexRange = "BadRange"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfUnsupported(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRawErr007BadDataEncodingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        DataEncoding = new QualifiedName("InvalidEncoding_12345")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfUnsupported(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRawErr008NodeIdOfNonHistoricalNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 100, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfUnsupported(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRawErr009ReleasedContinuationPointReuseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse first = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(first.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(first.Results[0].StatusCode);

            ByteString cp = first.Results[0].ContinuationPoint;
            if (cp.IsEmpty)
            {
                Assert.Ignore("No continuation point returned.");
                return;
            }

            await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Both,
                true,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        ContinuationPoint = cp
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            HistoryReadResponse reuse = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        ContinuationPoint = cp
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(reuse.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr010InvalidContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = new ByteString(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC })
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported ||
                ex.StatusCode == StatusCodes.BadHistoryOperationInvalid)
            {
                Assert.Ignore("Historical access not fully supported: " + ex.StatusCode);
            }
        }

        [Test]
        [Ignore("Obsoleted scenario; placeholder retained for ordering continuity with neighbouring tests.")]
        public void ReadRawErr011Obsoleted()
        {
        }

        [Test]
        public async Task ReadRawErr012NumericNodeIdInvalidNamespaceAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = new NodeId(99999, 999)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr013StringNodeIdInvalidNamespaceAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = new NodeId("InvalidNode", 999)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr014OpaqueNodeIdInvalidNamespaceAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = new NodeId(new ByteString(new byte[] { 0x01, 0x02 }), 999)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr015GuidNodeIdInvalidNamespaceAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = new NodeId(Guid.NewGuid(), 999)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False);
        }

        [Test]
        public async Task ReadRawErr016MaxNodesPerHistoryReadExceededAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadValueId[] nodes = [.. Enumerable.Range(0, 1000).Select(_ => new HistoryReadValueId { NodeId = nodeId })];

            try
            {
                HistoryReadResponse response = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 100,
                        IsReadModified = false,
                        ReturnBounds = false
                    }),
                    TimestampsToReturn.Both,
                    false,
                    nodes.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response, Is.Not.Null);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task ReadRawErr017MixValidAndInvalidNodesAsync()
        {
            NodeId validNode = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = validNode },
                    new() { NodeId = Constants.InvalidNodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReadRawErr018NoTimeRangeNoNumValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryReadResponse response = await HistoryReadRawAsync(
                    nodeId, DateTime.MinValue, DateTime.MinValue, 0, false).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadInvalidTimestampArgument ||
                ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported ||
                ex.StatusCode == StatusCodes.BadHistoryOperationInvalid)
            {
                Assert.Ignore("Historical access not fully supported: " + ex.StatusCode);
            }
        }

        [Test]
        public async Task ReadRawErr019ObjectNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr021ReadWithFutureTimeRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 100, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ReadRawErr022ReadMethodNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = MethodIds.Server_GetMonitoredItems
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr023ReadViewNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = new NodeId(99998, 0)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr024ReadDataTypeNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = DataTypeIds.Double
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr025ReadReferenceTypeNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = ReferenceTypeIds.References
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr026ReadObjectTypeNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = ObjectTypeIds.BaseObjectType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadRawErr027ReadVariableTypeNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = VariableTypeIds.BaseVariableType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DeleteValue000DeleteWithTimeRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue001DeleteNarrowRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, endTime.AddSeconds(-10), endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue002DeleteWideRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, endTime.AddDays(-7), endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue003DeleteEqualStartEndAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime time = DateTime.UtcNow.AddMinutes(-30);
            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, time, time).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue004DeleteStartAfterEndAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, DateTime.UtcNow, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue005DeleteFutureRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue006DeleteAndVerifyEmptyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);

                HistoryReadResponse readResponse = await HistoryReadRawAsync(
                    nodeId, startTime, endTime, 100, false).ConfigureAwait(false);
                Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue007DeleteWithMinStartTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    nodeId, DateTime.MinValue, DateTime.UtcNow).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue008DeleteModifiedFalseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                var deleteDetails = new DeleteRawModifiedDetails
                {
                    NodeId = nodeId,
                    IsDeleteModified = false,
                    StartTime = startTime,
                    EndTime = endTime
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValue010DeleteModifiedTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                var deleteDetails = new DeleteRawModifiedDetails
                {
                    NodeId = nodeId,
                    IsDeleteModified = true,
                    StartTime = startTime,
                    EndTime = endTime
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat000DeleteSingleTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-15) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat001DeleteMultipleTimestampsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat002DeleteFutureTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddDays(1) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat003DeleteMinTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.MinValue }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat004DeleteMaxTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.MaxValue }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat005DeleteAndReadBackAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime target = DateTime.UtcNow.AddMinutes(-5);
            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = new DateTimeUtc[] { target }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);

                HistoryReadResponse readBack = await HistoryReadRawAsync(
                    nodeId, target.AddSeconds(-1), target.AddSeconds(1), 100, false).ConfigureAwait(false);
                Assert.That(readBack.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDat006DeleteEmptyTimestampsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = Array.Empty<DateTimeUtc>().ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueErr001InvalidNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    Constants.InvalidNodeId, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueErr002NullNodeIdAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    NodeId.Null, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueErr003NonHistoricalNodeAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    ToNodeId(Constants.ScalarStaticBoolean), startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueErr004ObjectNodeAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryUpdateResponse response = await HistoryDeleteRawAsync(
                    ObjectIds.Server, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueErr005EmptyExtensionObjectsAsync()
        {
            try
            {
                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    Array.Empty<ExtensionObject>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(response, Is.Not.Null);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task DeleteValueDatErr001InvalidNodeIdAtTimeAsync()
        {
            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = Constants.InvalidNodeId,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-10) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDatErr002NullNodeIdAtTimeAsync()
        {
            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = NodeId.Null,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-10) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDatErr003NonHistoricalNodeAtTimeAsync()
        {
            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = ToNodeId(Constants.ScalarStaticBoolean),
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-10) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDatErr004ObjectNodeAtTimeAsync()
        {
            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = ObjectIds.Server,
                    ReqTimes = new DateTimeUtc[] { DateTime.UtcNow.AddMinutes(-10) }.ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteValueDatErr005EmptyReqTimesAtTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                var deleteDetails = new DeleteAtTimeDetails
                {
                    NodeId = nodeId,
                    ReqTimes = Array.Empty<DateTimeUtc>().ToArrayOf()
                };

                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(deleteDetails) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History delete at time not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue000InsertSingleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(42.0), StatusCodes.Good, DateTime.UtcNow.AddMinutes(-60))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue001InsertMultipleValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime baseTime = DateTime.UtcNow.AddHours(-2);
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, baseTime),
                new(new Variant(2.0), StatusCodes.Good, baseTime.AddMinutes(1)),
                new(new Variant(3.0), StatusCodes.Good, baseTime.AddMinutes(2))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue002InsertAndReadBackAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime ts = DateTime.UtcNow.AddHours(-3);
            var values = new DataValue[]
            {
                new(new Variant(99.5), StatusCodes.Good, ts)
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);

                HistoryReadResponse readBack = await HistoryReadRawAsync(
                    nodeId, ts.AddSeconds(-1), ts.AddSeconds(1), 100, false).ConfigureAwait(false);
                Assert.That(readBack.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue003InsertWithGoodStatusAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(10.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-4))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue004InsertWithUncertainStatusAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(20.0), StatusCodes.UncertainLastUsableValue, DateTime.UtcNow.AddHours(-5))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue005InsertWithBadStatusAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(30.0), StatusCodes.BadSensorFailure, DateTime.UtcNow.AddHours(-6))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue006InsertFutureTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(50.0), StatusCodes.Good, DateTime.UtcNow.AddDays(1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue007InsertMinTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(0.0), StatusCodes.Good, DateTime.MinValue)
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue008InsertLargeValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(double.MaxValue), StatusCodes.Good, DateTime.UtcNow.AddHours(-7))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue009InsertNegativeValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(-100.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-8))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue010InsertZeroValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(0.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-9))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue011InsertNaNValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(double.NaN), StatusCodes.Good, DateTime.UtcNow.AddHours(-10))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue012InsertInfinityValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(double.PositiveInfinity), StatusCodes.Good, DateTime.UtcNow.AddHours(-11))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue014InsertDuplicateTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime dupTs = DateTime.UtcNow.AddHours(-12);
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, dupTs),
                new(new Variant(2.0), StatusCodes.Good, dupTs)
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue015InsertOutOfOrderTimestampsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime baseTime = DateTime.UtcNow.AddHours(-13);
            var values = new DataValue[]
            {
                new(new Variant(3.0), StatusCodes.Good, baseTime.AddMinutes(2)),
                new(new Variant(1.0), StatusCodes.Good, baseTime)
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue016InsertWithServerTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime insertTs = DateTime.UtcNow.AddHours(-14);
            var dv = new DataValue(new Variant(77.0), StatusCodes.Good, insertTs, insertTs);

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    nodeId, [dv]).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue017InsertEmptyValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    nodeId, []).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValue019InsertMultipleNodesSequentiallyAsync()
        {
            NodeId nodeId1 = ToNodeId(Constants.HistoricalDouble);
            NodeId nodeId2 = ToNodeId(Constants.HistoricalInt32);
            DateTime insertTs = DateTime.UtcNow.AddHours(-15);

            try
            {
                HistoryUpdateResponse resp1 = await HistoryInsertAsync(
                    nodeId1,
                    [
                        new DataValue(new Variant(1.0), StatusCodes.Good, insertTs)
                    ]).ConfigureAwait(false);
                Assert.That(resp1.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(resp1.Results[0].StatusCode);

                var updateDetails2 = new UpdateDataDetails
                {
                    NodeId = nodeId2,
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues = new DataValue[]
                    {
                        new(new Variant(100), StatusCodes.Good, insertTs)
                    }.ToArrayOf()
                };

                HistoryUpdateResponse resp2 = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new(updateDetails2) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(resp2.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(resp2.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr001InvalidNodeIdAsync()
        {
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    Constants.InvalidNodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr002NullNodeIdAsync()
        {
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    NodeId.Null, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr005NonHistoricalNodeAsync()
        {
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    ToNodeId(Constants.ScalarStaticBoolean), values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr006ObjectNodeAsync()
        {
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    ObjectIds.Server, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr007MethodNodeAsync()
        {
            var values = new DataValue[]
            {
                new(new Variant(1.0), StatusCodes.Good, DateTime.UtcNow.AddHours(-1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryInsertAsync(
                    MethodIds.Server_GetMonitoredItems, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History insert not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task InsertValueErr008EmptyUpdateDetailsAsync()
        {
            try
            {
                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    Array.Empty<ExtensionObject>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(response, Is.Not.Null);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task InsertValueErr009NullExtensionObjectAsync()
        {
            try
            {
                HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                    null,
                    new ExtensionObject[] { new() }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                if (response.Results.Count == 0)
                {
                    Assert.Ignore("HistoryUpdate returned no results; feature may not be supported.");
                }
                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task ModifiedValues001ReadModifiedValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            try
            {
                HistoryReadResponse response = await HistoryReadModifiedAsync(
                    nodeId, startTime, endTime).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfNotGood(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Fail($"History read modified not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task MaxNodesReadCp000ReadSingleNodeWithNumValuesOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task MaxNodesReadCp001FollowContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (!cp.IsEmpty)
            {
                HistoryReadResponse next = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = cp
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(next.Results.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task MaxNodesReadCp002ReleaseContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await HistoryReadRawAsync(
                nodeId, startTime, endTime, 1, false).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (!cp.IsEmpty)
            {
                HistoryReadResponse release = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Both,
                    true,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = cp
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(release.Results.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ServerTimestamp001ReadWithServerTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Server,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ServerTimestamp002ReadWithServerTimestampAndBoundsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = false,
                    ReturnBounds = true
                }),
                TimestampsToReturn.Server,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            IgnoreIfNotGood(response.Results[0].StatusCode);
        }

        [Test]
        public async Task UpdateValue001UpdateSingleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            var values = new DataValue[]
            {
                new(new Variant(999.0), StatusCodes.Good, DateTime.UtcNow.AddMinutes(-30))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryUpdateValuesAsync(
                    nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History update not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task UpdateValue002UpdateMultipleValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime baseTime = DateTime.UtcNow.AddHours(-1);
            var values = new DataValue[]
            {
                new(new Variant(100.0), StatusCodes.Good, baseTime),
                new(new Variant(200.0), StatusCodes.Good, baseTime.AddMinutes(1))
            };

            try
            {
                HistoryUpdateResponse response = await HistoryUpdateValuesAsync(
                    nodeId, values).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                IgnoreIfUnsupported(response.Results[0].StatusCode);
            }
            catch (ServiceResultException ex) when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore($"History update not supported: {ex.StatusCode}");
            }
        }

        private static bool IsUnsupported(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadHistoryOperationUnsupported ||
                statusCode == StatusCodes.BadHistoryOperationInvalid ||
                statusCode == StatusCodes.BadNotSupported;
        }

        private async Task<HistoryReadResponse> HistoryReadRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint numValues,
            bool returnBounds)
        {
            return await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = numValues,
                    IsReadModified = false,
                    ReturnBounds = returnBounds
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<HistoryReadResponse> HistoryReadModifiedAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime)
        {
            return await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 100,
                    IsReadModified = true,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<HistoryUpdateResponse> HistoryDeleteRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime)
        {
            var deleteDetails = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                IsDeleteModified = false,
                StartTime = startTime,
                EndTime = endTime
            };

            return await Session.HistoryUpdateAsync(
                null,
                new ExtensionObject[]
                {
                    new(deleteDetails)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<HistoryUpdateResponse> HistoryInsertAsync(
            NodeId nodeId,
            DataValue[] values)
        {
            var updateDetails = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = values.ToArrayOf()
            };

            return await Session.HistoryUpdateAsync(
                null,
                new ExtensionObject[]
                {
                    new(updateDetails)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<HistoryUpdateResponse> HistoryUpdateValuesAsync(
            NodeId nodeId,
            DataValue[] values)
        {
            var updateDetails = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Update,
                UpdateValues = values.ToArrayOf()
            };

            return await Session.HistoryUpdateAsync(
                null,
                new ExtensionObject[]
                {
                    new(updateDetails)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private void IgnoreIfUnsupported(StatusCode sc)
        {
            if (IsUnsupported(sc))
            {
                Assert.Ignore($"History operation not supported: {sc}");
            }
        }

        private void IgnoreIfNotGood(StatusCode sc)
        {
            if (!StatusCode.IsGood(sc) && IsUnsupported(sc))
            {
                Assert.Ignore($"History operation not supported: {sc}");
            }
        }
    }
}
