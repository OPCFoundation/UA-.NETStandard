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

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for Historical Access services.
    /// Tests gracefully handle servers that do not support history.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("HistoricalAccess")]
    public class HistoricalAccessTests : TestFixture
    {
        [Test]
        public async Task HistoryReadRawDataReturnsResultAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
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
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"History not supported: {sc}");
            }
        }

        [Test]
        public async Task HistoryReadWithTimeRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddMinutes(-10);

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
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"History not supported: {sc}");
            }
        }

        [Test]
        public async Task HistoryReadWithMaxValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
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
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"History not supported: {sc}");
            }
        }

        [Test]
        public async Task HistoryReadNonExistentNodeAsync()
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
        public async Task HistoryReadServerCurrentTimeAsync()
        {
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 10,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // CurrentTime typically has no history; any result is valid.
        }

        [Test]
        public async Task HistoryUpdateInsertAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);

            var updateDetails = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = new DataValue[]
                {
                    new(
                        new Variant(1.0),
                        StatusCodes.Good,
                        DateTime.UtcNow)
                }.ToArrayOf()
            };

            try
            {
                HistoryUpdateResponse response =
                    await Session.HistoryUpdateAsync(
                        null,
                        new ExtensionObject[]
                        {
                            new(updateDetails)
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(sc))
                {
                    Assert.Ignore(
                        $"History update not supported: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"History update not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task HistoryUpdateDeleteAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            var deleteDetails = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                IsDeleteModified = false,
                StartTime = startTime,
                EndTime = endTime
            };

            try
            {
                HistoryUpdateResponse response =
                    await Session.HistoryUpdateAsync(
                        null,
                        new ExtensionObject[]
                        {
                            new(deleteDetails)
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(sc))
                {
                    Assert.Ignore(
                        $"History delete not supported: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"History delete not supported: {ex.StatusCode}");
            }
        }

        [Description("Verify that HistoryRead with ReadRawModifiedDetails returns a response with the correct number of Results entries.")]
        [Test]
        public async Task HistoryReadWithReadRawModifiedDetailsVerifyStructureAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            var details = new ReadRawModifiedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = 10,
                IsReadModified = false,
                ReturnBounds = false
            };

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));

            HistoryReadResult result = response.Results[0];
            Assert.That(result, Is.Not.Null);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    $"History not supported: {result.StatusCode}");
            }
        }

        [Description("Verify that HistoryRead where startTime is after endTime returns a result (may be empty or an error status).")]
        [Test]
        public async Task HistoryReadWithStartTimeAfterEndTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow.AddHours(-2);
            DateTime startTime = DateTime.UtcNow;

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
            // Reversed time range is allowed; result may be empty or
            // indicate an error — both are acceptable.
        }

        [Description("Verify HistoryRead with IsReadModified set to true. Servers that do not support modified history should be skipped via Assert.Ignore.")]
        [Test]
        public async Task HistoryReadWithIsReadModifiedTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
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

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"ReadModified not supported: {sc}");
            }
        }

        [Description("Verify that NumValuesPerNode constrains the number of returned data values.")]
        [Test]
        public async Task HistoryReadWithNumValuesPerNodeLimitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            const uint limit = 2;

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = limit,
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
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"History not supported: {sc}");
            }

            var historyData = ExtensionObject.ToEncodeable(
                response.Results[0].HistoryData) as HistoryData;

            if (historyData?.DataValues != null)
            {
                Assert.That(
                    historyData.DataValues.Count,
                    Is.LessThanOrEqualTo((int)limit));
            }
        }

        [Description("Verify continuation point handling by requesting one value at a time and releasing the continuation point.")]
        [Test]
        public async Task HistoryReadWithContinuationPointAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
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
            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore(
                    $"History not supported: {sc}");
            }

            ByteString continuationPoint =
                response.Results[0].ContinuationPoint;

            if (!continuationPoint.IsEmpty)
            {
                // Release the continuation point.
                HistoryReadResponse releaseResponse =
                    await Session.HistoryReadAsync(
                        null,
                        new ExtensionObject(new ReadRawModifiedDetails()),
                        TimestampsToReturn.Both,
                        true,
                        new HistoryReadValueId[]
                        {
                            new() {
                                NodeId = nodeId,
                                ContinuationPoint =
                                    continuationPoint
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    releaseResponse.Results.Count, Is.EqualTo(1));
            }
        }

        [Description("Verify HistoryUpdate with UpdateDataDetails using the Update perform-insert-replace mode.")]
        [Test]
        public async Task HistoryUpdateWithUpdateDataDetailsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);

            var updateDetails = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Update,
                UpdateValues = new DataValue[]
                {
                    new(
                        new Variant(42.0),
                        StatusCodes.Good,
                        DateTime.UtcNow)
                }.ToArrayOf()
            };

            try
            {
                HistoryUpdateResponse response =
                    await Session.HistoryUpdateAsync(
                        null,
                        new ExtensionObject[]
                        {
                            new(updateDetails)
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(sc))
                {
                    Assert.Ignore(
                        $"History update (Update) not supported: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"History update not supported: {ex.StatusCode}");
            }
        }

        [Description("Verify HistoryUpdate with DeleteRawModifiedDetails over a time range. Handles servers that do not support deletion.")]
        [Test]
        public async Task HistoryUpdateWithDeleteRawModifiedDetailsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            var deleteDetails = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                IsDeleteModified = false,
                StartTime = startTime,
                EndTime = endTime
            };

            try
            {
                HistoryUpdateResponse response =
                    await Session.HistoryUpdateAsync(
                        null,
                        new ExtensionObject[]
                        {
                            new(deleteDetails)
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(sc))
                {
                    Assert.Ignore(
                        $"History delete not supported: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"History delete not supported: {ex.StatusCode}");
            }
        }

        [Description("Verify that HistoryRead can read history for multiple nodes in a single request and returns one result per node.")]
        [Test]
        public async Task HistoryReadMultipleNodesAtOnceAsync()
        {
            NodeId nodeId1 = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeId2 = ToNodeId(Constants.ScalarStaticInt32);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 10,
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

        private static bool IsUnsupported(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadHistoryOperationUnsupported ||
                statusCode == StatusCodes.BadHistoryOperationInvalid ||
                statusCode == StatusCodes.BadNotSupported;
        }
    }
}
