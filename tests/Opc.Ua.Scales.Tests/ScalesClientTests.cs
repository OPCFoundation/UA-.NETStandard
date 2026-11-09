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
using Opc.Ua.Client;
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

        [Test]
        public async Task GetKindAsyncRecognizesConcreteScaleTypeWithoutWalkingSuperTypes()
        {
            (Mock<ISession> session, Mock<INodeCache> cache, ushort scalesNamespaceIndex, _) =
                CreateSessionWithNodeCache();
            var client = new ScalesClient(session.Object, NUnitTelemetryContext.Create());
            var checkweigherType = new NodeId(
                ScalesModel.ObjectTypeOf(ScaleKind.Checkweigher),
                scalesNamespaceIndex);

            ScaleKind? actual = await client.GetKindAsync(checkweigherType);

            Assert.That(actual, Is.EqualTo(ScaleKind.Checkweigher));
            cache.Verify(
                value => value.FindSuperTypeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetKindAsyncRecognizesScaleKindThroughVendorSuperTypeChain()
        {
            (Mock<ISession> session, Mock<INodeCache> cache, ushort scalesNamespaceIndex, ushort vendorNamespaceIndex) =
                CreateSessionWithNodeCache();
            var vendorType = new NodeId("VendorCheckweigherType", vendorNamespaceIndex);
            var vendorBaseType = new NodeId("VendorScaleType", vendorNamespaceIndex);
            var checkweigherType = new NodeId(
                ScalesModel.ObjectTypeOf(ScaleKind.Checkweigher),
                scalesNamespaceIndex);
            cache.Setup(value => value.FindSuperTypeAsync(vendorType, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(vendorBaseType));
            cache.Setup(value => value.FindSuperTypeAsync(vendorBaseType, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(checkweigherType));
            var client = new ScalesClient(session.Object, NUnitTelemetryContext.Create());

            ScaleKind? actual = await client.GetKindAsync(vendorType);

            Assert.That(actual, Is.EqualTo(ScaleKind.Checkweigher));
            cache.Verify(
                value => value.FindSuperTypeAsync(vendorType, It.IsAny<CancellationToken>()),
                Times.Once);
            cache.Verify(
                value => value.FindSuperTypeAsync(vendorBaseType, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GetKindAsyncReturnsNullForUnrelatedOrOutOfNamespaceType(bool collidesWithScalesTypeId)
        {
            (Mock<ISession> session, Mock<INodeCache> cache, ushort scalesNamespaceIndex, ushort vendorNamespaceIndex) =
                CreateSessionWithNodeCache();
            uint typeId = collidesWithScalesTypeId
                ? ScalesModel.ObjectTypeOf(ScaleKind.Checkweigher)
                : uint.MaxValue;
            ushort typeNamespaceIndex = collidesWithScalesTypeId
                ? vendorNamespaceIndex
                : scalesNamespaceIndex;
            var unknownType = new NodeId(typeId, typeNamespaceIndex);
            cache.Setup(value => value.FindSuperTypeAsync(unknownType, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(NodeId.Null));
            var client = new ScalesClient(session.Object, NUnitTelemetryContext.Create());

            ScaleKind? actual = await client.GetKindAsync(unknownType);

            Assert.That(actual, Is.Null);
            cache.Verify(
                value => value.FindSuperTypeAsync(unknownType, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task GetKindAsyncStopsAfterSixteenSuperTypeLookups()
        {
            (Mock<ISession> session, Mock<INodeCache> cache, ushort scalesNamespaceIndex, ushort vendorNamespaceIndex) =
                CreateSessionWithNodeCache();
            var cyclicType = new NodeId("VendorCycle", vendorNamespaceIndex);
            cache.Setup(value => value.FindSuperTypeAsync(cyclicType, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(cyclicType));
            var client = new ScalesClient(session.Object, NUnitTelemetryContext.Create());

            ScaleKind? actual = await client.GetKindAsync(cyclicType);

            Assert.That(actual, Is.Null);
            cache.Verify(
                value => value.FindSuperTypeAsync(cyclicType, It.IsAny<CancellationToken>()),
                Times.Exactly(16));
        }

        [TestCase(false, "absent")]
        [TestCase(false, "present")]
        [TestCase(false, "invalid")]
        [TestCase(true, "absent")]
        [TestCase(true, "present")]
        [TestCase(true, "invalid")]
        public void NotificationHelpSourcePreservesExistingEventAndAlarmFields(bool alarm, string help)
        {
            using SessionMock session = SessionMock.Create();
            ushort scalesIndex = (ushort)session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            var fields = new Variant[help == "absent" ? 11 : 12];
            var source = new NodeId("Scale", scalesIndex);
            fields[0] = Variant.From(source);
            fields[1] = Variant.From("Line scale");
            fields[4] = Variant.From(new LocalizedText("Inspection needed"));
            fields[6] = Variant.From(5001u);
            fields[7] = Variant.From("INSPECTION");
            fields[9] = Variant.From(new NodeId(
                alarm ? ObjectTypes.ScaleAlarmType : ObjectTypes.ScaleEventType, scalesIndex));
            fields[10] = alarm ? Variant.From(true) : Variant.Null;
            if (help != "absent")
            {
                fields[11] = help == "present" ? Variant.From("manual:inspection") : Variant.From(42);
            }

            ScaleNotificationInfo actual = client.Decode(fields.ToArrayOf());

            Assert.That(actual.SourceNode, Is.EqualTo(source));
            Assert.That(actual.SourceName, Is.EqualTo("Line scale"));
            Assert.That(actual.Message.Text, Is.EqualTo("Inspection needed"));
            Assert.That(actual.NotificationId, Is.EqualTo(5001u));
            Assert.That(actual.VendorNotificationId, Is.EqualTo("INSPECTION"));
            Assert.That(actual.IsAlarm, Is.EqualTo(alarm));
            Assert.That(actual.Active, Is.EqualTo(alarm ? true : (bool?)null));
            Assert.That(actual.HelpSource, Is.EqualTo(help == "present" ? "manual:inspection" : null));
        }

        [Test]
        public void NotificationFilterAppendsHelpSourceWithoutMovingExistingFields()
        {
            using SessionMock session = SessionMock.Create();
            ushort scalesIndex = (ushort)session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());

            EventFilter filter = client.CreateNotificationFilter();

            Assert.That(filter.SelectClauses.Count, Is.EqualTo(12));
            Assert.That(filter.SelectClauses[9].BrowsePath[0],
                Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.EventType)));
            Assert.That(filter.SelectClauses[10].BrowsePath[0],
                Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.ActiveState)));
            Assert.That(filter.SelectClauses[11].BrowsePath[0],
                Is.EqualTo(new QualifiedName(BrowseNames.HelpSource, scalesIndex)));
            Assert.That(filter.SelectClauses[11].AttributeId, Is.EqualTo(Attributes.Value));
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
            Assert.That(actual.MeasuredWeight.Value.TareMode, Is.EqualTo(TareMode.MeasuredTare_1));
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

        [TestCase(BuiltInType.Byte, 255UL)]
        [TestCase(BuiltInType.UInt16, 65535UL)]
        [TestCase(BuiltInType.UInt32, 4000000000UL)]
        [TestCase(BuiltInType.UInt64, 4294967295UL)]
        public async Task PieceCountingTotalsPreserveUnsignedWireRepresentationsAsync(
            BuiltInType type,
            ulong expected)
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ResolvePaths(session, 3);
            Variant count = type switch
            {
                BuiltInType.Byte => new Variant((byte)expected),
                BuiltInType.UInt16 => new Variant((ushort)expected),
                BuiltInType.UInt32 => new Variant((uint)expected),
                _ => new Variant(expected)
            };
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            ReadValues(session,
            [
                new DataValue(count, StatusCodes.Uncertain, (DateTimeUtc)timestamp),
                new DataValue(count, StatusCodes.Good, (DateTimeUtc)timestamp),
                new DataValue(Variant.FromStructure(new WeightType { Gross = 2, Net = 2, Tare = 0 }),
                    StatusCodes.Good, (DateTimeUtc)timestamp)
            ]);

            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScalePieceCountingSnapshot actual = await client.ReadPieceCountingTotalsAsync(
                new NodeId("Product", 2));

            Assert.That(actual.RegisteredPieceCount.HasValue, Is.True);
            Assert.That(actual.RegisteredPieceCount.Value, Is.EqualTo((uint)expected));
            Assert.That(actual.RegisteredPieceCount.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(actual.RegisteredPieceCount.Timestamp, Is.EqualTo(timestamp));
            Assert.That(actual.TotalizedItemCount.HasValue, Is.True);
            Assert.That(actual.TotalizedItemCount.Value, Is.EqualTo((uint)expected));
            Assert.That(actual.TotalizedItemCount.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(actual.TotalizedWeight.HasValue, Is.True);
            Assert.That(actual.TotalizedWeight.Value.Net, Is.EqualTo(2));
            Assert.That(actual.TotalizedWeight.Timestamp, Is.EqualTo(timestamp));
        }

        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Int32)]
        public async Task PieceCountingTotalsRejectOutOfRangeOrSignedCountsAsync(BuiltInType type)
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ResolvePaths(session, 3);
            Variant count = type == BuiltInType.UInt64
                ? new Variant((ulong)uint.MaxValue + 1)
                : new Variant(-1);
            ReadValues(session,
            [
                new DataValue(count, StatusCodes.Good),
                new DataValue(new Variant(9u), StatusCodes.Good),
                new DataValue(Variant.FromStructure(new WeightType { Gross = 2, Net = 2, Tare = 0 }),
                    StatusCodes.Good)
            ]);

            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScalePieceCountingSnapshot actual = await client.ReadPieceCountingTotalsAsync(
                new NodeId("Product", 2));

            Assert.That(actual.RegisteredPieceCount.HasValue, Is.False);
            Assert.That(actual.RegisteredPieceCount.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(actual.TotalizedItemCount.HasValue, Is.True);
            Assert.That(actual.TotalizedItemCount.Value, Is.EqualTo(9u));
            Assert.That(actual.TotalizedWeight.HasValue, Is.True);
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

        [Test]
        public async Task ReadLastItemAsyncUnresolvedOptionalLastItemReturnsNullIdAndPreservesMissingFieldsAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            var product = new NodeId("Product", 2);
            var translationRequests = new List<TranslateBrowsePathsToNodeIdsRequest>();
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.IsAny<TranslateBrowsePathsToNodeIdsRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((TranslateBrowsePathsToNodeIdsRequest request, CancellationToken _) =>
                {
                    translationRequests.Add(request);
                    TranslateBrowsePathsToNodeIdsResponse response = translationRequests.Count switch
                    {
                        1 => new TranslateBrowsePathsToNodeIdsResponse
                        {
                            Results = [new BrowsePathResult { Targets = [] }]
                        },
                        2 => ResolvedPaths(2, 3000),
                        _ => throw new InvalidOperationException("Unexpected path translation.")
                    };
                    return new ValueTask<IServiceResponse>(response);
                });

            // An unresolved LastItem leaves 21 null item IDs. ReadDataValuesAsync
            // omits those IDs, so the service response contains only the two
            // independently resolved product-price values.
            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var readValues = new[]
            {
                new DataValue(new Variant(3.75), StatusCodes.Good, (DateTimeUtc)timestamp),
                new DataValue(Variant.FromStructure(ScaleItemProcessingTests.Euro()),
                    StatusCodes.Good, (DateTimeUtc)timestamp)
            };
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(request => request.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(
                    new ReadResponse { Results = readValues.ToArrayOf() }));

            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleLastItemSnapshot actual = await client.ReadLastItemAsync(product);

            Assert.That(translationRequests, Has.Count.EqualTo(2));
            Assert.That(translationRequests[0].BrowsePaths.Count, Is.EqualTo(1));
            Assert.That(translationRequests[0].BrowsePaths[0].StartingNode, Is.EqualTo(product));
            Assert.That(translationRequests[0].BrowsePaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.LastItem));
            Assert.That(translationRequests[1].BrowsePaths.Count, Is.EqualTo(2));
            Assert.That(translationRequests[1].BrowsePaths[0].StartingNode, Is.EqualTo(product));
            Assert.That(translationRequests[1].BrowsePaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.UnitPrice));
            Assert.That(translationRequests[1].BrowsePaths[1].StartingNode, Is.EqualTo(product));
            Assert.That(translationRequests[1].BrowsePaths[1].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.UnitPrice));
            Assert.That(translationRequests[1].BrowsePaths[1].RelativePath.Elements[1].TargetName.Name,
                Is.EqualTo(BrowseNames.CurrencyUnit));
            session.Channel.Verify(c => c.SendRequestAsync(
                It.Is<ReadRequest>(request =>
                    request.NodesToRead.Count == 2 &&
                    request.NodesToRead[0].NodeId == new NodeId(3000u, 2) &&
                    request.NodesToRead[1].NodeId == new NodeId(3001u, 2)),
                It.IsAny<CancellationToken>()), Times.Once);

            Assert.That(actual.ProductNodeId, Is.EqualTo(product));
            Assert.That(actual.LastItemNodeId, Is.EqualTo(NodeId.Null));
            Assert.That(actual.ItemId.HasValue, Is.False);
            Assert.That(actual.ItemId.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(actual.MeasuredWeight.HasValue, Is.False);
            Assert.That(actual.MeasuredWeight.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(actual.ItemPrice.HasValue, Is.False);
            Assert.That(actual.ItemPrice.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(actual.UnitPrice.HasValue, Is.True);
            Assert.That(actual.UnitPrice.Value, Is.EqualTo(3.75));
            Assert.That(actual.UnitPrice.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(actual.UnitPrice.Timestamp, Is.EqualTo(timestamp));
            Assert.That(actual.UnitPriceCurrencyUnit.HasValue, Is.True);
            Assert.That(actual.UnitPriceCurrencyUnit.Value.AlphabeticCode, Is.EqualTo("EUR"));
            Assert.That(actual.UnitPriceCurrencyUnit.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(actual.UnitPriceCurrencyUnit.Timestamp, Is.EqualTo(timestamp));
        }

        [Test]
        public async Task ReadLastItemAsyncGoodStatusNonWeightPayloadReturnsBadTypeMismatchForWeightAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            var product = new NodeId("Product", 2);
            var translationRequests = new List<TranslateBrowsePathsToNodeIdsRequest>();
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.IsAny<TranslateBrowsePathsToNodeIdsRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((TranslateBrowsePathsToNodeIdsRequest request, CancellationToken _) =>
                {
                    translationRequests.Add(request);
                    TranslateBrowsePathsToNodeIdsResponse response = translationRequests.Count switch
                    {
                        1 => ResolvedPaths(1, 1000),
                        2 => ResolvedPaths(21, 2000),
                        3 => ResolvedPaths(2, 3000),
                        _ => throw new InvalidOperationException("Unexpected path translation.")
                    };
                    return new ValueTask<IServiceResponse>(response);
                });

            DateTime timestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var values = new DataValue[23];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = DataValue.FromStatusCode(StatusCodes.BadNoData);
            }
            values[1] = new DataValue(new Variant("not a WeightType"), StatusCodes.Good, (DateTimeUtc)timestamp);
            values[21] = new DataValue(new Variant(3.75));
            values[22] = new DataValue(Variant.FromStructure(ScaleItemProcessingTests.Euro()));
            ReadValues(session, values);

            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            ScaleLastItemSnapshot actual = await client.ReadLastItemAsync(product);

            Assert.That(translationRequests, Has.Count.EqualTo(3));
            Assert.That(translationRequests[0].BrowsePaths.Count, Is.EqualTo(1));
            Assert.That(translationRequests[0].BrowsePaths[0].StartingNode, Is.EqualTo(product));
            Assert.That(translationRequests[1].BrowsePaths.Count, Is.EqualTo(21));
            Assert.That(translationRequests[1].BrowsePaths[0].StartingNode, Is.EqualTo(new NodeId(1000u, 2)));
            Assert.That(translationRequests[2].BrowsePaths.Count, Is.EqualTo(2));
            Assert.That(translationRequests[2].BrowsePaths[0].StartingNode, Is.EqualTo(product));
            Assert.That(actual.MeasuredWeight.HasValue, Is.False);
            Assert.That(actual.MeasuredWeight.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(actual.MeasuredWeight.Timestamp, Is.EqualTo(timestamp));
            Assert.That(double.IsNaN(actual.MeasuredWeight.Value.Net), Is.True);
        }

        [Test]
        public async Task ObserveRecipeReportsAsyncMissingReportCompletesEmptyWithoutStreamingFallbackAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            var product = new NodeId("Product", 2);
            TranslateBrowsePathsToNodeIdsRequest? translationRequest = null;
            session.Channel.Setup(c => c.SendRequestAsync(
                    It.IsAny<TranslateBrowsePathsToNodeIdsRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((TranslateBrowsePathsToNodeIdsRequest request, CancellationToken _) =>
                {
                    translationRequest = request;
                    return new ValueTask<IServiceResponse>(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = [new BrowsePathResult { Targets = [] }]
                    });
                });
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());
            var observed = new List<ScaleField<ArrayOf<RecipeReportElementType>>>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await foreach (ScaleField<ArrayOf<RecipeReportElementType>> report in
                client.ObserveRecipeReportsAsync(product, streaming: null, cancellationToken: cancellation.Token))
            {
                observed.Add(report);
            }

            Assert.That(translationRequest, Is.Not.Null);
            Assert.That(translationRequest!.BrowsePaths.Count, Is.EqualTo(1));
            Assert.That(translationRequest.BrowsePaths[0].StartingNode, Is.EqualTo(product));
            Assert.That(translationRequest.BrowsePaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.Report));
            Assert.That(observed, Is.Empty);
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

        private static (
            Mock<ISession> Session,
            Mock<INodeCache> Cache,
            ushort ScalesNamespaceIndex,
            ushort VendorNamespaceIndex)
            CreateSessionWithNodeCache()
        {
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            var session = new Mock<ISession>(MockBehavior.Strict);
            var namespaceUris = new NamespaceTable();
            ushort scalesNamespaceIndex = (ushort)namespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ushort vendorNamespaceIndex =
                (ushort)namespaceUris.GetIndexOrAppend("urn:example:scales:vendor");
            session.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(value => value.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            return (session, cache, scalesNamespaceIndex, vendorNamespaceIndex);
        }
    }
}
