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

namespace Opc.Ua.MachineTool
{
    public sealed partial record MachineToolSnapshot
    {
        /// <summary>
        /// Gets the explicitly supplied inventory of physically accessible tools, or null when not configured.
        /// </summary>
        public MachineToolEquipmentObservation? Equipment { get; init; }
    }

    /// <summary>
    /// The complete tool inventory of one machine, including magazines to which it has automated access.
    /// </summary>
    public sealed record MachineToolEquipmentObservation
    {
        /// <summary>
        /// Gets the top-level tools and multitools. An explicitly empty inventory is valid.
        /// </summary>
        public ArrayOf<MachineToolTool> Tools { get; init; }
    }

    /// <summary>
    /// Selects the generated base type to instantiate or adopt, not a hardware command.
    /// </summary>
    public enum MachineToolToolKind
    {
        /// <summary>
        /// A ToolType or an actual generated subtype, such as a GMS sensor.
        /// </summary>
        Tool,

        /// <summary>
        /// A MultiToolType or an actual generated subtype, containing individual ToolType instances.
        /// </summary>
        MultiTool,

        /// <summary>
        /// An explicitly specified concrete companion subtype of BaseToolType.
        /// </summary>
        Companion
    }

    /// <summary>
    /// A tool slot and its equipment-reported observations. Keys are application identities, not global tool IDs.
    /// </summary>
    public sealed record MachineToolTool
    {
        /// <summary>
        /// Gets the stable, machine-unique application slot key used for its browse name.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the kind of generated tool node.
        /// </summary>
        public MachineToolToolKind Kind { get; init; }

        /// <summary>
        /// Gets the concrete generated type for a Companion entry; other kinds use their declared standard base.
        /// </summary>
        public ExpandedNodeId CompanionTypeId { get; init; }

        /// <summary>
        /// Gets the optional global identifier supplied by a superordinate tool-management system.
        /// The server never generates this identifier from the slot key or control identifiers.
        /// </summary>
        public string? Identifier { get; init; }

        /// <summary>
        /// Gets the optional operator-facing name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Gets the optional location, represented by the standard BaseObjectType with Name and PlaceNumber.
        /// </summary>
        public MachineToolLocation? Location { get; init; }

        /// <summary>
        /// Gets the mandatory ToolType observations. Multitools do not have these variables.
        /// </summary>
        public MachineToolToolControl? Control { get; init; }

        /// <summary>
        /// Gets independent tool-life measurements, never values to sum together.
        /// </summary>
        public ArrayOf<MachineToolToolLife> Life { get; init; }

        /// <summary>
        /// Gets the constituent single tools of a multitool. Nested multitools are not declared by the model.
        /// </summary>
        public ArrayOf<MachineToolTool> Tools { get; init; }

        /// <summary>
        /// Gets the quality and acquisition time of the tool's scalar identification and control observations.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// A provider-reported tool magazine or workspace and its UInt16 place number.
    /// </summary>
    /// <param name="Name">The location name, not the tool's name.</param>
    /// <param name="PlaceNumber">The original place number.</param>
    public sealed record MachineToolLocation(string Name, ushort PlaceNumber);

    /// <summary>
    /// Identification in the machine controller and its reported lock state; no field actuates equipment.
    /// </summary>
    /// <param name="ControlIdentifier1">The tool number or group number.</param>
    /// <param name="Interpretation">How the controller interprets the identifiers.</param>
    /// <param name="Locked">Whether the controller reports the tool as locked.</param>
    /// <param name="ReasonForLocking">The reported reason, or CapabilityUnavailable when unknown.</param>
    public sealed record MachineToolToolControl(
        uint ControlIdentifier1, ToolManagement Interpretation, bool Locked, ToolLocked ReasonForLocking)
    {
        /// <summary>
        /// Gets the second identifier, required for GroupBased and absent for NumberBased management.
        /// </summary>
        public uint? ControlIdentifier2 { get; init; }

        /// <summary>
        /// Gets the optional last automatic-mode use time with its own quality and acquisition time.
        /// DataValue.Null means the optional member is absent, not a successful zero date.
        /// </summary>
        public DataValue LastUsage { get; init; }

        /// <summary>
        /// Gets whether the controller plans to use this tool, or null when the optional observation is absent.
        /// </summary>
        public bool? PlannedForOperating { get; init; }
    }

    /// <summary>
    /// A Number-valued life measurement with explicit units, counting direction and independent raw samples.
    /// Limits describe equipment policy; the runtime does not manufacture tool locks or change commands.
    /// </summary>
    public sealed record MachineToolToolLife
    {
        /// <summary>
        /// Gets the measurement's browse name, unique within this tool.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets an optional loaded companion namespace for a declared entry such as GMS Qualified.
        /// Omission uses the application instance namespace.
        /// </summary>
        public string? NamespaceUri { get; init; }

        /// <summary>
        /// Gets the original scalar Number sample. Integral representations, quality and source time are preserved.
        /// </summary>
        public DataValue Value { get; init; }

        /// <summary>
        /// Gets the kind of measured life property.
        /// </summary>
        public ToolLifeIndication Indication { get; init; }

        /// <summary>
        /// Gets the explicit engineering unit required by the selected indication.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// Gets whether use increases rather than decreases the measured value.
        /// </summary>
        public bool IsCountingUp { get; init; }

        /// <summary>
        /// Gets the optional initial value, without changing its Number representation or metadata.
        /// </summary>
        public DataValue StartValue { get; init; }

        /// <summary>
        /// Gets the optional warning threshold.
        /// </summary>
        public DataValue WarningValue { get; init; }

        /// <summary>
        /// Gets the optional replacement threshold. Values beyond it can still be reported.
        /// </summary>
        public DataValue LimitValue { get; init; }

        /// <summary>
        /// Gets the acquisition metadata for the units, indication and counting-direction observations.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }
}
