// Archie - December 17 2024
// Requires discussion with Part 9 Editor
#define AddActiveState

using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test FilterRetain on MonitoredItem
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class FilterRetainTests
    {
        private SystemContext m_systemContext;
        private IFilterContext m_filterContext;

        internal static readonly LocalizedText InService = new("en", "In Service");
        internal static readonly LocalizedText OutOfService = new("en", "Out of Service");
        internal static readonly LocalizedText Unsuppressed = new("en-US", "Unsuppressed");

        internal static readonly LocalizedText Active = new("en-US", "Active");

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNotFilterTarget(bool pass)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            SystemContext systemContext = GetSystemContext(telemetry);
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: false,
                filterRetainValue: false,
                telemetry: telemetry);
            LimitAlarmStates desiredState = LimitAlarmStates.Inactive;
            if (pass)
            {
                desiredState = LimitAlarmStates.High;
            }
            alarm.SetLimitState(systemContext, desiredState);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
            CanSendFilteredAlarm(monitoredItem, GetFilterContext(telemetry), filter, alarm, pass, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNonConditionState(bool pass)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            SystemContext systemContext = GetSystemContext(telemetry);
            var alarm = new DeviceFailureEventState(null);
            alarm.Create(
                systemContext,
                new NodeId(12345, 1),
                new QualifiedName("AnyAlarm", 1),
                new LocalizedText(string.Empty, "AnyAlarm"),
                true);

            alarm.EventType.Value = ObjectTypeIds.DeviceFailureEventType;

            IFilterContext context = GetFilterContext(telemetry);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: !pass, telemetry);

            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
            CanSendFilteredAlarm(monitoredItem, context, filter, alarm, pass, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNonEvent(bool pass)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var certificateType = new ApplicationCertificateState(null);

            IFilterContext context = GetFilterContext(telemetry);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: !pass, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
            CanSendFilteredAlarm(monitoredItem, context, filter, certificateType, pass, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Set SupportsFilteredRetain False")]
        [TestCase(true, Description = "Set SupportsFilteredRetain True")]
        public void TestFilteredRetainExists(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            alarm.SetLimitState(GetSystemContext(telemetry), LimitAlarmStates.Inactive);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            CanSendFilteredAlarm(monitoredItem, GetFilterContext(telemetry), filter, alarm, expected: false, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestCanSendMultiple(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            IFilterContext filterContext = GetFilterContext(telemetry);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.HighHigh);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected: supportsFilteredRetain,
                telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestCanSendOnceSimple(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected: supportsFilteredRetain,
                telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestSendMultiple(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.HighHigh);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected: supportsFilteredRetain,
                telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected: supportsFilteredRetain,
                telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Low);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false, telemetry);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void SpecB14(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // https://reference.opcfoundation.org/Core/Part9/v105/docs/B.1.4

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);

            alarm.SetSuppressedState(systemContext, suppressed: false);
            alarm.OutOfServiceState.Value = InService;

            IFilterContext filterContext = GetFilterContext(telemetry);
            var filter = new EventFilter
            {
                SelectClauses = GetSelectFields(),
                WhereClause = GetStateFilter()
            };
            _ = filter.Validate(filterContext);

            MonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            // 16 States in Table B.3

            // 1 Alarm Goes Active
            Debug.WriteLine("// 1 Alarm Goes Active");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            bool expected = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 2 Placed Out of Service - the trailing event, "Retain sent" = False
            Debug.WriteLine("// 2 Placed Out of Service");
            alarm.OutOfServiceState.Value = OutOfService;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected,
                telemetry,
                expectedOverrideRetain: supportsFilteredRetain);

            // 3 Alarm Suppressed; No event since OutOfService
            Debug.WriteLine("// 3 Alarm Suppressed; No event since OutOfService");
            alarm.SetSuppressedState(systemContext, suppressed: true);
            expected = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 4 Alarm goes inactive; No event since OutOfService
            Debug.WriteLine("// 4 Alarm goes inactive; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 5 Alarm not Suppressed; No event since not active
            Debug.WriteLine("// 5 Alarm not Suppressed; No event since not active");
            alarm.SetSuppressedState(systemContext, suppressed: false);
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 6 Alarm goes active; No event since OutOfService
            Debug.WriteLine("// 6 Alarm goes active; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 7 Alarm no longer OutOfService; Event generated
            Debug.WriteLine("// 7 Alarm no longer OutOfService; Event generated");
            alarm.OutOfServiceState.Value = InService;
            expected = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 8 Alarm goes inactive - with the ActiveState clause this is the trailing
            // event, "Retain sent" = False
            Debug.WriteLine("// 8 Alarm goes inactive");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                filter,
                alarm,
                expected,
                telemetry,
                expectedOverrideRetain: supportsFilteredRetain);

            // 9 Alarm Suppressed; No event since not active
            Debug.WriteLine("// 9 Alarm Suppressed; No event since not active");
            alarm.SetSuppressedState(systemContext, suppressed: true);
            expected = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 10 Alarm goes active; No event since Suppressed
            Debug.WriteLine("// 10 Alarm goes active; No event since Suppressed");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 11 Alarm goes inactive; No event since Suppressed
            Debug.WriteLine("// 11 Alarm goes inactive; No event since Suppressed");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 12 Alarm no longer Suppressed
            Debug.WriteLine("// 12 Alarm no longer Suppressed");
            alarm.SetSuppressedState(systemContext, suppressed: false);
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 13 Placed OutOfService
            Debug.WriteLine("// 13 Placed OutOfService");
            alarm.OutOfServiceState.Value = OutOfService;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 14 Alarm goes active; No event since OutOfService
            Debug.WriteLine("// 14 Alarm goes active; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 15 Alarm goes inactive; No event since OutOfService
            Debug.WriteLine("// 15 Alarm goes inactive; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 16 Alarm no longer OutOfService
            Debug.WriteLine("// 16 Alarm no longer OutOfService");
            alarm.OutOfServiceState.Value = InService;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);
        }

        /// <summary>
        /// Part 9, 5.5.2: the trailing event a condition produces on its way out of a
        /// client's where clause carries a client specific Retain = false, whatever the
        /// server retains - otherwise that client keeps an alarm it should drop. The
        /// snapshot is shared by every item the condition is reported to, so an item whose
        /// filter still passes has to keep reading the server's real value from it.
        /// </summary>
        [Test]
        public void TrailingEventIsDeliveredWithRetainFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);

            // one client only wants High alarms, the other wants every event.
            EventFilter highOnly = GetRetainEventFilter(GetHighOnlyFilter(), telemetry);
            EventFilter everything = GetRetainEventFilter(new ContentFilter(), telemetry);
            MonitoredItem highOnlyItem = CreateMonitoredItem(highOnly, telemetry);
            MonitoredItem everythingItem = CreateMonitoredItem(everything, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;

            InstanceStateSnapshot inScope = CreateSnapshot(alarm, telemetry);
            highOnlyItem.QueueEvent(inScope);
            everythingItem.QueueEvent(inScope);

            Assert.That(PublishRetain(highOnlyItem), Is.True, "in scope: the server's value");
            Assert.That(PublishRetain(everythingItem), Is.True);

            // the alarm leaves the High-only client's scope while the server still retains
            // it - the shape of an alarm that was acknowledged but is not yet inactive.
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = true;

            InstanceStateSnapshot outOfScope = CreateSnapshot(alarm, telemetry);
            highOnlyItem.QueueEvent(outOfScope);
            everythingItem.QueueEvent(outOfScope);

            Assert.That(
                PublishRetain(highOnlyItem),
                Is.False,
                "the trailing event carries the client specific Retain");
            Assert.That(
                PublishRetain(everythingItem),
                Is.True,
                "an item the condition still passes reads the server's value");
            Assert.That(
                ReadRetain(outOfScope, filterContext),
                Is.True,
                "the shared snapshot is untouched");
        }

        /// <summary>
        /// The override belongs to the single transition out of scope. Once the condition
        /// passes the where clause again the client is back to the server's value.
        /// </summary>
        [Test]
        public void RetainOverrideAppliesToTheTrailingEventOnly()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            EventFilter highOnly = GetRetainEventFilter(GetHighOnlyFilter(), telemetry);
            MonitoredItem monitoredItem = CreateMonitoredItem(highOnly, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            monitoredItem.QueueEvent(CreateSnapshot(alarm, telemetry));
            Assert.That(PublishRetain(monitoredItem), Is.True);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = true;
            monitoredItem.QueueEvent(CreateSnapshot(alarm, telemetry));
            Assert.That(PublishRetain(monitoredItem), Is.False, "trailing event");

            // still out of scope and no longer tracked: nothing is delivered at all.
            monitoredItem.QueueEvent(CreateSnapshot(alarm, telemetry));
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            monitoredItem.QueueEvent(CreateSnapshot(alarm, telemetry));
            Assert.That(PublishRetain(monitoredItem), Is.True, "back in scope: the server's value");
        }

        /// <summary>
        /// A select clause the wrapper cannot resolve - here Retain asked for on a type the
        /// condition is not - must stay null rather than be turned into a false.
        /// </summary>
        [Test]
        public void RetainOverrideLeavesUnresolvedClausesAlone()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IFilterContext filterContext = GetFilterContext(telemetry);

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);
            alarm.Retain.Value = true;

            var wrapped = new FilteredRetainTarget(CreateSnapshot(alarm, telemetry));
            QualifiedNameCollection retainPath = [.. new QualifiedName[] { BrowseNames.Retain }];

            object overridden = wrapped.GetAttributeValue(
                filterContext,
                ObjectTypeIds.ConditionType,
                retainPath,
                Attributes.Value,
                NumericRange.Empty);
            Assert.That(overridden, Is.EqualTo(false));

            object unresolved = wrapped.GetAttributeValue(
                filterContext,
                ObjectTypeIds.AuditEventType,
                retainPath,
                Attributes.Value,
                NumericRange.Empty);
            Assert.That(unresolved, Is.Null);
        }

        private const int kRetainFieldIndex = 1;

        /// <summary>
        /// Select clauses with a known slot for Retain and no localized text, so the item
        /// does not need a resource manager to build the field list.
        /// </summary>
        private EventFilter GetRetainEventFilter(
            ContentFilter whereClause,
            ITelemetryContext telemetry)
        {
            var selectClauses = new SimpleAttributeOperandCollection
            {
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = [.. new QualifiedName[] { BrowseNames.EventId }]
                },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = ObjectTypeIds.ConditionType,
                    BrowsePath = [.. new QualifiedName[] { BrowseNames.Retain }]
                },
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.NodeId,
                    TypeDefinitionId = ObjectTypeIds.ConditionType
                }
            };

            var filter = new EventFilter
            {
                SelectClauses = selectClauses,
                WhereClause = whereClause
            };
            _ = filter.Validate(GetFilterContext(telemetry));
            return filter;
        }

        private InstanceStateSnapshot CreateSnapshot(
            BaseObjectState alarm,
            ITelemetryContext telemetry)
        {
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(GetSystemContext(telemetry), alarm);
            return snapshot;
        }

        /// <summary>
        /// Publishes the single queued event and returns the Retain field it carries.
        /// </summary>
        private static bool PublishRetain(MonitoredItem monitoredItem)
        {
            var notifications = new Queue<EventFieldList>();
            _ = monitoredItem.Publish(new OperationContext(monitoredItem), notifications, 10);

            Assert.That(notifications, Has.Count.EqualTo(1));
            EventFieldList fields = notifications.Dequeue();
            Assert.That(
                fields.EventFields[kRetainFieldIndex].Value,
                Is.TypeOf<bool>(),
                "Retain is a selected field");
            return (bool)fields.EventFields[kRetainFieldIndex].Value;
        }

        private static bool ReadRetain(
            InstanceStateSnapshot target,
            IFilterContext filterContext)
        {
            object value = target.GetAttributeValue(
                filterContext,
                ObjectTypeIds.ConditionType,
                [.. new QualifiedName[] { BrowseNames.Retain }],
                Attributes.Value,
                NumericRange.Empty);
            Assert.That(value, Is.TypeOf<bool>());
            return (bool)value;
        }

        /// <summary>
        /// Evaluates the alarm's current state against the item and asserts whether it is
        /// sent. When <paramref name="expectedOverrideRetain"/> is given it also asserts
        /// whether this is the trailing event that carries the client specific
        /// Retain = false - the "Retain sent" column of Part 9 Table B.3.
        /// </summary>
        private void CanSendFilteredAlarm(
            MonitoredItem monitoredItem,
            IFilterContext context,
            EventFilter filter,
            BaseObjectState alarm,
            bool expected,
            ITelemetryContext telemetry,
            bool? expectedOverrideRetain = null)
        {
            SystemContext systemContext = GetSystemContext(telemetry);

            var eventSnapshot = new InstanceStateSnapshot();
            eventSnapshot.Initialize(systemContext, alarm);

            const BindingFlags eFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo methodInfo = typeof(MonitoredItem).GetMethod("CanSendFilteredAlarm", eFlags);
            Debug.WriteLine("Expecting " + expected.ToString());

            // the out parameter is read back from the argument array after the call.
            object[] arguments = [context, filter, eventSnapshot, false];
            object result = methodInfo.Invoke(monitoredItem, arguments);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.GetType().Name, Is.EqualTo("Boolean"));
            Assert.That((bool)result, Is.EqualTo(expected));

            if (expectedOverrideRetain.HasValue)
            {
                Assert.That(
                    (bool)arguments[3],
                    Is.EqualTo(expectedOverrideRetain.Value),
                    "Retain sent");
            }
        }

        private ExclusiveLevelAlarmState GetExclusiveLevelAlarm(
            bool addFilterRetain,
            bool filterRetainValue,
            ITelemetryContext telemetry)
        {
            var alarm = new ExclusiveLevelAlarmState(null);
            SystemContext context = GetSystemContext(telemetry);
            alarm.Create(
                context,
                new NodeId(12345, 1),
                new QualifiedName("AnyAlarm", 1),
                new LocalizedText(string.Empty, "AnyAlarm"),
                true);

            alarm.EventType.Value = ObjectTypeIds.ExclusiveLevelAlarmType;

            if (addFilterRetain)
            {
                alarm.SupportsFilteredRetain = new PropertyState<bool>(alarm)
                {
                    Value = filterRetainValue
                };
            }

            return alarm;
        }

        private static SimpleAttributeOperandCollection GetSelectFields()
        {
            var simpleAttributeOperands = new SimpleAttributeOperandCollection();

            int eventIndexCounter = 0;
            var desiredEventFields = new Dictionary<int, QualifiedNameCollection>
            {
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.EventId }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.EventType }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Time }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.ActiveState }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Message }] },
                {
                    eventIndexCounter++,
                    [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.CurrentState }] },
                {
                    eventIndexCounter++,
                    [.. new QualifiedName[] {
                        BrowseNames.LimitState,
                        BrowseNames.CurrentState,
                        BrowseNames.Id }]
                },
                {
                    eventIndexCounter++,
                    [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.LastTransition }]
                }
            };

            foreach (QualifiedNameCollection desiredEventField in desiredEventFields.Values)
            {
                simpleAttributeOperands.Add(
                    new SimpleAttributeOperand
                    {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = desiredEventField
                    });
            }

            // ConditionId
            simpleAttributeOperands.Add(
                new SimpleAttributeOperand
                {
                    AttributeId = Attributes.NodeId,
                    TypeDefinitionId = ObjectTypeIds.ConditionType
                });

            return simpleAttributeOperands;
        }

        private EventFilter GetHighOnlyEventFilter(bool addClauses, ITelemetryContext telemetry)
        {
            var filter = new EventFilter();
            if (addClauses)
            {
                filter.SelectClauses = GetSelectFields();
                filter.WhereClause = GetHighOnlyFilter();
            }
            _ = filter.Validate(GetFilterContext(telemetry));
            return filter;
        }

        private static ContentFilter GetHighOnlyFilter()
        {
            var whereClause = new ContentFilter();

            var eventLevel = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = ObjectTypeIds.ExclusiveLevelAlarmType,
                BrowsePath =
                [
                    .. new QualifiedName[] {
                        BrowseNames.LimitState,
                        BrowseNames.CurrentState,
                        BrowseNames.Id }
                ]
            };

            var desiredEventLevel = new LiteralOperand
            {
                Value = new Variant(new NodeId(Objects.ExclusiveLimitStateMachineType_High))
            };

            whereClause.Push(FilterOperator.Equals, [eventLevel, desiredEventLevel]);

            return whereClause;
        }

        private static ContentFilter GetStateFilter()
        {
            var whereClause = new ContentFilter();

            var notOutOfServiceState = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { BrowseNames.OutOfServiceState }]
            };

            var desiredOutOfServiceValue = new LiteralOperand { Value = new Variant(InService) };

            whereClause.Push(
                FilterOperator.Equals,
                [notOutOfServiceState, desiredOutOfServiceValue]);

            var notSuppressed = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { BrowseNames.SuppressedState }]
            };

            var desiredSuppressedValue = new LiteralOperand { Value = new Variant(Unsuppressed) };

            whereClause.Push(FilterOperator.Equals, [notSuppressed, desiredSuppressedValue]);

