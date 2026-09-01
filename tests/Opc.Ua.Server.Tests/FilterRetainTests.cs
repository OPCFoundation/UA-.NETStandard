// Archie - December 17 2024
// Requires discussion with Part 9 Editor
#define AddActiveState

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private MonitoredItemQueueFactory m_queueFactory;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_queueFactory?.Dispose();
            m_queueFactory = null;
        }

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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
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

            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);
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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

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
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

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

            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            // 16 States in Table B.3

            // 1 Alarm Goes Active
            Debug.WriteLine("// 1 Alarm Goes Active");
            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            bool expected = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

            // 2 Placed Out of Service
            Debug.WriteLine("// 2 Placed Out of Service");
            alarm.OutOfServiceState.Value = OutOfService;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

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

            // 8 Alarm goes inactive
            Debug.WriteLine("// 8 Alarm goes inactive");
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            if (!supportsFilteredRetain)
            {
                expected = false;
            }
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected, telemetry);

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
        /// ConditionRefresh puts the ConditionState itself into the event list rather than
        /// an InstanceStateSnapshot, so filtered retain has to recognise that shape too.
        /// </summary>
        [Test]
        public void ConditionStateTargetParticipatesInFilteredRetain()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, alarm),
                Is.True,
                "the alarm passes the where clause");

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, alarm),
                Is.True,
                "leaving filter scope produces the trailing event");

            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, alarm),
                Is.False,
                "the trailing event is sent once");
        }

        /// <summary>
        /// A branch shares its parent's NodeId, so tracking by NodeId alone would let one of
        /// them consume the entry the other relies on.
        /// </summary>
        [Test]
        public void BranchesDoNotShareFilteredRetainState()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;

            var branch = (ExclusiveLevelAlarmState)alarm.CreateBranch(systemContext, new NodeId(1, 1));
            Assert.That(branch, Is.Not.Null);
            Assert.That(branch.NodeId, Is.EqualTo(alarm.NodeId), "a branch keeps the parent NodeId");

            // both are in scope; each has to claim its own entry.
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, alarm),
                Is.True);
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, branch),
                Is.True);

            // the branch leaves scope and consumes its own entry only.
            branch.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, branch),
                Is.True,
                "the branch gets its trailing event");
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, branch),
                Is.False);

            // the parent is untouched by that and still gets its own trailing event.
            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(filterContext, filter, alarm),
                Is.True,
                "the parent entry survived the branch transition");
        }

        /// <summary>
        /// SupportsFilteredRetain is not an instance child, so nothing that copies a
        /// condition by walking its children carries it. CreateBranch copies it explicitly.
        /// </summary>
        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void BranchInheritsSupportsFilteredRetain(bool supportsFilteredRetain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: supportsFilteredRetain,
                telemetry: telemetry);

            var branch = (ConditionState)alarm.CreateBranch(
                GetSystemContext(telemetry),
                new NodeId(1, 1));

            Assert.That(branch, Is.Not.Null);
            Assert.That(branch.SupportsFilteredRetain, Is.Not.Null);
            Assert.That(branch.SupportsFilteredRetain.Value, Is.EqualTo(supportsFilteredRetain));
            Assert.That(
                branch.SupportsFilteredRetain,
                Is.Not.SameAs(alarm.SupportsFilteredRetain),
                "the branch owns its copy");
        }

        /// <summary>
        /// A condition that never opted in must not get a property out of thin air.
        /// </summary>
        [Test]
        public void BranchWithoutSupportsFilteredRetainStaysUnset()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: false,
                filterRetainValue: false,
                telemetry: telemetry);

            var branch = (ConditionState)alarm.CreateBranch(
                GetSystemContext(telemetry),
                new NodeId(1, 1));

            Assert.That(branch, Is.Not.Null);
            Assert.That(branch.SupportsFilteredRetain, Is.Null);
        }

        /// <summary>
        /// Part 9 provides SupportsFilteredRetain on the ConditionType only, and the standard
        /// nodeset declares no modelling rule for it, so condition instances must not expose
        /// it as an address space child.
        /// </summary>
        [Test]
        public void SupportsFilteredRetainIsNotAnInstanceChild()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            SystemContext systemContext = GetSystemContext(telemetry);
            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            var children = new List<BaseInstanceState>();
            alarm.GetChildren(systemContext, children);

            Assert.That(
                children.Any(child =>
                    child.BrowseName.Name == BrowseNames.SupportsFilteredRetain),
                Is.False);
            Assert.That(
                alarm.FindChild(
                    systemContext,
                    QualifiedName.From(BrowseNames.SupportsFilteredRetain)),
                Is.Null);
        }

        /// <summary>
        /// Every entry means "this condition passed the previous where clause", so keeping
        /// them across a where clause change would produce a trailing event against a filter
        /// that never saw the condition pass.
        /// </summary>
        [Test]
        public void ModifyAttributesDiscardsFilteredRetainWhenWhereClauseChanges()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            // a where clause the condition has never been evaluated against.
            var replacement = new EventFilter
            {
                SelectClauses = GetSelectFields(),
                WhereClause = GetStateFilter()
            };
            _ = replacement.Validate(filterContext);

            monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                3,
                replacement,
                replacement,
                null,
                1000.0,
                10,
                false);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                replacement,
                alarm,
                expected: false,
                telemetry);
        }

        /// <summary>
        /// A modification that leaves the where clause alone - a new queue size, say - has to
        /// keep the bookkeeping, otherwise the trailing event is lost.
        /// </summary>
        [Test]
        public void ModifyAttributesKeepsFilteredRetainWhenWhereClauseIsUnchanged()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.High);
            alarm.Retain.Value = true;
            CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

            EventFilter sameFilter = GetHighOnlyEventFilter(addClauses: true, telemetry);

            monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                3,
                sameFilter,
                sameFilter,
                null,
                1000.0,
                20,
                false);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(
                monitoredItem,
                filterContext,
                sameFilter,
                alarm,
                expected: true,
                telemetry);
        }

        /// <summary>
        /// A durable subscription has to carry the bookkeeping across a restart, otherwise
        /// the first transition out of filter scope after the restart is dropped.
        /// </summary>
        [Test]
        public void FilteredRetainSurvivesADurableRoundTrip()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ExclusiveLevelAlarmState alarm = GetExclusiveLevelAlarm(
                addFilterRetain: true,
                filterRetainValue: true,
                telemetry: telemetry);

            SystemContext systemContext = GetSystemContext(telemetry);
            IFilterContext filterContext = GetFilterContext(telemetry);
            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);

            IStoredMonitoredItem stored;
            using (TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry))
            {
                alarm.SetLimitState(systemContext, LimitAlarmStates.High);
                alarm.Retain.Value = true;
                CanSendFilteredAlarm(monitoredItem, filterContext, filter, alarm, expected: true, telemetry);

                stored = monitoredItem.ToStorableMonitoredItem();
            }

            Assert.That(stored.FilteredRetainConditionIds, Is.Not.Null);
            Assert.That(stored.FilteredRetainConditionIds, Has.Count.EqualTo(1));

            using TestableMonitoredItem restored = RestoreMonitoredItem(stored, telemetry);

            alarm.SetLimitState(systemContext, LimitAlarmStates.Inactive);
            alarm.Retain.Value = false;
            CanSendFilteredAlarm(restored, filterContext, filter, alarm, expected: true, telemetry);
            CanSendFilteredAlarm(restored, filterContext, filter, alarm, expected: false, telemetry);
        }

        /// <summary>
        /// An item with nothing to track stores nothing.
        /// </summary>
        [Test]
        public void NothingIsStoredWhenNoConditionIsTracked()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            EventFilter filter = GetHighOnlyEventFilter(addClauses: true, telemetry);
            using TestableMonitoredItem monitoredItem = CreateMonitoredItem(filter, telemetry);

            Assert.That(
                monitoredItem.ToStorableMonitoredItem().FilteredRetainConditionIds,
                Is.Null);
        }

        private void CanSendFilteredAlarm(
            TestableMonitoredItem monitoredItem,
            IFilterContext context,
            EventFilter filter,
            BaseObjectState alarm,
            bool expected,
            ITelemetryContext telemetry)
        {
            SystemContext systemContext = GetSystemContext(telemetry);

            var eventSnapshot = new InstanceStateSnapshot();
            eventSnapshot.Initialize(systemContext, alarm);

            Debug.WriteLine("Expecting " + expected.ToString());

            Assert.That(
                monitoredItem.CanSendFilteredAlarmForTest(context, filter, eventSnapshot),
                Is.EqualTo(expected));
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
            alarm.AddOutOfServiceState(context)
                .AddSuppressedState(context)
                .AddSilenceState(context)
                .AddShelvingState(context);
            alarm.AddSeverityLowLow(context);
            if (addFilterRetain)
            {
                alarm.SupportsFilteredRetain = new PropertyState<bool>.Implementation<VariantBuilder>(alarm)
                {
                    Value = filterRetainValue
                };
            }

            return alarm;
        }

        private static ArrayOf<SimpleAttributeOperand> GetSelectFields()
        {
            int eventIndexCounter = 0;
            var desiredEventFields = new Dictionary<int, ArrayOf<QualifiedName>>
            {
                { eventIndexCounter++, [QualifiedName.From(BrowseNames.EventId) ] },
                { eventIndexCounter++, [QualifiedName.From(BrowseNames.EventType) ] },
                { eventIndexCounter++, [QualifiedName.From(BrowseNames.Time) ] },
                { eventIndexCounter++, [QualifiedName.From(BrowseNames.ActiveState) ] },
                { eventIndexCounter++, [QualifiedName.From(BrowseNames.Message) ] },
                { eventIndexCounter++, [
                    QualifiedName.From(BrowseNames.LimitState),
                    QualifiedName.From(BrowseNames.CurrentState)
                ]
                },
                { eventIndexCounter++, [
                    QualifiedName.From(BrowseNames.LimitState),
                    QualifiedName.From(BrowseNames.CurrentState),
                    QualifiedName.From(BrowseNames.Id)
                ]
                },
                { eventIndexCounter++, [
                    QualifiedName.From(BrowseNames.LimitState),
                    QualifiedName.From(BrowseNames.LastTransition)
                ]
                }
            };

            var simpleAttributeOperands = new List<SimpleAttributeOperand>();
            foreach (ArrayOf<QualifiedName> desiredEventField in desiredEventFields.Values)
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
                    .. new QualifiedName[]
                    {
                        QualifiedName.From(BrowseNames.LimitState),
                        QualifiedName.From(BrowseNames.CurrentState),
                        QualifiedName.From(BrowseNames.Id)
                    }
                ]
            };

            var desiredEventLevel = new LiteralOperand
            {
                Value = new Variant(new NodeId(Objects.ExclusiveLimitStateMachineType_High))
            };

            whereClause.Push(
                FilterOperator.Equals,
                Variant.FromStructure(eventLevel),
                Variant.FromStructure(desiredEventLevel));

            return whereClause;
        }

        private static ContentFilter GetStateFilter()
        {
            var whereClause = new ContentFilter();

            var notOutOfServiceState = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { QualifiedName.From(BrowseNames.OutOfServiceState) }]
            };

            var desiredOutOfServiceValue = new LiteralOperand { Value = new Variant(InService) };

            whereClause.Push(
                FilterOperator.Equals,
                Variant.FromStructure(notOutOfServiceState),
                Variant.FromStructure(desiredOutOfServiceValue));

            var notSuppressed = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { QualifiedName.From(BrowseNames.SuppressedState) }]
            };

            var desiredSuppressedValue = new LiteralOperand { Value = new Variant(Unsuppressed) };

            whereClause.Push(
                FilterOperator.Equals,
                Variant.FromStructure(notSuppressed),
                Variant.FromStructure(desiredSuppressedValue));

