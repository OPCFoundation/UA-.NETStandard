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
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Generators;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;

namespace Generators
{
    /// <summary>
    /// The live bindings that drive one generator set's twin.
    /// </summary>
    public partial class GeneratorNodeManager
    {
        /// <summary>
        /// Stable binding-definition identifiers, one per declared binding.
        /// </summary>
        /// <remarks>
        /// A BindingDefinitionId identifies the binding <em>declaration</em>, not an
        /// instance of it: the effective runtime identity is the pair (represented
        /// object, BindingDefinitionId). Every set therefore shares this table,
        /// while the source NodeIds and target prim paths are per instance.
        /// </remarks>
        private static readonly Dictionary<string, Guid> s_bindingIds = new(StringComparer.Ordinal)
        {
            ["BayLayout"] = new Guid("8f2a1c40-0001-4a3b-9c11-6d5e2f7a9b01"),
            ["CoolingFan"] = new Guid("8f2a1c40-0002-4a3b-9c11-6d5e2f7a9b02"),
            ["LoadGauge"] = new Guid("8f2a1c40-0003-4a3b-9c11-6d5e2f7a9b03"),
            ["CoolantGauge"] = new Guid("8f2a1c40-0004-4a3b-9c11-6d5e2f7a9b04"),
            ["ExhaustHeat"] = new Guid("8f2a1c40-0005-4a3b-9c11-6d5e2f7a9b05"),
            ["RadiatorHeat"] = new Guid("8f2a1c40-0006-4a3b-9c11-6d5e2f7a9b06"),
            ["FuelLevel"] = new Guid("8f2a1c40-0007-4a3b-9c11-6d5e2f7a9b07"),
            ["ProtectionRing"] = new Guid("8f2a1c40-0008-4a3b-9c11-6d5e2f7a9b08"),
            ["OverheatHalo"] = new Guid("8f2a1c40-0009-4a3b-9c11-6d5e2f7a9b09"),
            ["OilHalo"] = new Guid("8f2a1c40-000a-4a3b-9c11-6d5e2f7a9b0a"),
            ["RunLamp"] = new Guid("8f2a1c40-000b-4a3b-9c11-6d5e2f7a9b0b"),
            ["FrequencyReadout"] = new Guid("8f2a1c40-000c-4a3b-9c11-6d5e2f7a9b0c"),
            ["PowerReadout"] = new Guid("8f2a1c40-000d-4a3b-9c11-6d5e2f7a9b0d"),
            ["EngineHoursReadout"] = new Guid("8f2a1c40-000e-4a3b-9c11-6d5e2f7a9b0e"),
            ["LoadReadout"] = new Guid("8f2a1c40-000f-4a3b-9c11-6d5e2f7a9b0f"),
            ["OperatingStateReadout"] = new Guid("8f2a1c40-0010-4a3b-9c11-6d5e2f7a9b10"),
            ["ManifoldLeftHeat"] = new Guid("8f2a1c40-0011-4a3b-9c11-6d5e2f7a9b11"),
            ["ManifoldRightHeat"] = new Guid("8f2a1c40-0012-4a3b-9c11-6d5e2f7a9b12"),
            ["TurboLeftSpin"] = new Guid("8f2a1c40-0013-4a3b-9c11-6d5e2f7a9b13"),
            ["TurboRightSpin"] = new Guid("8f2a1c40-0014-4a3b-9c11-6d5e2f7a9b14"),
            ["AlternatorHeat"] = new Guid("8f2a1c40-0015-4a3b-9c11-6d5e2f7a9b15"),
        };

