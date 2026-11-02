/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Scales.Client;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// The error handling of the <see cref="ScalesClient"/> browse helper
    /// against a mocked channel.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScalesClientTests
    {
        /// <summary>
        /// Source and expected status codes for malformed counter reads.
        /// </summary>
        private static TestCaseData[] MalformedTipCounterCases =>
        [
            new(StatusCodes.Good, StatusCodes.BadTypeMismatch),
            new(StatusCodes.BadNoData, StatusCodes.BadNoData)
        ];

        /// <summary>
        /// Source quality partitions for typed item reads.
        /// </summary>
        private static TestCaseData[] ItemQualityCases =>
        [
            new(StatusCodes.Good, true),
            new(StatusCodes.Uncertain, true),
            new(StatusCodes.BadNoCommunication, false)
        ];

        [Test]
        public async Task BrowseOfANodeWithABadStatusYieldsNothingAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }],
                    DiagnosticInfos = []
                }));
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());

            var children = new List<ReferenceDescription>();
            await foreach (ReferenceDescription reference in client
                .BrowseChildrenAsync(new NodeId("Scale", 2), NodeClass.Object, CancellationToken.None)
                .ConfigureAwait(false))
            {
                children.Add(reference);
            }

            Assert.That(children, Is.Empty);
        }

        [Test]
        public async Task BrowseThatFailsAsAServiceCallPropagates()
        {
            // A timeout or a lost session is not an empty folder.
            using SessionMock session = SessionMock.Create();
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(
                    Task.FromException<IServiceResponse>(new ServiceResultException(StatusCodes.BadTimeout))));
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (ReferenceDescription reference in client
                    .BrowseChildrenAsync(new NodeId("Scale", 2), NodeClass.Object, CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    Assert.Fail("No reference was expected, but got " + reference.BrowseName);
                }
            });
            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
        }

        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.UInt64)]
        public async Task TipCounterPreservesConcreteUnsignedRepresentationsAndQualityAsync(BuiltInType type)
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            Variant value = type switch
            {
                BuiltInType.Byte => new Variant(byte.MaxValue),
                BuiltInType.UInt16 => new Variant(ushort.MaxValue),
                BuiltInType.UInt32 => new Variant(uint.MaxValue),
                _ => new Variant(ulong.MaxValue)
            };
            ulong expected = type switch
            {
                BuiltInType.Byte => byte.MaxValue,
                BuiltInType.UInt16 => ushort.MaxValue,
                BuiltInType.UInt32 => uint.MaxValue,
                _ => ulong.MaxValue
            };
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            ResolvePaths(session, 1);
            ReadValues(session, [new DataValue(value, StatusCodes.Uncertain, (DateTimeUtc)timestamp)]);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleField<ulong> actual = await client.ReadTipCounterAsync(new NodeId("Product", 2));
            Assert.That(actual.HasValue, Is.True);
            Assert.That(actual.Value, Is.EqualTo(expected));
            Assert.That(actual.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(actual.Timestamp, Is.EqualTo(timestamp));
        }

        [TestCaseSource(nameof(MalformedTipCounterCases))]
        public async Task MalformedTipCountersRetainErrorsInsteadOfBecomingZeroAsync(
            StatusCode quality, StatusCode expected)
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ResolvePaths(session, 1);
            ReadValues(session, [new DataValue(new Variant(-1), quality, DateTimeUtc.Now)]);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleField<ulong> actual = await client.ReadTipCounterAsync(new NodeId("Product", 2));
            Assert.That(actual.HasValue, Is.False);
            Assert.That(actual.StatusCode, Is.EqualTo((StatusCode)expected));
        }

        [TestCaseSource(nameof(ItemQualityCases))]
        public async Task ItemReadsRetainFieldQualityFlagsUnitsAndSourceTimeAsync(StatusCode quality, bool present)
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            session.Channel.SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<TranslateBrowsePathsToNodeIdsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(ResolvedPaths(1, 1000)))
                .Returns(new ValueTask<IServiceResponse>(ResolvedPaths(21, 2000)))
                .Returns(new ValueTask<IServiceResponse>(ResolvedPaths(2, 3000)));
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var values = new DataValue[23];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = DataValue.FromStatusCode(StatusCodes.BadNoData);
            }
            values[0] = new DataValue(new Variant("measured-item"));
            values[1] = new DataValue(Variant.FromStructure(
                new WeightType { Gross = 3, Net = 2, Tare = 1 }), quality, (DateTimeUtc)timestamp);
            values[2] = new DataValue(Variant.FromStructure(ScaleUnits.Kilogram));
            values[5] = new DataValue(new Variant(12.0), StatusCodes.Uncertain, (DateTimeUtc)timestamp);
            values[6] = new DataValue(Variant.FromStructure(ScaleUnits.Create("MMT", "mm", "millimetre")));
            values[12] = new DataValue(new Variant("not a numeric price"));
            values[13] = new DataValue(Variant.FromStructure(ScaleItemProcessingTests.Euro()));
            values[14] = new DataValue(new Variant(false));
            values[15] = new DataValue(new Variant(false));
            values[16] = new DataValue(new Variant(1));
            values[17] = new DataValue(new Variant(true));
            values[18] = new DataValue(new Variant(false));
            values[19] = new DataValue(new Variant((ushort)7));
            values[20] = new DataValue(new Variant("equipment-weight"));
            values[21] = new DataValue(new Variant(3.75));
            values[22] = new DataValue(Variant.FromStructure(ScaleItemProcessingTests.Euro()));
            ReadValues(session, values);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleLastItemSnapshot actual = await client.ReadLastItemAsync(new NodeId("Product", 2));
            Assert.That(actual.LastItemNodeId, Is.EqualTo(new NodeId(1000u, 2)));
            Assert.That(actual.ItemId.Value, Is.EqualTo("measured-item"));
            Assert.That(actual.MeasuredWeight.HasValue, Is.EqualTo(present));
            Assert.That(actual.MeasuredWeight.StatusCode, Is.EqualTo((StatusCode)quality));
            Assert.That(actual.MeasuredWeight.Timestamp, Is.EqualTo(timestamp));
            Assert.That(actual.MeasuredWeight.Value.Net, Is.EqualTo(2));
            Assert.That(actual.MeasuredWeight.Value.Tare, Is.EqualTo(1));
            Assert.That(actual.MeasuredWeight.Value.EngineeringUnits!.UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));
            Assert.That(actual.MeasuredWeight.Value.WeightId, Is.EqualTo("equipment-weight"));
            Assert.That(actual.MeasuredWeight.Value.Stable, Is.True);
            Assert.That(actual.MeasuredWeight.Value.CurrentRangeId, Is.EqualTo(7));
            Assert.That(actual.MeasuredWeight.Value.TareMode, Is.EqualTo((TareMode)1));
            Assert.That(actual.MeasuredLength.HasValue, Is.True);
            Assert.That(actual.MeasuredLength.Value, Is.EqualTo(12));
            Assert.That(actual.MeasuredLength.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(actual.MeasuredLength.Timestamp, Is.EqualTo(timestamp));
            Assert.That(actual.MeasuredWidth.HasValue, Is.False);
            Assert.That(actual.ItemPrice.HasValue, Is.False);
            Assert.That(actual.ItemPrice.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(actual.UnitPrice.Value, Is.EqualTo(3.75));
            Assert.That(actual.UnitPriceCurrencyUnit.Value.AlphabeticCode, Is.EqualTo("EUR"));
        }

        [Test]
        public async Task RecipeReadsDecodeTypedReportsAndKeepTheirSourceMetadataAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ResolvePaths(session, 2);
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var entry = new RecipeReportElementType
            {
                ReportMessage = new LocalizedText("Condition timed out; intervention required."),
                Timestamp = (DateTimeUtc)timestamp
            };
            ArrayOf<RecipeReportElementType> entries = [entry];
            ReadValues(session,
            [
                new DataValue(new Variant(new NodeId("Recipe", 2))),
                new DataValue(Variant.FromStructure(entries),
                    StatusCodes.Uncertain, (DateTimeUtc)timestamp)
            ]);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleRecipeStatus actual = await client.ReadRecipeStatusAsync(new NodeId("Product", 2));
            Assert.That(actual.RecipeNodeId.HasValue, Is.True);
            Assert.That(actual.RecipeNodeId.Value, Is.EqualTo(new NodeId("Recipe", 2)));
            Assert.That(actual.Report.HasValue, Is.True);
            Assert.That(actual.Report.Value.Count, Is.EqualTo(1));
            Assert.That(actual.Report.Value[0].ReportMessage.Text,
                Is.EqualTo("Condition timed out; intervention required."));
            Assert.That((DateTime)actual.Report.Value[0].Timestamp, Is.EqualTo(timestamp));
            Assert.That(actual.Report.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(actual.Report.Timestamp, Is.EqualTo(timestamp));
        }

        [Test]
        public async Task TypedCounterReadsPropagateServiceFailuresAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ResolvePaths(session, 1);
            session.Channel.Setup(c => c.SendRequestAsync(It.IsAny<ReadRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(
                    Task.FromException<IServiceResponse>(new ServiceResultException(StatusCodes.BadTimeout))));
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadTipCounterAsync(new NodeId("Product", 2)));
            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
        }

        private static void ResolvePaths(SessionMock session, int count)
        {
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.IsAny<TranslateBrowsePathsToNodeIdsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(ResolvedPaths(count, 1000)));
        }

        private static TranslateBrowsePathsToNodeIdsResponse ResolvedPaths(int count, uint firstId)
        {
            var results = new BrowsePathResult[count];
            for (int index = 0; index < count; index++)
            {
                results[index] = new BrowsePathResult
                {
                    Targets =
                    [
                        new BrowsePathTarget
                        {
                            TargetId = new ExpandedNodeId(firstId + (uint)index, 2),
                            RemainingPathIndex = uint.MaxValue
                        }
                    ]
                };
            }
            return new TranslateBrowsePathsToNodeIdsResponse { Results = results.ToArrayOf() };
        }

        private static void ReadValues(SessionMock session, DataValue[] values)
        {
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(request => request.NodesToRead.Count == values.Length),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse { Results = values.ToArrayOf() }));
        }
    }
}
