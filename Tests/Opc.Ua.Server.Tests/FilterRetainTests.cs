
// Archie - December 17 2024
// Requires discussion with Part 9 Editor
#define AddActiveState

using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test FilterRetain on MonitoredItem
    /// </summary>
    [TestFixture, Category("MonitoredItem")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class FilterRetainTests
    {
        private SystemContext m_systemContext;
        private FilterContext m_filterContext;

        internal static readonly LocalizedText InService = new("en", "In Service");
        internal static readonly LocalizedText OutOfService = new("en", "Out of Service");
        internal static readonly LocalizedText Unsuppressed = new("en-US", "Unsuppressed");

        internal static readonly LocalizedText Active = new("en-US", "Active");

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNotFilterTarget(bool pass)
        {
            SystemContext systemContext = GetSystemContext();
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(addFilterRetain: false);
            LimitAlarmStates desiredState = LimitAlarmStates.Inactive;
            if (pass)
            {
                desiredState = LimitAlarmStates.High;
            }
            alarm.SetLimitState(systemContext, desiredState);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);
            CanSendFilteredAlarm(monitoredItem, GetFilterContext(), filter, alarm, pass);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNonConditionState(bool pass)
        {
            SystemContext systemContext = GetSystemContext();
            var alarm = new DeviceFailureEventState(null);
            alarm.Create(
               systemContext,
               new NodeId(12345, 1),
               new QualifiedName("AnyAlarm", 1),
               new LocalizedText("", "AnyAlarm"),
               true);

            alarm.EventType.Value = ObjectTypeIds.DeviceFailureEventType;

            FilterContext context = GetFilterContext();

            EventFilter filter = GetHighOnlyEventFilter(addClauses: !pass);

            MonitoredItem monitoredItem = CreateMonitoredItem(filter);
            CanSendFilteredAlarm(monitoredItem, context, filter, alarm, pass);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestNonEvent(bool pass)
        {
            var certificateType = new ApplicationCertificateState(null);

            FilterContext context = GetFilterContext();

            EventFilter filter = GetHighOnlyEventFilter(addClauses: !pass);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);
            CanSendFilteredAlarm(monitoredItem, context, filter, certificateType, pass);
        }

        [Test]
        [TestCase(false, Description = "Set SupportsFilteredRetain False")]
        [TestCase(true, Description = "Set SupportsFilteredRetain True")]
        public void TestFilteredRetainExists(bool supportsFilteredRetain)
        {
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true, supportsFilteredRetain);

            alarm.SetLimitState(GetSystemContext(), LimitAlarmStates.Inactive);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);

            CanSendFilteredAlarm(monitoredItem, GetFilterContext(), filter, alarm, expected: false);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestCanSendMultiple(bool supportsFilteredRetain)
        {
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true, supportsFilteredRetain);

            FilterContext filterContext = GetFilterContext();

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);

            SystemContext systemContext = GetSystemContext();
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true);

            alarm.SetLimitState(systemContext, LimitAlarmStates.HighHigh);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: supportsFilteredRetain);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestCanSendOnceSimple(bool supportsFilteredRetain)
        {
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true, supportsFilteredRetain);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);

            SystemContext systemContext = GetSystemContext();
            FilterContext filterContext = GetFilterContext();
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: supportsFilteredRetain);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void TestSendMultiple(bool supportsFilteredRetain)
        {
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true, supportsFilteredRetain);

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true);
            MonitoredItem monitoredItem = CreateMonitoredItem(filter);

            SystemContext systemContext = GetSystemContext();
            FilterContext filterContext = GetFilterContext();
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true);

            alarm.SetLimitState(systemContext, LimitAlarmStates.HighHigh);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: supportsFilteredRetain);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: supportsFilteredRetain);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Low);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: false);
        }

        [Test]
        [TestCase(false, Description = "Should not pass filter")]
        [TestCase(true, Description = "Should pass filter")]
        public void SpecB14(bool supportsFilteredRetain)
        {
            // https://reference.opcfoundation.org/Core/Part9/v105/docs/B.1.4

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true, supportsFilteredRetain);

            SystemContext systemContext = GetSystemContext();

            alarm.SetSuppressedState(systemContext, suppressed: false);
            alarm.OutOfServiceState.Value = InService;

            FilterContext filterContext = GetFilterContext();
            var filter = new EventFilter {
                SelectClauses = GetSelectFields(),
                WhereClause = GetStateFilter()
            };
            filter.Validate(filterContext);

            MonitoredItem monitoredItem = CreateMonitoredItem(filter);

            // 16 States in Table B.3

            // 1 Alarm Goes Active
            Debug.WriteLine("// 1 Alarm Goes Active");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            bool expected = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 2 Placed Out of Service
            Debug.WriteLine("// 2 Placed Out of Service");
            alarm.OutOfServiceState.Value = OutOfService;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 3 Alarm Suppressed; No event since OutOfService
            Debug.WriteLine("// 3 Alarm Suppressed; No event since OutOfService");
            alarm.SetSuppressedState(systemContext, suppressed: true);
            expected = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 4 Alarm goes inactive; No event since OutOfService
            Debug.WriteLine("// 4 Alarm goes inactive; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 5 Alarm not Suppressed; No event since not active
            Debug.WriteLine("// 5 Alarm not Suppressed; No event since not active");
            alarm.SetSuppressedState(systemContext, suppressed: false);
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 6 Alarm goes active; No event since OutOfService
            Debug.WriteLine("// 6 Alarm goes active; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 7 Alarm no longer OutOfService; Event generated
            Debug.WriteLine("// 7 Alarm no longer OutOfService; Event generated");
            alarm.OutOfServiceState.Value = InService;
            expected = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 8 Alarm goes inactive
            Debug.WriteLine("// 8 Alarm goes inactive");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 9 Alarm Suppressed; No event since not active
            Debug.WriteLine("// 9 Alarm Suppressed; No event since not active");
            alarm.SetSuppressedState(systemContext, suppressed: true);
            expected = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 10 Alarm goes active; No event since Suppressed
            Debug.WriteLine("// 10 Alarm goes active; No event since Suppressed");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 11 Alarm goes inactive; No event since Suppressed
            Debug.WriteLine("// 11 Alarm goes inactive; No event since Suppressed");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 12 Alarm no longer Suppressed
            Debug.WriteLine("// 12 Alarm no longer Suppressed");
            alarm.SetSuppressedState(systemContext, suppressed: false);
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 13 Placed OutOfService
            Debug.WriteLine("// 13 Placed OutOfService");
            alarm.OutOfServiceState.Value = OutOfService;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 14 Alarm goes active; No event since OutOfService
            Debug.WriteLine("// 14 Alarm goes active; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 15 Alarm goes inactive; No event since OutOfService
            Debug.WriteLine("// 15 Alarm goes inactive; No event since OutOfService");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);

            // 16 Alarm no longer OutOfService
            Debug.WriteLine("// 16 Alarm no longer OutOfService");
            alarm.OutOfServiceState.Value = InService;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected);
        }

        private void CanSendFilteredAlarm(
            MonitoredItem monitoredItem,
            FilterContext context,
            EventFilter filter,
            BaseObjectState alarm,
            bool expected)
        {
            SystemContext systemContext = GetSystemContext();

            var eventSnapshot = new InstanceStateSnapshot();
            eventSnapshot.Initialize(systemContext, alarm);

            const BindingFlags eFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo methodInfo = typeof(MonitoredItem).GetMethod("CanSendFilteredAlarm", eFlags);
            Debug.WriteLine("Expecting " + expected.ToString());
            object result = methodInfo.Invoke(monitoredItem, [context, filter, eventSnapshot]);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.GetType().Name, Is.EqualTo("Boolean"));
            Assert.That((bool)result, Is.EqualTo(expected));
        }

        private ExclusiveLevelAlarmState GetExclusiveLevelAlarm(
            bool addFilterRetain, bool filterRetainValue = false)
        {
            var alarm = new ExclusiveLevelAlarmState(null);
            alarm.Create(
               GetSystemContext(),
               new NodeId(12345, 1),
               new QualifiedName("AnyAlarm", 1),
               new LocalizedText("", "AnyAlarm"),
               true);

            alarm.EventType.Value = ObjectTypeIds.ExclusiveLevelAlarmType;

            if (addFilterRetain)
            {
                alarm.SupportsFilteredRetain = new PropertyState<bool>(alarm) {
                    Value = filterRetainValue
                };
            }

            return alarm;
        }

        private static SimpleAttributeOperandCollection GetSelectFields()
        {
            var simpleAttributeOperands = new SimpleAttributeOperandCollection();

            int eventIndexCounter = 0;
            var desiredEventFields = new Dictionary<int, QualifiedNameCollection> {
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.EventId }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.EventType }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Time }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.ActiveState }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Message }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.CurrentState }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.CurrentState, BrowseNames.Id }] },
                { eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.LastTransition }] }
            };

            foreach (QualifiedNameCollection desiredEventField in desiredEventFields.Values)
            {
                simpleAttributeOperands.Add(new SimpleAttributeOperand() {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = desiredEventField
                });
            }

            // ConditionId
            simpleAttributeOperands.Add(new SimpleAttributeOperand() {
                AttributeId = Attributes.NodeId,
                TypeDefinitionId = ObjectTypeIds.ConditionType
            });

            return simpleAttributeOperands;
        }

        private EventFilter GetHighOnlyEventFilter(bool addClauses)
        {
            var filter = new EventFilter();
            if (addClauses)
            {
                filter.SelectClauses = GetSelectFields();
                filter.WhereClause = GetHighOnlyFilter();
            }
            filter.Validate(GetFilterContext());
            return filter;
        }

        private static ContentFilter GetHighOnlyFilter()
        {
            var whereClause = new ContentFilter();

            var eventLevel = new SimpleAttributeOperand() {
                AttributeId = Attributes.Value,
                TypeDefinitionId = ObjectTypeIds.ExclusiveLevelAlarmType,
                BrowsePath = [.. new QualifiedName[] {
                    BrowseNames.LimitState,
                    BrowseNames.CurrentState,
                    BrowseNames.Id }]
            };

            var desiredEventLevel = new LiteralOperand {
                Value = new Variant(new NodeId(Objects.ExclusiveLimitStateMachineType_High))
            };

            whereClause.Push(FilterOperator.Equals, [eventLevel, desiredEventLevel]);

            return whereClause;
        }

        private static ContentFilter GetStateFilter()
        {
            var whereClause = new ContentFilter();

            var notOutOfServiceState = new SimpleAttributeOperand() {
                AttributeId = Attributes.Value,
                TypeDefinitionId = null,
                BrowsePath = [.. new QualifiedName[] {
                    BrowseNames.OutOfServiceState }]
            };

            var desiredOutOfServiceValue = new LiteralOperand {
                Value = new Variant(InService)
            };

            whereClause.Push(FilterOperator.Equals, [notOutOfServiceState, desiredOutOfServiceValue]);

            var notSuppressed = new SimpleAttributeOperand() {
                AttributeId = Attributes.Value,
                TypeDefinitionId = null,
                BrowsePath = [.. new QualifiedName[] {
                    BrowseNames.SuppressedState }]
            };

            var desiredSuppressedValue = new LiteralOperand {
                Value = new Variant(Unsuppressed)
            };

            whereClause.Push(FilterOperator.Equals, [notSuppressed, desiredSuppressedValue]);

#if AddActiveState

            var activeState = new SimpleAttributeOperand() {
                AttributeId = Attributes.Value,
                TypeDefinitionId = null,
                BrowsePath = [.. new QualifiedName[] {
                    BrowseNames.ActiveState }]
            };

            var activeValue = new LiteralOperand {
                Value = new Variant(Active)
            };

            whereClause.Push(FilterOperator.Equals, [
                activeState,
                activeValue ]);

            whereClause.Push(FilterOperator.And, [
                new ElementOperand(1),
                new ElementOperand(2) ]);

#endif

            whereClause.Push(FilterOperator.And, [
                new ElementOperand(0),
                new ElementOperand(1) ]);

            return whereClause;
        }

        private SystemContext GetSystemContext()
        {
            if (m_systemContext == null)
            {
                m_systemContext = new SystemContext {
                    NamespaceUris = new NamespaceTable()
                };
                m_systemContext.NamespaceUris.Append(Namespaces.OpcUa);
                var typeTable = new TypeTable(m_systemContext.NamespaceUris);
                typeTable.AddSubtype(ObjectTypeIds.BaseObjectType, null);
                typeTable.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);
                typeTable.AddSubtype(ObjectTypeIds.ConditionType, ObjectTypeIds.BaseEventType);
                typeTable.AddSubtype(ObjectTypeIds.AcknowledgeableConditionType, ObjectTypeIds.ConditionType);
                typeTable.AddSubtype(ObjectTypeIds.AlarmConditionType, ObjectTypeIds.AcknowledgeableConditionType);
                typeTable.AddSubtype(ObjectTypeIds.LimitAlarmType, ObjectTypeIds.AlarmConditionType);
                typeTable.AddSubtype(ObjectTypeIds.ExclusiveLimitAlarmType, ObjectTypeIds.LimitAlarmType);
                typeTable.AddSubtype(ObjectTypeIds.ExclusiveLevelAlarmType, ObjectTypeIds.ExclusiveLimitAlarmType);

                m_systemContext.TypeTable = typeTable;
            }

            return m_systemContext;
        }

        private FilterContext GetFilterContext()
        {
            if (m_filterContext == null)
            {
                SystemContext systemContext = GetSystemContext();
                m_filterContext = new FilterContext(
                    systemContext.NamespaceUris,
                    systemContext.TypeTable);
            }

            return m_filterContext;
        }

        private MonitoredItem CreateMonitoredItem(MonitoringFilter filter)
        {
            var serverMock = new Mock<IServerInternal>();

            SystemContext systemContext = GetSystemContext();
            serverMock.Setup(s => s.NamespaceUris).Returns(systemContext.NamespaceUris);
            serverMock.Setup(s => s.TypeTree).Returns((TypeTable)systemContext.TypeTable);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(new MonitoredItemQueueFactory());

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
                1000
                );
        }
    }
}
