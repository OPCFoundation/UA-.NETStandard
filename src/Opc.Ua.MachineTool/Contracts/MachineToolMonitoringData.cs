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

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Explicitly selected optional MachineTool surfaces; no selection implies the mandatory model only.
    /// </summary>
    [Flags]
    public enum MachineToolFeatures
    {
        /// <summary>
        /// Publishes only mandatory MachineTool observations.
        /// </summary>
        None = 0,

        /// <summary>
        /// Publishes the provider's ordinary and combined channels.
        /// </summary>
        Channels = 1,

        /// <summary>
        /// Publishes software-component identities.
        /// </summary>
        SoftwareIdentification = 2,

        /// <summary>
        /// Requires the additional Machinery machine-nameplate properties.
        /// </summary>
        AdditionalIdentification = 4,

        /// <summary>
        /// Publishes spindles, lasers and EDM generators.
        /// </summary>
        WorkingUnits = 8,

        /// <summary>
        /// Publishes segmented IA stacklights.
        /// </summary>
        Stacklights = 16,

        /// <summary>
        /// Retains the deprecated integral-hours power-on counter.
        /// </summary>
        LegacyPowerOnDuration = 32,

        /// <summary>
        /// Publishes the DI/Machinery duration counter in milliseconds.
        /// </summary>
        OperationCounters = 64,

        /// <summary>
        /// Publishes the lifetime produced-parts counter.
        /// </summary>
        PartsProducedInLifetime = 128,

        /// <summary>
        /// Publishes nested Machinery item and operation-mode observations.
        /// </summary>
        MachineryState = 256,

        /// <summary>
        /// Publishes operator versus machine-builder obligation.
        /// </summary>
        Obligation = 512,

        /// <summary>
        /// Publishes the maintenance substate, invalid when its parent mode is not Maintenance.
        /// </summary>
        MaintenanceMode = 1024,

        /// <summary>
        /// Publishes the retained production jobs, program transitions and LastTransition information.
        /// </summary>
        Production = 2048,

        /// <summary>
        /// Allows insertion and removal in the retained production plan.
        /// </summary>
        DynamicProductionPlan = 4096,

        /// <summary>
        /// Publishes retained production part sets, individual parts and coherent quality counters.
        /// </summary>
        ProductionParts = 8192,

        /// <summary>
        /// Publishes the retained UInt32 lifetime production statistics.
        /// </summary>
        ProductionStatistics = 16384,

        /// <summary>
        /// Publishes known production interruption reasons using retained standard conditions.
        /// </summary>
        ProductionInterruptions = 32768,

        /// <summary>
        /// Publishes the complete provider-reported inventory of tools and multitools.
        /// </summary>
        Tools = 65536,

        /// <summary>
        /// Allows dynamic tool inventories with NodeVersion, model-change events and owned-resource draining.
        /// </summary>
        DynamicTools = 131072,

        /// <summary>
        /// Requires at least one explicit tool-life measurement on every single tool.
        /// </summary>
        ToolLife = 262144,

        /// <summary>
        /// Publishes explicitly selected, provider-reported prognosis kinds.
        /// </summary>
        Prognoses = 524288,

        /// <summary>
        /// Allows changing prognosis inventories with model events and final-observation lifetime draining.
        /// </summary>
        DynamicPrognoses = 1048576,

        /// <summary>
        /// Selects all currently supported observation features.
        /// </summary>
        All = Channels | SoftwareIdentification | AdditionalIdentification | WorkingUnits |
            Stacklights | LegacyPowerOnDuration | OperationCounters | PartsProducedInLifetime |
            MachineryState | Obligation | MaintenanceMode | Production | DynamicProductionPlan |
            ProductionParts | ProductionStatistics | ProductionInterruptions | Tools | DynamicTools | ToolLife |
            Prognoses | DynamicPrognoses
    }

    /// <summary>
    /// A channel observation; combined channels name their constituent ordinary channels.
    /// </summary>
    public sealed record MachineToolChannel
    {
        /// <summary>
        /// Gets the stable application name of the channel.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the channel's reported execution state.
        /// </summary>
        public ChannelState State { get; init; }

        /// <summary>
        /// Gets the channel's reported operating mode.
        /// </summary>
        public ChannelMode Mode { get; init; }

        /// <summary>
        /// Gets the HMI's feed override as a percentage.
        /// </summary>
        public double FeedOverride { get; init; }

        /// <summary>
        /// Gets the optional rapid-traverse override as a percentage.
        /// </summary>
        public double? RapidOverride { get; init; }

        /// <summary>
        /// Gets the controller's maximum supported override percentage.
        /// </summary>
        public double MaximumOverride { get; init; } = 200;

        /// <summary>
        /// Gets the optional program modifiers.
        /// </summary>
        public MachineToolChannelModifiers? Modifiers { get; init; }

        /// <summary>
        /// Gets the names to aggregate. An empty array denotes an ordinary channel.
        /// </summary>
        public ArrayOf<string> CombinedChannels { get; init; }

        /// <summary>
        /// Gets the quality and acquisition time of the observation.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// Reported program modifiers, never commands to disable or bypass machine functions.
    /// </summary>
    /// <param name="DryRun">Whether the controller reports a dry run.</param>
    /// <param name="OptionalStop">Whether the controller reports optional stop.</param>
    /// <param name="SingleStep">Whether the controller reports single-step execution.</param>
    /// <param name="BlockSkip">Whether the controller reports block skipping.</param>
    /// <param name="TestMode">Whether the controller reports its no-motion test mode.</param>
    public readonly record struct MachineToolChannelModifiers(
        bool DryRun, bool OptionalStop, bool SingleStep, bool BlockSkip = false, bool TestMode = false);

    /// <summary>
    /// An installed software component, separate from the machine's overall software revision.
    /// </summary>
    /// <param name="Identifier">The component identifier.</param>
    /// <param name="Revision">The installed component revision.</param>
    /// <param name="Manufacturer">The optional component manufacturer.</param>
    public sealed record MachineToolSoftware(string Identifier, string Revision, LocalizedText Manufacturer = default);

    /// <summary>
    /// A rotary working-unit observation.
    /// </summary>
    /// <param name="Name">The stable spindle name.</param>
    /// <param name="IsRotating">Whether a valid commanded spindle rotation is reported.</param>
    /// <param name="Sample">Acquisition quality and time.</param>
    /// <param name="Override">Optional spindle override percentage.</param>
    /// <param name="IsUsedAsAxis">Whether spindle rotation and override are inapplicable.</param>
    /// <param name="MaximumOverride">The controller's upper override range.</param>
    public sealed record MachineToolSpindle(
        string Name, bool IsRotating, MachineToolSampleInfo Sample,
        double? Override = null, bool? IsUsedAsAxis = null, double MaximumOverride = 200);

    /// <summary>
    /// A laser working-unit observation, not a laser-control interface.
    /// </summary>
    /// <param name="Name">The stable laser name.</param>
    /// <param name="ControllerIsOn">Whether the laser controller is running.</param>
    /// <param name="State">The observed laser state.</param>
    /// <param name="Sample">Acquisition quality and time.</param>
    public sealed record MachineToolLaser(
        string Name, bool ControllerIsOn, LaserState State, MachineToolSampleInfo Sample);

    /// <summary>
    /// An EDM spark-generator observation, not a generator-control interface.
    /// </summary>
    /// <param name="Name">The stable generator name.</param>
    /// <param name="IsOn">Whether the generator reports its active operating conditions.</param>
    /// <param name="State">The observed generator state.</param>
    /// <param name="Sample">Acquisition quality and time.</param>
    public sealed record MachineToolEdmGenerator(
        string Name, bool IsOn, EDMGeneratorState State, MachineToolSampleInfo Sample);

    /// <summary>
    /// A reported light segment; positions are assigned in the order supplied by the provider.
    /// </summary>
    /// <param name="IsOn">Whether the segment is lit.</param>
    /// <param name="Color">The reported IA signal color.</param>
    /// <param name="Mode">The reported light mode.</param>
    /// <param name="Sample">Acquisition quality and time.</param>
    public sealed record MachineToolLight(
        bool IsOn, IA.SignalColor Color, IA.SignalModeLight Mode, MachineToolSampleInfo Sample);

    /// <summary>
    /// A segmented stacklight; level and running modes are not permitted by OPC 40501-1.
    /// </summary>
    /// <param name="Name">The stable application name of the stacklight.</param>
    /// <param name="Lights">The ordered physical light segments.</param>
    public sealed record MachineToolStacklight(string Name, ArrayOf<MachineToolLight> Lights);

    /// <summary>
    /// Responsibility for the current machine activity.
    /// </summary>
    /// <param name="EndUser">Whether the end user is responsible.</param>
    /// <param name="MachineBuilder">Whether the machine builder is responsible.</param>
    public readonly record struct MachineToolObligation(bool EndUser, bool MachineBuilder);

    /// <summary>
    /// OPC 40001-1 item-state observations.
    /// </summary>
    public enum MachineToolItemState
    {
        /// <summary>
        /// No item-state information is available.
        /// </summary>
        NotAvailable,

        /// <summary>
        /// The item is out of service.
        /// </summary>
        OutOfService,

        /// <summary>
        /// The item is not executing.
        /// </summary>
        NotExecuting,

        /// <summary>
        /// The item is executing.
        /// </summary>
        Executing
    }

    /// <summary>
    /// OPC 40001-1 operation-mode observations.
    /// </summary>
    public enum MachineToolMachineryMode
    {
        /// <summary>
        /// No operation mode is available.
        /// </summary>
        None,

        /// <summary>
        /// Maintenance is being performed.
        /// </summary>
        Maintenance,

        /// <summary>
        /// Setup is being performed.
        /// </summary>
        Setup,

        /// <summary>
        /// Value-adding processing is being performed.
        /// </summary>
        Processing
    }

    /// <summary>
    /// The released MachineTool maintenance substates.
    /// </summary>
    public enum MachineToolMaintenanceMode
    {
        /// <summary>
        /// Service work is performed.
        /// </summary>
        Service,

        /// <summary>
        /// Inspection work is performed.
        /// </summary>
        Inspection,

        /// <summary>
        /// Repair work is performed.
        /// </summary>
        Repair,

        /// <summary>
        /// Upgrade work is performed.
        /// </summary>
        Upgrade,

        /// <summary>
        /// Other maintenance work is performed.
        /// </summary>
        Other
    }
}