#if AddActiveState
            var activeState = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = default,
                BrowsePath = [.. new QualifiedName[] { QualifiedName.From(BrowseNames.ActiveState) }]
            };

            var activeValue = new LiteralOperand { Value = new Variant(Active) };

            whereClause.Push(
                FilterOperator.Equals,
                Variant.FromStructure(activeState),
                Variant.FromStructure(activeValue));

            whereClause.Push(
                FilterOperator.And,
                Variant.FromStructure(new ElementOperand(1)),
                Variant.FromStructure(new ElementOperand(2)));

#endif
            whereClause.Push(
                FilterOperator.And,
                Variant.FromStructure(new ElementOperand(0)),
                Variant.FromStructure(new ElementOperand(1)));

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

        private TestableMonitoredItem CreateMonitoredItem(
            MonitoringFilter filter,
            ITelemetryContext telemetry)
        {
            return new TestableMonitoredItem(
                CreateServer(telemetry),
                new Mock<IAsyncNodeManager>().Object,
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

        private TestableMonitoredItem RestoreMonitoredItem(
            IStoredMonitoredItem storedMonitoredItem,
            ITelemetryContext telemetry)
        {
            return new TestableMonitoredItem(
                CreateServer(telemetry),
                new Mock<IAsyncNodeManager>().Object,
                null,
                storedMonitoredItem);
        }

        private IServerInternal CreateServer(ITelemetryContext telemetry)
        {
            var serverMock = new Mock<IServerInternal>();

            SystemContext systemContext = GetSystemContext(telemetry);
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(systemContext.NamespaceUris);
            serverMock.Setup(s => s.TypeTree).Returns((TypeTable)systemContext.TypeTable);

            // the factory has to outlive every item the fixture builds, so it is owned by
            // the fixture rather than by the call that hands it to a monitored item.
            m_queueFactory ??= new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);

            return serverMock.Object;
        }

        /// <summary>
        /// Exposes the protected filtered retain evaluation so the suite does not have to
        /// reach for it by reflection, where a signature change would compile cleanly and
        /// only fail at run time.
        /// </summary>
        private sealed class TestableMonitoredItem : MonitoredItem
        {
            public TestableMonitoredItem(
                IServerInternal server,
                IAsyncNodeManager nodeManager,
                object managerHandle,
                uint subscriptionId,
                uint id,
                ReadValueId itemToMonitor,
                DiagnosticsMasks diagnosticsMasks,
                TimestampsToReturn timestampsToReturn,
                MonitoringMode monitoringMode,
                uint clientHandle,
                MonitoringFilter originalFilter,
                MonitoringFilter filterToUse,
                Range range,
                double samplingInterval,
                uint queueSize,
                bool discardOldest,
                double sourceSamplingInterval)
                : base(
                    server,
                    nodeManager,
                    managerHandle,
                    subscriptionId,
                    id,
                    itemToMonitor,
                    diagnosticsMasks,
                    timestampsToReturn,
                    monitoringMode,
                    clientHandle,
                    originalFilter,
                    filterToUse,
                    range,
                    samplingInterval,
                    queueSize,
                    discardOldest,
                    sourceSamplingInterval)
            {
            }

            public TestableMonitoredItem(
                IServerInternal server,
                IAsyncNodeManager nodeManager,
                object managerHandle,
                IStoredMonitoredItem storedMonitoredItem)
                : base(server, nodeManager, managerHandle, storedMonitoredItem)
            {
            }

            public bool CanSendFilteredAlarmForTest(
                IFilterContext context,
                EventFilter filter,
                IFilterTarget instance)
            {
                return CanSendFilteredAlarm(context, filter, instance);
            }
        }
    }
}
