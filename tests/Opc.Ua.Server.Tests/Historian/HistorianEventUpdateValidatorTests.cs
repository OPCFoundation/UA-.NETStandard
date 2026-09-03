/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistorianEventUpdateValidatorTests
    {
        [Test]
        public void InsertRequiresEventTypeAndTime()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            var details = new UpdateEventDetails
            {
                NodeId = notifier.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = new EventFilter()
            };
            details.Filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);

            ServiceResult result = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                HistorianNodeCapabilities.ReadWrite,
                out _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        public void UpdateRejectsSelectClauseIndexRange()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Update,
                includeEventId: true);
            details.Filter.SelectClauses[^1].IndexRange = "0";

            ServiceResult result = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                HistorianNodeCapabilities.ReadWrite,
                out _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void ReplaceRejectsEventIdIndexRange()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Replace,
                includeEventId: true);
            details.Filter.SelectClauses[0].IndexRange = "0";

            ServiceResult result = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                HistorianNodeCapabilities.ReadWrite,
                out _);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public async Task InsertGeneratesCanonicalEventIdAsync()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);

            _ = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.ConditionType]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                HistorianNodeCapabilities.ReadWrite,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);
            var fields = new HistoryEventFieldList
            {
                EventFields =
                [
                    new Variant(ObjectTypeIds.BaseEventType),
                    new Variant((DateTimeUtc)DateTime.UtcNow),
                    new Variant(new LocalizedText("generated"))
                ]
            };

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    HistorianNodeCapabilities.ReadWrite,
                    fields,
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(decoded.Record, Is.Not.Null);
            Assert.That(decoded.Record!.EventId.IsEmpty, Is.False);
            var eventIdKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(BrowseNames.EventId)],
                Attributes.Value,
                null);
            Assert.That(
                decoded.Record.TryGetQualifiedField(
                    eventIdKey,
                    out Variant eventIdValue),
                Is.True);
            Assert.That(
                eventIdValue.TryGetValue(out ByteString eventId),
                Is.True);
            Assert.That(eventId, Is.EqualTo(decoded.Record.EventId));
        }

        [Test]
        public async Task ShortEventFieldListReturnsArgumentsMissingAsync()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);

            _ = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.ConditionType]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                HistorianNodeCapabilities.ReadWrite,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    HistorianNodeCapabilities.ReadWrite,
                    new HistoryEventFieldList
                    {
                        EventFields = [new Variant(ObjectTypeIds.BaseEventType)]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        public async Task UnsupportedEventTypeIsRejectedAsync()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.ConditionType]
            };
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    capabilities,
                    new HistoryEventFieldList
                    {
                        EventFields =
                        [
                            new Variant(ObjectTypeIds.BaseEventType),
                            new Variant((DateTimeUtc)DateTime.UtcNow),
                            new Variant(new LocalizedText("unsupported"))
                        ]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                decoded.StatusCode,
                Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
        }

        [Test]
        public void InsertRequiresConfiguredMandatoryFields()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventType,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Time,
                Attributes.Value);
            details.Filter = filter;
            var mandatoryMessage = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(BrowseNames.Message)],
                AttributeId = Attributes.Value
            };
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                MandatoryEventFields = [mandatoryMessage]
            };

            ServiceResult result = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        public async Task UnsupportedFieldsReturnGoodDataIgnoredAsync()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);
            var unsupported = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName("Unsupported")],
                AttributeId = Attributes.Value
            };
            details.Filter.SelectClauses =
                details.Filter.SelectClauses.AddItem(unsupported);
            var supported = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(BrowseNames.Message)],
                AttributeId = Attributes.Value
            };
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventFields = [supported]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    capabilities,
                    new HistoryEventFieldList
                    {
                        EventFields =
                        [
                            new Variant(ObjectTypeIds.BaseEventType),
                            new Variant((DateTimeUtc)DateTime.UtcNow),
                            new Variant(new LocalizedText("message")),
                            Variant.From(42)
                        ]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                decoded.StatusCode,
                Is.EqualTo(StatusCodes.GoodDataIgnored));
            Assert.That(decoded.FieldIndexes, Has.Count.EqualTo(1));
            Assert.That(decoded.FieldIndexes[0], Is.EqualTo(3));
            Assert.That(decoded.FieldNames, Has.Count.EqualTo(1));
            Assert.That(decoded.FieldNames[0], Is.EqualTo("Unsupported"));
            Assert.That(
                decoded.Record!.TryGetField(
                    "Unsupported",
                    out _),
                Is.False);
        }

        [Test]
        public async Task BaseEventFieldNameInCustomNamespaceIsIgnoredAsync()
        {
            ServerSystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);
            var customMessage = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath =
                [
                    new QualifiedName(
                        BrowseNames.Message,
                        namespaceIndex: 1)
                ],
                AttributeId = Attributes.Value
            };
            details.Filter.SelectClauses =
                details.Filter.SelectClauses.AddItem(customMessage);
            var supportedMessage = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(BrowseNames.Message)],
                AttributeId = Attributes.Value
            };
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventFields = [supportedMessage]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    capabilities,
                    new HistoryEventFieldList
                    {
                        EventFields =
                        [
                            new Variant(ObjectTypeIds.BaseEventType),
                            new Variant((DateTimeUtc)DateTime.UtcNow),
                            new Variant(new LocalizedText("standard")),
                            new Variant(new LocalizedText("custom"))
                        ]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                decoded.StatusCode,
                Is.EqualTo(StatusCodes.GoodDataIgnored));
            Assert.That(decoded.FieldIndexes, Has.Count.EqualTo(1));
            Assert.That(decoded.FieldIndexes[0], Is.EqualTo(3));
        }

        [Test]
        public async Task DefaultedTimeUsesSelectedOperandIdentityAsync()
        {
            ServerSystemContext context = CreateSystemContext(
                addConditionSubtype: true);
            BaseObjectState notifier = CreateNotifier();
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventType,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.ConditionType,
                BrowseNames.Time,
                Attributes.Value);
            var details = new UpdateEventDetails
            {
                NodeId = notifier.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = filter
            };
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.ConditionType]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    capabilities,
                    new HistoryEventFieldList
                    {
                        EventFields =
                        [
                            new Variant(ObjectTypeIds.ConditionType),
                            Variant.Null
                        ]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.Good));
            var selectedTime =
                HistorianEventFieldKey.FromOperand(
                    filter.SelectClauses[1]);
            Assert.That(
                decoded.Record!.TryGetQualifiedField(
                    selectedTime,
                    out Variant storedTime),
                Is.True);
            Assert.That(
                storedTime.TryGetValue(out DateTimeUtc timestamp),
                Is.True);
            Assert.That(timestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task NullEventTypeAndTimeUseServerDefaultsAsync()
        {
            ServerSystemContext context = CreateSystemContext(
                addConditionSubtype: true);
            BaseObjectState notifier = CreateNotifier();
            UpdateEventDetails details = CreateUpdateDetails(
                notifier.NodeId,
                PerformUpdateType.Insert,
                includeEventId: false);
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.ConditionType]
            };
            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                context,
                notifier,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            Assert.That(ServiceResult.IsGood(validation), Is.True);

            HistorianEventDecodeResult decoded =
                await HistorianEventUpdateValidator.DecodeAsync(
                    context,
                    capabilities,
                    new HistoryEventFieldList
                    {
                        EventFields =
                        [
                            Variant.Null,
                            Variant.Null,
                            new Variant(
                                new LocalizedText("defaulted"))
                        ]
                    },
                    details.Filter,
                    plan,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                decoded.Record!.EventType,
                Is.EqualTo(ObjectTypeIds.ConditionType));
            Assert.That(
                decoded.Record.SourceTimestamp,
                Is.Not.EqualTo(DateTimeUtc.MinValue));
        }

        private static UpdateEventDetails CreateUpdateDetails(
            NodeId nodeId,
            PerformUpdateType updateType,
            bool includeEventId)
        {
            var filter = new EventFilter();
            if (includeEventId)
            {
                filter.AddSelectClause(
                    ObjectTypeIds.BaseEventType,
                    BrowseNames.EventId,
                    Attributes.Value);
            }
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventType,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Time,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            return new UpdateEventDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = updateType,
                Filter = filter
            };
        }

        private static BaseObjectState CreateNotifier()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1),
                EventNotifier = EventNotifiers.HistoryRead |
                    EventNotifiers.HistoryWrite
            };
        }

        private static ServerSystemContext CreateSystemContext(
            bool addConditionSubtype = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:event-history");
            var typeTree = new TypeTable(namespaceUris);
            if (addConditionSubtype)
            {
                typeTree.AddSubtype(
                    ObjectTypeIds.BaseEventType,
                    NodeId.Null);
                typeTree.AddSubtype(
                    ObjectTypeIds.ConditionType,
                    ObjectTypeIds.BaseEventType);
            }
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.TypeTree)
                .Returns(typeTree);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            return new ServerSystemContext(server.Object);
        }
    }
}
