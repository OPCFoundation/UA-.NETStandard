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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies released characteristic, tolerance, correction and mixed-event semantics on real GMS instances.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsResultMeasurementTests
    {
        /// <summary>
        /// Corrections expose calculated values with native quality; optional absence remains observable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CorrectionsRetainRawValuesAndRejectShapeChanges(bool complete)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            GmsCorrection initial = Correction(complete);
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            IMachineHandle<GMSState> machine = await fixture.Stage("CorrectionGms")
                .WithGmsResults(store, out GmsResultBinding binding,
                    new GmsResultBindingOptions { Corrections = [initial] }).BuildAsync();
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            ArrayOf<GmsCorrectionValues> readings = await client.ReadCorrectionsAsync();

            Assert.That(readings, Has.Count.EqualTo(1));
            GmsCorrectionValues reading = readings[0];
            Assert.That(reading.Identifier.WrappedValue.TryGetValue(out string? identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("offset-1"));
            Assert.That(reading.CharacteristicIdentifier.WrappedValue.TryGetValue(out string? characteristic), Is.True);
            Assert.That(characteristic, Is.EqualTo("bore"));
            Assert.That(reading.RelativeValue!.Value.WrappedValue.TryGetValue(out double relative), Is.True);
            Assert.That(relative, Is.EqualTo(0.25));
            Assert.That(reading.RelativeValue.EngineeringUnits.WrappedValue.TryGetValue(out ExtensionObject units), Is.True);
            Assert.That(units.TryGetValue(out EUInformation? eu), Is.True);
            Assert.That(eu!.UnitId, Is.EqualTo(5066068));
            Assert.That(reading.Description.IsNull, Is.EqualTo(!complete));
            Assert.That(reading.ProgramName.IsNull, Is.EqualTo(!complete));
            Assert.That(reading.AbsoluteValue == null, Is.EqualTo(!complete));
            if (complete)
            {
                Assert.That(reading.AbsoluteValue!.Value.WrappedValue.TryGetValue(out double absolute), Is.True);
                Assert.That(absolute, Is.EqualTo(10.25));
                Assert.That(reading.LowerControlLimit!.Value.WrappedValue.TryGetValue(out double lower), Is.True);
                Assert.That(lower, Is.EqualTo(-0.2));
                Assert.That(reading.UpperControlLimit!.Value.WrappedValue.TryGetValue(out double upper), Is.True);
                Assert.That(upper, Is.EqualTo(0.2));
            }

            GmsResultPublisher publisher = binding.CreatePublisher(machine);
            var bad = new GmsAnalogValue(new DataValue(Variant.Null, StatusCodes.BadSensorFailure, s_acquired),
                Analog(0).EngineeringUnits);
            var updated = new GmsCorrection(initial.Identifier, initial.CharacteristicIdentifier, bad, initial.AbsoluteValue)
            {
                LowerControlLimit = initial.LowerControlLimit,
                UpperControlLimit = initial.UpperControlLimit,
                Description = initial.Description,
                ProgramName = initial.ProgramName
            };
            await publisher.PublishCorrectionAsync(updated);
            GmsCorrectionValues actual = (await client.ReadCorrectionsAsync())[0];
            Assert.That(actual.RelativeValue!.Value.StatusCode, Is.EqualTo(StatusCodes.BadSensorFailure));
            Assert.That(actual.RelativeValue.Value.SourceTimestamp, Is.EqualTo(s_acquired));
            Assert.That(actual.RelativeValue.Value.WrappedValue.IsNull, Is.True);
            ServiceResultException shape = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await publisher.PublishCorrectionAsync(new GmsCorrection("offset-1", "different", Analog(1)))))!;
            Assert.That(shape.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            ServiceResultException missing = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await publisher.PublishCorrectionAsync(new GmsCorrection("absent", "bore", Analog(1)))))!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        /// <summary>
        /// Missing folders, empty folders, bad initial values and omitted tool properties remain distinct.
        /// </summary>
        [Test]
        public async Task OptionalAbsenceIsNotReplacedByEmptyGoodValues()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> absent = await fixture.Stage("AbsentCorrections").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding first).BuildAsync();
            IMachineHandle<GMSState> empty = await fixture.Stage("EmptyCorrections").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out _, new GmsResultBindingOptions { Corrections = [], PublishedResults = 0 }).BuildAsync();
            ISession session = await fixture.ConnectAsync();
            var firstClient = new GmsResultClient(session, absent.NodeId, NUnitTelemetryContext.Create());
            var secondClient = new GmsResultClient(session, empty.NodeId, NUnitTelemetryContext.Create());

            Assert.That((await firstClient.ReadCorrectionsAsync()).IsNull, Is.True);
            Assert.That((await secondClient.ReadCorrectionsAsync()).IsEmpty, Is.True);
            Assert.That((await secondClient.ReadPublishedResultsAsync()).IsEmpty, Is.True);
            GmsResultVariableValue initial = (await firstClient.ReadPublishedResultsAsync())[0];
            Assert.That(initial.Value.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(initial.UsedTools.IsNull, Is.True);
            await first.CreatePublisher(absent).PublishAsync(GmsResultStoreTests.Result("one"));
            GmsResultVariableValue measured = (await firstClient.ReadPublishedResultsAsync())[0];
            Assert.That(measured.NodeId.IsNull, Is.False);
            Assert.That(measured.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(measured.UsedTools.IsNull, Is.True);
        }

        /// <summary>
        /// Final and intermediate results select different concrete event subtypes, with genuine characteristic nodes.
        /// </summary>
        [Test]
        public async Task MixedResultEventsCarryCharacteristicsTolerancesAndProgramOverTcp()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("EventGms").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding,
                new GmsResultBindingOptions { EnableIntermediateEvents = true, IncludeUsedTools = true }).BuildAsync();
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            ArrayOf<QualifiedName> names = [new QualifiedName("Bore", machine.NodeId.NamespaceIndex)];
            EventFilter filter = client.CreateIntermediateResultFilter(names);
            filter.WhereClause = new ContentFilter();
            for (int i = 0; i < 13; i++)
            {
                filter.SelectClauses[i].TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            }
            filter.SelectClauses[13].TypeDefinitionId = NodeId.Create(
                Machinery.Result.ObjectTypes.ResultReadyEventType,
                Machinery.Result.Namespaces.MachineryResult, session.NamespaceUris);
            uint subscription = await SubscribeAsync(session, machine.State.ResultManagement!.NodeId, filter);
            try
            {
                GmsResultPublisher publisher = binding.CreatePublisher(machine);
                await publisher.PublishAsync(GmsResultStoreTests.Result("final"));
                await publisher.PublishAsync(new GmsResult(
                    GmsResultStoreTests.Result("intermediate").Value.WithSourceTimestamp(s_acquired),
                    ["probe-7"], [Characteristic()], "InspectBore", isIntermediate: true));
                ArrayOf<EventFieldList> events = await ReceiveEventsAsync(session, subscription, 2);
                Assert.That(events, Has.Count.EqualTo(2));
                Assert.That(events[0].EventFields[1].TryGetValue(out NodeId finalType), Is.True);
                Assert.That(events[1].EventFields[1].TryGetValue(out NodeId intermediateType), Is.True);
                Assert.That(finalType, Is.Not.EqualTo(intermediateType));
                NodeId intermediateBase = NodeId.Create(ObjectTypes.IntermediateResultEventType,
                    GmsModel.NamespaceUri, session.NamespaceUris);
                Assert.That(await session.NodeCache.IsTypeOfAsync(intermediateType, intermediateBase), Is.True);
                Assert.That(await session.NodeCache.IsTypeOfAsync(finalType, intermediateBase), Is.False);
                ReadResponse type = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = intermediateType, AttributeId = Attributes.IsAbstract }],
                    CancellationToken.None);
                Assert.That(type.Results[0].WrappedValue.TryGetValue(out bool isAbstract), Is.True);
                Assert.That(isAbstract, Is.False);
                GmsIntermediateResultNotification notification =
                    await client.DecodeIntermediateResultAsync(events[1].EventFields, names);
                Assert.That(notification.Event.EventType, Is.EqualTo(intermediateType));
                Assert.That(notification.Event.ProgramName, Is.EqualTo("InspectBore"));
                Assert.That(notification.Event.Result!.ResultMetaData!.ResultId, Is.EqualTo("intermediate"));
                GmsCharacteristicEventValues value = notification.Characteristics[0];
                Assert.That(value.Name, Is.EqualTo(names[0]));
                Assert.That(value.Identifier.TryGetValue(out string? identifier), Is.True);
                Assert.That(identifier, Is.EqualTo("bore"));
                Assert.That(value.ResultValue.TryGetValue(out double measured), Is.True);
                Assert.That(measured, Is.EqualTo(10.25));
                Assert.That(value.UpperTolerance.TryGetValue(out double upper), Is.True);
                Assert.That(upper, Is.EqualTo(10.5));
                Assert.That(value.LowerTolerance.TryGetValue(out double lower), Is.True);
                Assert.That(lower, Is.EqualTo(9.5));
                Assert.That(value.UpperToleranceForm.TryGetValue(out int upperForm), Is.True);
                Assert.That(upperForm, Is.EqualTo(1));
                Assert.That(value.LowerToleranceForm.TryGetValue(out int lowerForm), Is.True);
                Assert.That(lowerForm, Is.EqualTo(2));
                Assert.That(value.Nominal.TryGetValue(out double nominal), Is.True);
                Assert.That(nominal, Is.EqualTo(10.0));
                Assert.That(value.CharacteristicsClass.TryGetValue(out byte importance), Is.True);
                Assert.That(importance, Is.EqualTo(4));
                Assert.That(value.Evaluation.TryGetValue(out int evaluation), Is.True);
                Assert.That(evaluation, Is.EqualTo(1));
                Assert.That(value.IsValid.TryGetValue(out bool valid), Is.True);
                Assert.That(valid, Is.True);
                Assert.That(value.Formula.TryGetValue(out string? formula), Is.True);
                Assert.That(formula, Is.EqualTo("diameter"));
                foreach (Variant unit in new[]
                {
                    value.EngineeringUnits, value.UpperToleranceUnits, value.LowerToleranceUnits, value.NominalUnits
                })
                {
                    Assert.That(unit.TryGetValue(out ExtensionObject extension), Is.True);
                    Assert.That(extension.TryGetValue(out EUInformation? eu), Is.True);
                    Assert.That(eu!.UnitId, Is.EqualTo(5066068));
                }
            }
            finally
            {
                await session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None);
            }
        }

        /// <summary>
        /// Creates a complete released characteristic with independently known expected values.
        /// </summary>
        internal static GmsCharacteristic Characteristic()
        {
            return new GmsCharacteristic("Bore")
            {
                Identifier = "bore",
                ResultValue = Analog(10.25),
                Nominal = Analog(10),
                UpperTolerance = new GmsTolerance(Analog(10.5), ToleranceLimitEnum.LimitValue),
                LowerTolerance = new GmsTolerance(Analog(9.5), ToleranceLimitEnum.NaturalLimit),
                CharacteristicsClass = 4,
                Evaluation = ResultEvaluationEnum.OK,
                IsValid = true,
                Formula = "diameter"
            };
        }

        /// <summary>
        /// Creates a correction with either minimal or complete optional metadata.
        /// </summary>
        internal static GmsCorrection Correction(bool complete)
        {
            return new GmsCorrection("offset-1", "bore", Analog(0.25), complete ? Analog(10.25) : null)
            {
                LowerControlLimit = complete ? Analog(-0.2) : null,
                UpperControlLimit = complete ? Analog(0.2) : null,
                Description = complete ? new LocalizedText("Bore diameter deviation") : default,
                ProgramName = complete ? "InspectBore" : null
            };
        }

        /// <summary>
        /// Creates a millimetre sample with a fixed acquisition time.
        /// </summary>
        internal static GmsAnalogValue Analog(double value)
        {
            return new GmsAnalogValue(new DataValue(new Variant(value), StatusCodes.Good, s_acquired),
                new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 5066068,
                    DisplayName = new LocalizedText("mm")
                });
        }

        /// <summary>
        /// Creates and awaits one real event monitored item before publishing test events.
        /// </summary>
        internal static async Task<uint> SubscribeAsync(ISession session, NodeId notifier, EventFilter filter)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 25, 1000, 10, 0, true, 0, CancellationToken.None);
            CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = notifier, AttributeId = Attributes.EventNotifier },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            QueueSize = 8,
                            DiscardOldest = false,
                            Filter = new ExtensionObject(filter)
                        }
                    }
                ], CancellationToken.None);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscription.SubscriptionId;
        }

        /// <summary>
        /// Receives bounded actual Publish responses, acknowledging only the subscription created by this test.
        /// </summary>
        internal static async Task<ArrayOf<EventFieldList>> ReceiveEventsAsync(
            ISession session,
            uint subscription,
            int count)
        {
            var events = new List<EventFieldList>();
            ArrayOf<SubscriptionAcknowledgement> acknowledgments = [];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (events.Count < count)
            {
                PublishResponse response = await session.PublishAsync(null, acknowledgments, timeout.Token);
                acknowledgments = [new SubscriptionAcknowledgement
                {
                    SubscriptionId = subscription,
                    SequenceNumber = response.NotificationMessage.SequenceNumber
                }];
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (notification.TryGetValue(out EventNotificationList? list))
                    {
                        foreach (EventFieldList value in list.Events)
                        {
                            events.Add(value);
                        }
                    }
                }
            }
            return events.ToArrayOf();
        }

        private static readonly DateTimeUtc s_acquired = new(2026, 1, 2, 3, 4, 5);
    }
}
