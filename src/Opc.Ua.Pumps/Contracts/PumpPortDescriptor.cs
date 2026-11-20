/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// Which OPC 40223 port type a port below the <c>Ports</c> group is.
    /// </summary>
    public enum PumpPortKind
    {
        /// <summary>The port's type definition is none of the three OPC 40223 ports.</summary>
        Unknown = 0,

        /// <summary>A <c>DrivePortType</c> - the motor side of the pump.</summary>
        Drive = 1,

        /// <summary>An <c>InletConnectionPortType</c> - the suction side.</summary>
        InletConnection = 2,

        /// <summary>An <c>OutletConnectionPortType</c> - the discharge side.</summary>
        OutletConnection = 3
    }

    /// <summary>
    /// One port below a pump's OPC 40223 <c>Ports</c> group.
    /// </summary>
    /// <remarks>
    /// <c>PortsGroupType</c> declares no children: which ports a pump has is
    /// an instance property, not a type property, so they are discovered by
    /// browsing. <see cref="Kind"/> is resolved from the port's type
    /// definition and decides which of the four groups below are populated -
    /// a drive port carries <see cref="Design"/> and
    /// <see cref="Measurements"/> only, a connection port carries all four.
    /// </remarks>
    public sealed record PumpPortDescriptor
    {
        /// <summary>
        /// The port object.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The port's browse name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The port's type definition.
        /// </summary>
        public NodeId TypeDefinitionId { get; init; }

        /// <summary>
        /// Which OPC 40223 port type the port is.
        /// </summary>
        public PumpPortKind Kind { get; init; }

        /// <summary>
        /// The port's flow direction, when it publishes one.
        /// </summary>
        public PortDirectionEnum? Direction { get; init; }

        /// <summary>
        /// The port's category, when it publishes one.
        /// </summary>
        public string? Category { get; init; }

        /// <summary>
        /// The port's ID carrier, when it publishes one.
        /// </summary>
        public string? IdCarrier { get; init; }

        /// <summary>
        /// The port's <c>Design</c> group.
        /// </summary>
        public PumpValueSet Design { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// The port's <c>Implementation</c> group, for a connection port.
        /// </summary>
        public PumpValueSet Implementation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// The port's <c>Measurements</c> group.
        /// </summary>
        public PumpValueSet Measurements { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// The port's <c>SystemRequirements</c> group, for a connection port.
        /// </summary>
        public PumpValueSet SystemRequirements { get; init; } = PumpValueSet.Empty;
    }
}
