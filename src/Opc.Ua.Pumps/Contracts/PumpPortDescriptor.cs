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