        /// <summary>
        /// Declares every live binding for one set.
        /// </summary>
        /// <param name="rep">The set's representation.</param>
        /// <param name="ns">OpenUSD namespace index.</param>
        /// <param name="twin">The set's twin.</param>
        private void CreateBindings(OpenUsdRepresentationState rep, ushort ns, GeneratorTwin twin)
        {
            GeneratorSetState set = (GeneratorSetState)rep.Parent!;
            EngineState engine = set.Engine!;
            AlternatorState alternator = set.Alternator!;
            string prim = twin.PrimPath;

            // Bay layout. The connector folds a Translation target into a single
            // xformOp:transform matrix, so the root prim declares that op - naming
            // xformOp:translate there would make USD discard every value and stack
            // all sets on the origin.
            Bind(rep, ns, "BayLayout", twin.BayPosition!.NodeId,
                prim, "xformOp:translate", "double3",
                OpenUsdRenderTargetKindEnum.Translation, 1.0);

            // A real machine's most visible motion is the cooling fan.
            Bind(rep, ns, "CoolingFan", twin.FanAngle!.NodeId,
                prim + "/Radiator/Fan", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0);

            Bind(rep, ns, "LoadGauge", twin.LoadNeedle!.NodeId,
                prim + "/ControlPanel/LoadGauge/Needle", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0);

            Bind(rep, ns, "CoolantGauge", twin.TempNeedle!.NodeId,
                prim + "/ControlPanel/TempGauge/Needle", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0);

            // The DisplayColor ramp models a temperature in degrees Celsius, so both
            // colour bindings publish Celsius rather than the model's kelvin.
            Bind(rep, ns, "ExhaustHeat", twin.ExhaustCelsius!.NodeId,
                prim + "/Exhaust/Stack", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);

            Bind(rep, ns, "RadiatorHeat", twin.RadiatorCelsius!.NodeId,
                prim + "/Radiator/Core", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);

            // The exhaust manifolds are the parts of a running engine that visibly
            // glow, so the same exhaust temperature that drives the stack drives
            // them too. One source, three targets: a client that wants to know why
            // they are glowing reads the one Variable behind all of them.
            Bind(rep, ns, "ManifoldLeftHeat", twin.ExhaustCelsius!.NodeId,
                prim + "/Engine/ManifoldLeft/Log", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);

            Bind(rep, ns, "ManifoldRightHeat", twin.ExhaustCelsius!.NodeId,
                prim + "/Engine/ManifoldRight/Log", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);

            // The alternator has no moving part a viewer can see, so without a heat
            // band it is the one major assembly on the machine that never reacts to
            // anything. Driven from load rather than a winding temperature, which
            // this sample does not simulate.
            Bind(rep, ns, "AlternatorHeat", alternator.LoadPercent!.NodeId,
                prim + "/Alternator/HeatBand", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);

            // Turbochargers turn with the engine. They share the fan's display
            // angle: both are "is this machine turning?", and a second integrator
            // would only drift away from the first.
            Bind(rep, ns, "TurboLeftSpin", twin.FanAngle!.NodeId,
                prim + "/Engine/TurboLeft", "xformOp:rotateX", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0);

            Bind(rep, ns, "TurboRightSpin", twin.FanAngle!.NodeId,
                prim + "/Engine/TurboRight", "xformOp:rotateX", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0);

            Bind(rep, ns, "FuelLevel", twin.FuelSurface!.NodeId,
                prim + "/FuelTank/Surface", "xformOp:translate", "double3",
                OpenUsdRenderTargetKindEnum.Translation, 1.0);

            // A red ring around the machine reads from any camera angle, which a
            // small lamp does not. It aggregates: the per-protection alarms are
            // individually browsable and each carries its own ActiveState, but the
            // ring answers the only question worth asking from across a hall - is
            // this machine in trouble - rather than picking one protection to show.
            Bind(rep, ns, "ProtectionRing", twin.ProtectionTripped!.NodeId,
                prim + "/AlarmRing", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                OpenUsdAlarmAspectEnum.ActiveState);

            // Each fault is shown at the subsystem it belongs to.
            Bind(rep, ns, "OverheatHalo", twin.CoolantOverTemperature!.NodeId,
                prim + "/Engine/OverheatHalo", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                OpenUsdAlarmAspectEnum.ActiveState);

            Bind(rep, ns, "OilHalo", twin.LowOilPressure!.NodeId,
                prim + "/Engine/OilHalo", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                OpenUsdAlarmAspectEnum.ActiveState);

            Bind(rep, ns, "RunLamp", twin.Running!.NodeId,
                prim + "/ControlPanel/RunLamp", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0);

            // Readouts carry no render semantics of their own; they are attributes a
            // viewer shows on selection, which is what makes the twin inspectable
            // rather than merely decorative.
            Bind(rep, ns, "FrequencyReadout", alternator.Frequency!.NodeId,
                prim, "ua:frequencyHertz", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);

            Bind(rep, ns, "PowerReadout", alternator.TotalRealPower!.NodeId,
                prim, "ua:realPowerKilowatts", "double",
                OpenUsdRenderTargetKindEnum.Custom, 0.001);

            Bind(rep, ns, "EngineHoursReadout", engine.EngineHours!.NodeId,
                prim, "ua:engineHours", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);

            Bind(rep, ns, "LoadReadout", alternator.LoadPercent!.NodeId,
                prim, "ua:loadPercent", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);

            // The operating state is what makes an idle machine legible: without it
            // a stopped set and a faulted one look identical in the viewport.
            Bind(rep, ns, "OperatingStateReadout", twin.OperatingStateName!.NodeId,
                prim, "ua:operatingState", "string",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);
        }

        /// <summary>
        /// Declares one live binding.
        /// </summary>
        /// <param name="rep">The owning representation.</param>
        /// <param name="ns">OpenUSD namespace index.</param>
        /// <param name="name">Binding name, also its definition-id key.</param>
        /// <param name="source">Source Variable NodeId.</param>
        /// <param name="primPath">Target prim path.</param>
        /// <param name="property">Target property name.</param>
        /// <param name="usdType">Target USD type name.</param>
        /// <param name="kind">Render target kind.</param>
        /// <param name="scale">Scale applied before any offset.</param>
        /// <param name="bindingTypeId">Binding subtype to instantiate.</param>
        /// <param name="alarmAspect">Alarm aspect, for alarm bindings.</param>
        private void Bind(
            OpenUsdRepresentationState rep,
            ushort ns,
            string name,
            NodeId source,
            string primPath,
            string property,
            string usdType,
            OpenUsdRenderTargetKindEnum kind,
            double scale,
            uint bindingTypeId = Opc.Ua.OpenUsd.ObjectTypes.OpenUsdValueChangeBindingType,
            OpenUsdAlarmAspectEnum? alarmAspect = null)
        {
            rep.AddLiveBinding(
                SystemContext,
                ns,
                m_powerhouseStage!.NodeId,
                name,
                s_bindingIds[name],
                source,
                primPath,
                property,
                usdType,
                kind,
                scale,
                bindingTypeId,
                OpenUsdSignalRoleEnum.Observable,
                sourceSemanticId: null,
                alarmAspect);
        }
    }
}
