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
using System.Collections.Concurrent;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Generators;

namespace Generators
{
    /// <summary>
    /// The protection alarms a generating set annunciates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>GeneratorProtectionAlarmType</c> instance per protection function
    /// rather than a single instance whose <c>ProtectionFunction</c> changes: a set
    /// can trip on low oil pressure and overspeed at the same moment, and an
    /// operator needs to see both. It is also how a real control panel annunciates.
    /// </para>
    /// <para>
    /// Because <c>OffNormalAlarmType</c> takes <em>healthy</em> as the normal state,
    /// each instance carries the supervised input and its normal value so a client
    /// can find what is actually being watched.
    /// </para>
    /// </remarks>
    public partial class GeneratorNodeManager
    {
        private readonly ConcurrentDictionary<NodeId, List<ProtectionAlarm>> m_alarms = new();

        /// <summary>
        /// Creates the protection alarm nodes for one set.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <remarks>
        /// Called while the set is being materialised, before its subtree is
        /// registered, so the alarms register along with everything else. Building
        /// them later would mean registering each one separately from a synchronous
        /// context, which is how sync-over-async creeps in.
        /// </remarks>
        private void MaterialiseProtectionAlarms(GeneratorSetState set)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Generators.Namespaces.Generators);

            foreach (ProtectionDefinition definition in GeneratorProtections.Definitions)
            {
                Create(set, ns, definition);
            }
        }

        /// <summary>
        /// Binds the trip conditions once the simulation for a set exists.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="simulation">The set's simulation.</param>
        private void AttachProtectionAlarms(GeneratorSetState set, GeneratorSimulation simulation)
        {
            var alarms = new List<ProtectionAlarm>();
            foreach (ProtectionDefinition definition in GeneratorProtections.Definitions)
            {
                if (FindPredefinedNode<GeneratorProtectionAlarmState>(
                        AlarmNodeId(set, definition.Name)) is not { } alarm)
                {
                    continue;
                }
                alarms.Add(new ProtectionAlarm(
                    alarm, () => definition.IsTripped(simulation), definition.IsShutdown));
            }
            m_alarms[set.NodeId] = alarms;
        }

        /// <summary>
        /// Returns the NodeId an alarm is minted with.
        /// </summary>
        /// <param name="set">Owning generator set.</param>
        /// <param name="name">Alarm browse name.</param>
        /// <returns>The alarm's NodeId.</returns>
        private NodeId AlarmNodeId(GeneratorSetState set, string name)
        {
            return new NodeId(
                $"{set.NodeId.IdentifierAsString}_{name}", InstanceNamespaceIndex);
        }

        /// <summary>
        /// Creates and configures one protection alarm node.
        /// </summary>
        /// <param name="set">Owning generator set.</param>
        /// <param name="ns">Generators namespace index.</param>
        /// <param name="definition">What the alarm reports.</param>
        private void Create(GeneratorSetState set, ushort ns, ProtectionDefinition definition)
        {
            GeneratorProtectionAlarmState alarm = SystemContext
                .CreateInstanceOfGeneratorProtectionAlarmType(
                    set, new QualifiedName(definition.Name, ns));
            alarm.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            alarm.SymbolicName = definition.Name;

            GeneratorProtections.ApplyDefinition(alarm, SystemContext, definition);

            set.AddChild(alarm);

            alarm.CreateOrReplaceEventType(SystemContext, null!).Value = NodeId.Create(
                Opc.Ua.Generators.ObjectTypes.GeneratorProtectionAlarmType,
                Opc.Ua.Generators.Namespaces.Generators,
                Server.NamespaceUris);
            alarm.CreateOrReplaceSourceNode(SystemContext, null!).Value = set.NodeId;
            alarm.CreateOrReplaceSourceName(SystemContext, null!).Value =
                set.BrowseName.Name ?? string.Empty;
            alarm.CreateOrReplaceConditionName(SystemContext, null!).Value = definition.Name;
            alarm.CreateOrReplaceSeverity(SystemContext, null!).Value = definition.Severity;
            alarm.CreateOrReplaceRetain(SystemContext, null!).Value = false;
            alarm.CreateOrReplaceTime(SystemContext, null!).Value = DateTime.UtcNow;
            alarm.CreateOrReplaceReceiveTime(SystemContext, null!).Value = DateTime.UtcNow;
            alarm.CreateOrReplaceMessage(SystemContext, null!).Value =
                new LocalizedText("Protection healthy.");

            // OffNormalAlarmType treats healthy as normal, so record what is being
            // supervised rather than leaving a client to guess.
            NodeId input = definition.Name switch
            {
                "LowOilPressureAlarm" => set.LubricationSystem?.OilPressure?.NodeId ?? NodeId.Null,
                "HighCoolantTemperatureAlarm" => set.Engine?.CoolantTemperature?.NodeId ?? NodeId.Null,
                "OverspeedAlarm" => set.Engine?.Speed?.NodeId ?? NodeId.Null,
                _ => set.Alternator?.LoadPercent?.NodeId ?? NodeId.Null,
            };
            if (!input.IsNull)
            {
                alarm.CreateOrReplaceInputNode(SystemContext, null!).Value = input;
            }

            alarm.SetEnableState(SystemContext, true);
            alarm.SetActiveState(SystemContext, false);
        }

        /// <summary>
        /// Evaluates every set's protections and reports state changes as events.
        /// </summary>
        /// <remarks>
        /// The trip conditions come from the simulation, which already applies
        /// hysteresis, so an alarm latches and clears cleanly instead of chattering
        /// on the threshold.
        /// </remarks>
        private void EvaluateProtections()
        {
            foreach (KeyValuePair<NodeId, List<ProtectionAlarm>> entry in m_alarms)
            {
                if (!m_simulations.TryGetValue(entry.Key, out GeneratorSimulation? simulation))
                {
                    continue;
                }
                foreach (ProtectionAlarm alarm in entry.Value)
                {
                    bool active = alarm.IsTripped();
                    if (active == alarm.WasActive)
                    {
                        continue;
                    }
                    alarm.WasActive = active;

                    alarm.State.SetActiveState(SystemContext, active);
                    alarm.State.CreateOrReplaceRetain(SystemContext, null!).Value = active;
                    alarm.State.CreateOrReplaceTime(SystemContext, null!).Value = DateTime.UtcNow;
                    alarm.State.CreateOrReplaceReceiveTime(SystemContext, null!).Value = DateTime.UtcNow;
                    alarm.State.CreateOrReplaceMessage(SystemContext, null!).Value = new LocalizedText(
                        active ? "Protection tripped." : "Protection cleared.");
                    alarm.State.ClearChangeMasks(SystemContext, true);

                    var snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(SystemContext, alarm.State);
                    alarm.State.ReportEvent(SystemContext, snapshot);

                    // A shutdown-class protection stops the machine; a warning does
                    // not. Reporting a trip without stopping the set would publish a
                    // generator that is on fire and still loaded.
                    if (active && alarm.IsShutdown)
                    {
                        simulation.Trip();
                    }
                }
            }
        }

        /// <summary>
        /// Clears the latched protections of one set.
        /// </summary>
        /// <param name="set">The generator set.</param>
        private void ClearLatchedAlarms(GeneratorSetState set)
        {
            if (!m_alarms.TryGetValue(set.NodeId, out List<ProtectionAlarm>? alarms))
            {
                return;
            }
            foreach (ProtectionAlarm alarm in alarms)
            {
                alarm.WasActive = false;
                alarm.State.SetActiveState(SystemContext, false);
                alarm.State.CreateOrReplaceRetain(SystemContext, null!).Value = false;
                alarm.State.ClearChangeMasks(SystemContext, true);
            }
        }
    }

    /// <summary>
    /// The protections every generating set annunciates.
    /// </summary>
    /// <remarks>
    /// A table rather than a switch, so a client-visible property and the condition
    /// that drives it are declared next to each other and cannot drift apart. Held
    /// to the datasheet's trip points by <c>GeneratorProtectionTests</c>.
    /// </remarks>
    internal static class GeneratorProtections
    {
        /// <summary>
        /// Gets the protections, in the order a set publishes them.
        /// </summary>
        public static ArrayOf<ProtectionDefinition> Definitions { get; } = new ProtectionDefinition[]
        {
            new ProtectionDefinition("LowOilPressureAlarm",
                GeneratorProtectionFunctionEnum.LowOilPressure,
                "LubricationSystem", IsShutdown: true, Severity: 900,
                s => s.LowOilPressure),
            new ProtectionDefinition("HighCoolantTemperatureAlarm",
                GeneratorProtectionFunctionEnum.HighCoolantTemperature,
                "CoolingSystem", IsShutdown: true, Severity: 900,
                s => s.CoolantOverTemperature),
            new ProtectionDefinition("OverspeedAlarm",
                GeneratorProtectionFunctionEnum.Overspeed,
                "Engine", IsShutdown: true, Severity: 1000,
                s => s.SpeedRpm > GeneratorDatasheet.TripPoints.OverspeedRpm),
            new ProtectionDefinition("OverloadAlarm",
                GeneratorProtectionFunctionEnum.Overload,
                "Alternator", IsShutdown: false, Severity: 600,
                s => s.LoadFraction > GeneratorDatasheet.TripPoints.OverloadFraction),
        };

        /// <summary>
        /// Writes what a protection reports onto one alarm node.
        /// </summary>
        /// <param name="alarm">The alarm node to configure.</param>
        /// <param name="context">The system context.</param>
        /// <param name="definition">What the alarm reports.</param>
        /// <remarks>
        /// <c>ProtectionFunction</c> is mandatory on the type, so the generated
        /// factory has already materialised it. <c>IsShutdown</c> and
        /// <c>SubsystemName</c> are <b>optional</b> and have not been.
        /// <c>CreateOrReplace</c> alone is not enough for those: it produces a child
        /// with no <c>ReferenceTypeId</c> and no NodeId, so there is no reference for
        /// a browse to follow and the alarm reports its trip without saying whether
        /// the trip stops the machine or which subsystem to go to. Opting in with
        /// <c>AddXxx</c> first is what gives the child its <c>HasProperty</c>
        /// reference. Every write looks correct in the source; the loss is only
        /// visible from a client.
        /// </remarks>
        public static void ApplyDefinition(
            GeneratorProtectionAlarmState alarm,
            ISystemContext context,
            ProtectionDefinition definition)
        {
            alarm.AddIsShutdown(context);
            alarm.AddSubsystemName(context);

            alarm.CreateOrReplaceProtectionFunction(context, null!).Value = definition.Function;
            alarm.CreateOrReplaceIsShutdown(context, null!).Value = definition.IsShutdown;
            alarm.CreateOrReplaceSubsystemName(context, null!).Value = definition.Subsystem;
        }
    }

    /// <summary>
    /// One protection a generating set annunciates.
    /// </summary>
    /// <param name="Name">Browse name of the alarm.</param>
    /// <param name="Function">Which protection this alarm reports.</param>
    /// <param name="Subsystem">Subsystem the fault belongs to.</param>
    /// <param name="IsShutdown">Whether tripping shuts the set down.</param>
    /// <param name="Severity">Event severity.</param>
    /// <param name="IsTripped">Evaluates the trip condition against a simulation.</param>
    internal sealed record ProtectionDefinition(
        string Name,
        GeneratorProtectionFunctionEnum Function,
        string Subsystem,
        bool IsShutdown,
        ushort Severity,
        Func<GeneratorSimulation, bool> IsTripped);

    /// <summary>
    /// One protection alarm and the condition that trips it.
    /// </summary>
    /// <param name="state">The alarm node.</param>
    /// <param name="isTripped">Evaluates the trip condition.</param>
    /// <param name="isShutdown">Whether tripping shuts the set down.</param>
    internal sealed class ProtectionAlarm(
        GeneratorProtectionAlarmState state,
        Func<bool> isTripped,
        bool isShutdown)
    {
        /// <summary>
        /// Gets the alarm node.
        /// </summary>
        public GeneratorProtectionAlarmState State { get; } = state;

        /// <summary>
        /// Gets whether tripping this protection shuts the set down.
        /// </summary>
        public bool IsShutdown { get; } = isShutdown;

        /// <summary>
        /// Gets or sets the last reported active state, so only changes raise events.
        /// </summary>
        public bool WasActive { get; set; }

        /// <summary>
        /// Evaluates the trip condition.
        /// </summary>
        /// <returns><see langword="true"/> when the protection is tripped.</returns>
        public bool IsTripped()
        {
            return isTripped();
        }
    }
}
