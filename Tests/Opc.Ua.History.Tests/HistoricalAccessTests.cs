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

        [TestCase("Boolean")]
        [TestCase("SByte")]
        [TestCase("Byte")]
        [TestCase("Int16")]
        [TestCase("UInt16")]
        [TestCase("UInt32")]
        [TestCase("Int64")]
        [TestCase("UInt64")]
        [TestCase("Float")]
        [TestCase("String")]
        [TestCase("DateTime")]
        [TestCase("Guid")]
        [TestCase("ByteString")]
        [Description("Raw history read of every historized scalar type returns Good and seeded values.")]
        [Test]
        public async Task HistoryReadRawDataForScalarTypeReturnsSeededValuesAsync(string typeName)
        {
            NodeId nodeId = ToNodeId(
                new ExpandedNodeId($"Scalar_Static_{typeName}", Constants.ReferenceServerNamespaceUri));
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);

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
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            HistoryReadResult result = response.Results[0];
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Raw history read of Scalar_Static_{typeName} should return Good.");
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True,
                "Result should carry a HistoryData payload.");
            Assert.That(historyData!.DataValues.Count, Is.GreaterThan(0),
                $"Scalar_Static_{typeName} should return seeded historical values.");
        }

        [TestCase("Boolean")]
        [TestCase("SByte")]
        [TestCase("Byte")]
        [TestCase("Int16")]
        [TestCase("UInt16")]
        [TestCase("UInt32")]
        [TestCase("Int32")]
        [TestCase("Int64")]
        [TestCase("UInt64")]
        [TestCase("Float")]
        [TestCase("Double")]
        [TestCase("String")]
        [TestCase("DateTime")]
        [TestCase("ByteString")]
        [Description("Raw history read of every historized array type returns Good and seeded array values.")]
        [Test]
        public async Task HistoryReadRawDataForArrayTypeReturnsSeededValuesAsync(string typeName)
        {
            NodeId nodeId = ToNodeId(
                new ExpandedNodeId($"Scalar_Static_Arrays_{typeName}", Constants.ReferenceServerNamespaceUri));
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);

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
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            HistoryReadResult result = response.Results[0];
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Raw history read of Scalar_Static_Arrays_{typeName} should return Good.");
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True,
                "Result should carry a HistoryData payload.");
            Assert.That(historyData!.DataValues.Count, Is.GreaterThan(0),
                $"Scalar_Static_Arrays_{typeName} should return seeded historical values.");
            Assert.That(historyData.DataValues[0].WrappedValue.TypeInfo.ValueRank,
                Is.GreaterThanOrEqualTo(ValueRanks.OneDimension),
                $"Scalar_Static_Arrays_{typeName} history values should be arrays.");
        }

        [TestCase("Boolean")]
        [TestCase("SByte")]
        [TestCase("Byte")]
        [TestCase("Int16")]
        [TestCase("UInt16")]
        [TestCase("UInt32")]
        [TestCase("Int32")]
        [TestCase("Int64")]
        [TestCase("UInt64")]
        [TestCase("Float")]
        [TestCase("Double")]
        [TestCase("String")]
        [TestCase("DateTime")]
        [TestCase("ByteString")]
        [Description("Raw history read of every historized 2D array (matrix) type returns Good and seeded matrix values.")]
        [Test]
        public async Task HistoryReadRawDataForMatrixTypeReturnsSeededValuesAsync(string typeName)
        {
            NodeId nodeId = ToNodeId(
                new ExpandedNodeId($"Scalar_Static_Arrays2D_{typeName}", Constants.ReferenceServerNamespaceUri));
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);

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
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            HistoryReadResult result = response.Results[0];
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Raw history read of Scalar_Static_Arrays2D_{typeName} should return Good.");
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True,
                "Result should carry a HistoryData payload.");
            Assert.That(historyData!.DataValues.Count, Is.GreaterThan(0),
                $"Scalar_Static_Arrays2D_{typeName} should return seeded historical values.");
            Assert.That(historyData.DataValues[0].WrappedValue.TypeInfo.ValueRank,
                Is.GreaterThanOrEqualTo(ValueRanks.TwoDimensions),
                $"Scalar_Static_Arrays2D_{typeName} history values should be matrices.");
        }

        [Description("Raw history read of the historized structure node returns Good and seeded structure values.")]
        [Test]
        public async Task HistoryReadRawDataForStructureNodeReturnsSeededValuesAsync()
        {
            NodeId nodeId = ToNodeId(
                new ExpandedNodeId("Scalar_Static_Decimal", Constants.ReferenceServerNamespaceUri));
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);

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
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            HistoryReadResult result = response.Results[0];
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "Raw history read of Scalar_Static_Decimal should return Good.");
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True,
                "Result should carry a HistoryData payload.");
            Assert.That(historyData!.DataValues.Count, Is.GreaterThan(0),
                "Scalar_Static_Decimal should return seeded historical values.");
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

        [Description("Raw history read returns seeded data that includes Good, Bad, and Uncertain quality status codes.")]
        [Test]
        public async Task HistoryReadRawDataContainsBadAndUncertainQualityAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 500,
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
            HistoryReadResult result = response.Results[0];
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore($"History not supported: {result.StatusCode}");
            }

            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True,
                "Result should carry a HistoryData payload.");
            Assert.That(historyData!.DataValues.Count, Is.GreaterThan(10),
                "Need at least 10 data values to verify quality pattern.");

            bool hasBad = false;
            bool hasUncertain = false;
            bool hasGood = false;
            foreach (DataValue dv in historyData.DataValues)
            {
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    hasBad = true;
                }
                else if (StatusCode.IsUncertain(dv.StatusCode))
                {
                    hasUncertain = true;
                }
                else if (StatusCode.IsGood(dv.StatusCode))
                {
                    hasGood = true;
                }
            }

            Assert.That(hasGood, Is.True, "Seeded history should contain Good quality data.");
            Assert.That(hasBad, Is.True, "Seeded history should contain Bad quality data.");
            Assert.That(hasUncertain, Is.True, "Seeded history should contain Uncertain quality data.");
        }

        private static bool IsUnsupported(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadHistoryOperationUnsupported ||
                statusCode == StatusCodes.BadHistoryOperationInvalid ||
                statusCode == StatusCodes.BadNotSupported;
        }
    }
}
