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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using PadimBrowseNames = Opc.Ua.PADIM.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Keeps the fluent chain in the test readable when one step is optional.
    /// </summary>
    internal static class ProcessValueBuilderTestExtensions
    {
        public static IProcessValueBuilder WithDeviationAlarmWhen(
            this IProcessValueBuilder builder,
            bool enabled)
        {
            return enabled ? builder.WithDeviationAlarm() : builder;
        }
    }

    /// <summary>
    /// Exercises the OPC 40001-2 runtime behaviour: the alarms have to follow
    /// the value, and the zero-point adjustment has to report an event a
    /// client can filter for.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryProcessValueTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineryServerFixture(
                MachineryParts.BuildingBlocks | MachineryParts.ProcessValues);
            await m_fixture.StartAsync();
            m_context = m_fixture.CreateBuildContext();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync();
            }
        }

        [Test]
        public async Task TheLimitAlarmMirrorsTheSignalLimitsAndFollowsTheValueAsync()
        {
            // No deviation alarm here: both alarms report on the same
            // notifier, so counting events is only unambiguous with one.
            IProcessValueHandle handle = await BuildAsync(withDeviationAlarm: false);
            var alarm = (ExclusiveLimitAlarmState)handle.State.LimitAlarm!;

            Assert.That(alarm.HighHighLimit!.Value, Is.EqualTo(80.0));
            Assert.That(alarm.HighLimit!.Value, Is.EqualTo(65.0));
            Assert.That(alarm.LowLimit!.Value, Is.EqualTo(15.0));
            Assert.That(alarm.LowLowLimit!.Value, Is.EqualTo(5.0));
            Assert.That(
                alarm.EnabledState!.Id!.Value,
                Is.True,
                "A condition that is never enabled can never report.");
            Assert.That(alarm.InputNode!.Value, Is.EqualTo(handle.Signal.NodeId));
            Assert.That(alarm.SourceNode!.Value, Is.EqualTo(handle.State.NodeId));
            Assert.That(alarm.ActiveState!.Id!.Value, Is.False);

            List<IFilterTarget> events = CaptureEvents(handle.State);

            await handle.SetValueAsync(70.0);
            Assert.That(alarm.ActiveState.Id.Value, Is.True);
            Assert.That(
                alarm.LimitState!.CurrentState!.Value.Text,
                Is.EqualTo("High"));
            Assert.That(alarm.Retain!.Value, Is.True);
            Assert.That(events, Has.Count.EqualTo(1));

            await handle.SetValueAsync(85.0);
            Assert.That(
                alarm.LimitState.CurrentState.Value.Text,
                Is.EqualTo("HighHigh"));
            Assert.That(events, Has.Count.EqualTo(2));

            // The same reading twice is not a transition, so no second event.
            await handle.SetValueAsync(86.0);
            Assert.That(events, Has.Count.EqualTo(2));

            await handle.SetValueAsync(45.0);
            Assert.That(alarm.ActiveState.Id.Value, Is.False);
            Assert.That(alarm.Retain.Value, Is.False);
            Assert.That(events, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task TheDeviationAlarmMeasuresAgainstTheSetpointAsync()
        {
            IProcessValueHandle handle = await BuildAsync();
            var alarm = (ExclusiveDeviationAlarmState)handle.State.DeviationAlarm!;

            Assert.That(
                alarm.SetpointNode!.Value,
                Is.EqualTo(handle.State.ProcessValueSetpoint!.NodeId),
                "OPC 40001-2 points the deviation alarm at the setpoint.");
            Assert.That(alarm.HighLimit!.Value, Is.EqualTo(10.0));

            // The deviation limits are written on the setpoint itself, which
            // is what the "Deviation Base" conformance unit asks for.
            Assert.That(
                handle.State.ProcessValueSetpoint.HighDeviation,
                Is.Not.Null);

            // 45 + 12 exceeds the high deviation of 10 but stays below the
            // absolute high limit of 65, so only the deviation alarm fires.
            await handle.SetValueAsync(57.0);
            Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
            Assert.That(
                ((ExclusiveLimitAlarmState)handle.State.LimitAlarm!).ActiveState!.Id!.Value,
                Is.False);

            // Moving the setpoint up removes the deviation without the value
            // changing at all.
            await handle.SetSetpointAsync(55.0);
            Assert.That(alarm.ActiveState.Id.Value, Is.False);
        }

        [Test]
        public async Task ThePercentageValueTracksTheInstrumentRangeAsync()
        {
            IProcessValueHandle handle = await BuildAsync();

            // Range is -20 … 120, so 50 sits at exactly half.
            await handle.SetValueAsync(50.0);
            Assert.That(handle.Signal.PercentageValue, Is.Not.Null);
            Assert.That(
                handle.Signal.PercentageValue!.WrappedValue.TryGetValue(out double percentage),
                Is.True);
            Assert.That(percentage, Is.EqualTo(50.0).Within(0.001));
        }

        [Test]
        public async Task ZeroPointAdjustmentReportsAConcreteEventTypeAsync()
        {
            IProcessValueHandle handle = await BuildAsync();
            MethodState method = handle.State.ZeroPointAdjustment!;
            List<IFilterTarget> events = CaptureEvents(handle.State);

            var outputs = new List<Variant>();
            var argumentErrors = new List<ServiceResult>();
            ServiceResult status = await method.CallAsync(
                m_fixture!.Manager.SystemContext,
                handle.State.NodeId,
                [],
                argumentErrors,
                outputs);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(m_adjusted, Is.True, "The handler has to run.");
            Assert.That(
                events,
                Has.Count.EqualTo(1),
                "OPC 40001-2 requires the event on every call.");

            var reported = (ZeroPointAdjustmentEventState)events[0];
            NodeId eventTypeId = reported.EventType!.Value;
            Assert.That(
                eventTypeId,
                Is.EqualTo(reported.TypeDefinitionId),
                "The EventType field is what a client filters on.");

            // OPC UA forbids an instance of an abstract type, so the reported
            // type must be a concrete subtype minted by the server.
            NodeState? eventType = m_fixture.Manager.FindPredefinedNode(eventTypeId);
            Assert.That(eventType, Is.InstanceOf<BaseObjectTypeState>());
            var concrete = (BaseObjectTypeState)eventType!;
            Assert.That(concrete.IsAbstract, Is.False);
            Assert.That(
                concrete.SuperTypeId,
                Is.EqualTo(NodeId.Create(
                    Opc.Ua.Machinery.ProcessValues.ObjectTypes.ZeroPointAdjustmentEventType,
                    Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                    m_fixture.Manager.Server.NamespaceUris)));
            Assert.That(
                concrete.NodeId.NamespaceIndex,
                Is.EqualTo(m_fixture.Manager.MachineryInstanceNamespaceIndex),
                "The subtype belongs to the server, not to the companion model.");
            Assert.That(
                reported.ZeroPointAdjustmentResult!.Value.Code,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task SimulationReplacesTheReportedReadingAsync()
        {
            IProcessValueHandle? handle = null;
            await m_context!
                .AddMachine(new QualifiedName("Press-Simulated"))
                .WithProcessValue(
                    new QualifiedName("OilTemperature"),
                    processValue => processValue
                        .WithEngineeringUnits(
                            DegreeCelsius(),
                            new Opc.Ua.Range { Low = -20, High = 120 })
                        .WithPercentageValue()
                        .WithSimulation(simulationValue: 100)
                        .WithValue(45)
                        .Bind(out handle))
                .BuildAsync();

            Assert.That(handle, Is.Not.Null);
            ProcessValueVariableState signal = handle!.Signal;

            // OPC 30081 declares all three on AnalogSignalVariableType.
            Assert.That(signal.ActualValue, Is.Not.Null);
            Assert.That(signal.SimulationValue, Is.Not.Null);
            Assert.That(signal.SimulationState, Is.Not.Null);
            Assert.That(
                signal.SimulationState!.Value,
                Is.False,
                "A server does not come up simulating unless asked to.");
            Assert.That(signal.WrappedValue.TryGetValue(out double reported), Is.True);
            Assert.That(reported, Is.EqualTo(45.0));

            // While simulation is active the signal reports the simulated
            // reading and ActualValue keeps carrying the measured one; that
            // difference is the whole point of the unit.
            await handle.SetSimulationAsync(true);
            Assert.That(signal.WrappedValue.TryGetValue(out reported), Is.True);
            Assert.That(reported, Is.EqualTo(100.0));
            Assert.That(
                signal.ActualValue!.WrappedValue.TryGetValue(out double actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(45.0));

            // Everything derived follows the reported value, because that is
            // what the plant is acting on. Range -20 … 120 spans 140, so 100
            // sits at 85.71 %.
            Assert.That(
                signal.PercentageValue!.WrappedValue.TryGetValue(out double percentage),
                Is.True);
            Assert.That(percentage, Is.EqualTo(85.714285).Within(0.001));

            // A new measurement while simulating moves ActualValue only.
            await handle.SetValueAsync(60.0);
            Assert.That(signal.WrappedValue.TryGetValue(out reported), Is.True);
            Assert.That(reported, Is.EqualTo(100.0));
            Assert.That(signal.ActualValue.WrappedValue.TryGetValue(out actual), Is.True);
            Assert.That(actual, Is.EqualTo(60.0));

            // Switching simulation off hands the signal back to the plant.
            await handle.SetSimulationAsync(false);
            Assert.That(signal.WrappedValue.TryGetValue(out reported), Is.True);
            Assert.That(reported, Is.EqualTo(60.0));
        }

        [Test]
        public async Task SimulationIsRefusedWhenTheMembersAreAbsentAsync()
        {
            IProcessValueHandle handle = await BuildAsync();

            Assert.That(
                async () => await handle.SetSimulationAsync(true),
                Throws.TypeOf<ServiceResultException>()
                    .With.Message.Contains("no simulation members"));
        }

        [Test]
        public async Task TheSignalTagOverrideReplacesTheDefaultAsync()
        {
            IProcessValueHandle? handle = null;
            await m_context!
                .AddMachine(new QualifiedName("Press-Tagged"))
                .WithProcessValue(
                    new QualifiedName("OilTemperature"),
                    processValue => processValue
                        .WithEngineeringUnits(
                            DegreeCelsius(),
                            new Opc.Ua.Range { Low = -20, High = 120 })
                        .WithSignalTag("PRESS-1/TT-4711")
                        .WithValue(45)
                        .Bind(out handle))
                .BuildAsync();

            // The builder defaults SignalTag to the process value's browse
            // name; OPC 30081 wants it unique across the plant, so an explicit
            // tag has to win over that default.
            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.State.SignalTag, Is.Not.Null);
            Assert.That(
                handle.State.SignalTag!.Value,
                Is.EqualTo("PRESS-1/TT-4711"));
        }

        [Test]
        public void AnEmptySignalTagIsRefused()
        {
            Assert.That(
                async () => await m_context!
                    .AddMachine(new QualifiedName("Press-Untagged"))
                    .WithProcessValue(
                        new QualifiedName("OilTemperature"),
                        processValue => processValue.WithSignalTag(string.Empty))
                    .BuildAsync(),
                Throws.ArgumentException,
                "OPC 30081 requires a non-empty SignalTag.");
        }

        [Test]
        public async Task TheSignalKeepsThePadimBrowseNameAndTheNarrowerTypeAsync()
        {
            IProcessValueHandle handle = await BuildAsync();

            // OPC 30081 re-declares EngineeringUnits on AnalogSignalVariableType
            // but leaves the browse name in namespace 0, where OPC 10000-8 put
            // it and where every Data Access client browses for it.
            Assert.That(handle.Signal.EngineeringUnits, Is.Not.Null);
            Assert.That(
                handle.Signal.EngineeringUnits!.BrowseName,
                Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits)));
            Assert.That(handle.Signal.EURange, Is.Not.Null);
            Assert.That(
                handle.Signal.EURange!.BrowseName,
                Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.EURange)));

            // A child whose ReferenceTypeId stayed null is in the node tree
            // but produces no reference in a filtered Browse, so no client
            // ever sees it. Both members the generator drops are pinned here.
            Assert.That(
                handle.Signal.EngineeringUnits.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasProperty));
            Assert.That(handle.State.SignalTag, Is.Not.Null);
            Assert.That(
                handle.State.SignalTag!.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasProperty));
            Assert.That(
                handle.State.SignalTag.Value,
                Is.EqualTo("OilTemperature"),
                "SignalTag is mandatory on PADIM's AnalogSignalType.");

            ushort padimNamespaceIndex = (ushort)m_fixture!.Manager.Server.NamespaceUris
                .GetIndex(Opc.Ua.PADIM.Namespaces.PADIM);
            Assert.That(
                handle.Signal.BrowseName,
                Is.EqualTo(new QualifiedName(
                    PadimBrowseNames.AnalogSignal,
                    padimNamespaceIndex)));
            Assert.That(
                handle.Signal.TypeDefinitionId,
                Is.EqualTo(NodeId.Create(
                    Opc.Ua.Machinery.ProcessValues.VariableTypes.ProcessValueVariableType,
                    Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                    m_fixture.Manager.Server.NamespaceUris)));
        }

        [Test]
        public async Task TheAdvertisedProcessValueFacetsMatchWhatWasBuiltAsync()
        {
            await BuildAsync();

            string[] profiles = [.. m_fixture!.Manager.ServerProfiles];
            Assert.That(
                profiles,
                Contains.Item(
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/Base/"));
            Assert.That(
                profiles,
                Contains.Item(
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/LimitsAlarm/"));
            Assert.That(
                profiles,
                Contains.Item(
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
                    "DeviationAlarm/"));
            Assert.That(
                profiles,
                Contains.Item(
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/Server/" +
                    "ZeroPointAdjustmentEvents/"));

            QualifiedName[] units = [.. m_fixture.Manager.ConformanceUnits];
            Assert.That(
                units,
                Contains.Item(
                    new QualifiedName("Machinery Process Values Analog Object Instances")));
            Assert.That(
                units,
                Contains.Item(
                    new QualifiedName(
                        "Machinery Process Values ZeroPointAdjustment Events")));
            Assert.That(
                units,
                Contains.Item(
                    new QualifiedName("Machinery Process Values Percentage Value")));
        }

        private async ValueTask<IProcessValueHandle> BuildAsync(
            bool withDeviationAlarm = true)
        {
            IProcessValueHandle? handle = null;
            await m_context!
                .AddMachine(new QualifiedName("Press-1"))
                .WithProcessValue(
                    new QualifiedName("OilTemperature"),
                    processValue => processValue
                        .WithEngineeringUnits(
                            DegreeCelsius(),
                            new Opc.Ua.Range { Low = -20, High = 120 })
                        .WithLimits(lowLow: 5, low: 15, high: 65, highHigh: 80)
                        .WithSetpoint(45)
                        .WithDeviationLimits(
                            lowLow: -20,
                            low: -10,
                            high: 10,
                            highHigh: 20,
                            autoAdjustment: false,
                            sensitivity: 5)
                        .WithPercentageValue()
                        .WithStatus()
                        .WithLimitAlarm()
                        .WithDeviationAlarmWhen(withDeviationAlarm)
                        .WithZeroPointAdjustment((_, _) =>
                        {
                            m_adjusted = true;
                            return new ValueTask<StatusCode>(StatusCodes.Good);
                        })
                        .WithValue(45)
                        .Bind(out handle))
                .BuildAsync();
            Assert.That(handle, Is.Not.Null);
            return handle!;
        }

        /// <summary>
        /// Intercepts the root-notifier sink the build installed so the test
        /// sees exactly the events a subscribed client would.
        /// </summary>
        private static List<IFilterTarget> CaptureEvents(NodeState notifier)
        {
            var events = new List<IFilterTarget>();
            NodeStateReportEventAsyncHandler? inner = notifier.OnReportEventAsync;
            notifier.OnReportEventAsync = (context, node, e, ct) =>
            {
                events.Add(e);
                return inner?.Invoke(context, node, e, ct) ?? default;
            };
            return events;
        }

        private static EUInformation DegreeCelsius()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4408652,
                DisplayName = new LocalizedText("°C"),
                Description = new LocalizedText("degree Celsius")
            };
        }

        private MachineryServerFixture? m_fixture;
        private IMachineryBuildContext? m_context;
        private bool m_adjusted;
    }
}