#if AddActiveState

            var activeState = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { BrowseNames.ActiveState }]
            };

            var activeValue = new LiteralOperand { Value = new Variant(Active) };

            whereClause.Push(FilterOperator.Equals, [activeState, activeValue]);

            whereClause.Push(FilterOperator.And, [new ElementOperand(1), new ElementOperand(2)]);

#endif

            whereClause.Push(FilterOperator.And, [new ElementOperand(0), new ElementOperand(1)]);

            return whereClause;
        }

        private SystemContext GetSystemContext(ITelemetryContext telemetry)
        {
            if (m_systemContext == null)
            {
                m_systemContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
                m_systemContext.NamespaceUris.Append(Ua.Namespaces.OpcUa);
                var typeTable = new TypeTable(m_systemContext.NamespaceUris);
                typeTable.AddSubtype(ObjectTypeIds.BaseObjectType, default);
                typeTable.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);
                typeTable.AddSubtype(ObjectTypeIds.ConditionType, ObjectTypeIds.BaseEventType);
                typeTable.AddSubtype(
                    ObjectTypeIds.AcknowledgeableConditionType,
                    ObjectTypeIds.ConditionType);
                typeTable.AddSubtype(
                    ObjectTypeIds.AlarmConditionType,
                    ObjectTypeIds.AcknowledgeableConditionType);
                typeTable.AddSubtype(
                    ObjectTypeIds.LimitAlarmType,
                    ObjectTypeIds.AlarmConditionType);
                typeTable.AddSubtype(
                    ObjectTypeIds.ExclusiveLimitAlarmType,
                    ObjectTypeIds.LimitAlarmType);
                typeTable.AddSubtype(
                    ObjectTypeIds.ExclusiveLevelAlarmType,
                    ObjectTypeIds.ExclusiveLimitAlarmType);

                m_systemContext.TypeTable = typeTable;
            }

            return m_systemContext;
        }

        private IFilterContext GetFilterContext(ITelemetryContext telemetry)
        {
            if (m_filterContext == null)
            {
                SystemContext systemContext = GetSystemContext(telemetry);
                m_filterContext = new FilterContext(
                    systemContext.NamespaceUris,
                    systemContext.TypeTable,
                    systemContext.Telemetry);
            }

            return m_filterContext;
        }

        private MonitoredItem CreateMonitoredItem(MonitoringFilter filter, ITelemetryContext telemetry)
        {
            var serverMock = new Mock<IServerInternal>();

            SystemContext systemContext = GetSystemContext(telemetry);
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(systemContext.NamespaceUris);
            serverMock.Setup(s => s.TypeTree).Returns((TypeTable)systemContext.TypeTable);
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));

            var nodeMangerMock = new Mock<INodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
                null,
                1,
                2,
                new ReadValueId(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                1000.0,
                10,
                false,
                1000);
        }
    }
}
