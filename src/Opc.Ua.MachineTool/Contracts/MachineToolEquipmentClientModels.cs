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
    /// <summary>
    /// A bounded equipment read. Null NodeIds and DataValue.IsNull distinguish absent optional members.
    /// </summary>
    public sealed record MachineToolEquipmentReadSnapshot
    {
        /// <summary>
        /// Gets the actual machine identifier.
        /// </summary>
        public NodeId MachineId { get; init; }

        /// <summary>
        /// Gets the actual Tools list, or NodeId.Null when the optional feature is absent.
        /// </summary>
        public NodeId ToolsNodeId { get; init; }

        /// <summary>
        /// Gets the original optional NodeVersion sample.
        /// </summary>
        public DataValue NodeVersion { get; init; }

        /// <summary>
        /// Gets the top-level tools and their bounded constituent inventories.
        /// </summary>
        public ArrayOf<MachineToolToolReadSnapshot> Tools { get; init; }
    }

    /// <summary>
    /// The original samples and actual generated identity of one tool, multitool or companion subtype.
    /// </summary>
    public sealed record MachineToolToolReadSnapshot
    {
        /// <summary>
        /// Gets the instance identifier.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the original qualified browse name.
        /// </summary>
        public QualifiedName BrowseName { get; init; }

        /// <summary>
        /// Gets the actual type, including vendor and GMS subtypes.
        /// </summary>
        public NodeId TypeDefinitionId { get; init; }

        /// <summary>
        /// Gets the standard base kind recognized through the server's type hierarchy.
        /// </summary>
        public MachineToolToolKind Kind { get; init; }

        /// <summary>
        /// Gets the optional global identifier sample.
        /// </summary>
        public DataValue Identifier { get; init; }

        /// <summary>
        /// Gets the optional operator-facing name sample.
        /// </summary>
        public DataValue Name { get; init; }

        /// <summary>
        /// Gets the optional Location object's original identifier.
        /// </summary>
        public NodeId LocationNodeId { get; init; }

        /// <summary>
        /// Gets the location's Name sample, not the tool's name.
        /// </summary>
        public DataValue LocationName { get; init; }

        /// <summary>
        /// Gets the original UInt16 place-number sample.
        /// </summary>
        public DataValue PlaceNumber { get; init; }

        /// <summary>
        /// Gets the controller's first identifier, absent for multitools and direct BaseTool subtypes.
        /// </summary>
        public DataValue ControlIdentifier1 { get; init; }

        /// <summary>
        /// Gets the optional second controller identifier.
        /// </summary>
        public DataValue ControlIdentifier2 { get; init; }

        /// <summary>
        /// Gets the original ToolManagement enumeration sample.
        /// </summary>
        public DataValue ControlIdentifierInterpretation { get; init; }

        /// <summary>
        /// Gets the controller's reported lock sample.
        /// </summary>
        public DataValue Locked { get; init; }

        /// <summary>
        /// Gets the original ToolLocked enumeration sample.
        /// </summary>
        public DataValue ReasonForLocking { get; init; }

        /// <summary>
        /// Gets the optional last-use sample with its own acquisition metadata.
        /// </summary>
        public DataValue LastUsage { get; init; }

        /// <summary>
        /// Gets the optional planned-use sample.
        /// </summary>
        public DataValue PlannedForOperating { get; init; }

        /// <summary>
        /// Gets the optional ToolLife container identifier.
        /// </summary>
        public NodeId ToolLifeNodeId { get; init; }

        /// <summary>
        /// Gets independent life samples, which must not be accumulated together.
        /// </summary>
        public ArrayOf<MachineToolToolLifeReadSnapshot> Life { get; init; }

        /// <summary>
        /// Gets the bounded single-tool inventory inside this multitool.
        /// </summary>
        public ArrayOf<MachineToolToolReadSnapshot> Tools { get; init; }
    }

    /// <summary>
    /// A tool-life variable retaining its concrete numerical representation, quality, times and optional presence.
    /// </summary>
    public sealed record MachineToolToolLifeReadSnapshot
    {
        /// <summary>
        /// Gets the actual life-variable identifier.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the original browse name, including a companion namespace such as GMS Qualified.
        /// </summary>
        public QualifiedName BrowseName { get; init; }

        /// <summary>
        /// Gets the actual variable type.
        /// </summary>
        public NodeId TypeDefinitionId { get; init; }

        /// <summary>
        /// Gets the declared data type, which can be more specific than Number in a companion declaration.
        /// </summary>
        public NodeId DataType { get; init; }

        /// <summary>
        /// Gets the original value sample without numeric conversion.
        /// </summary>
        public DataValue Value { get; init; }

        /// <summary>
        /// Gets the engineering-unit sample at the actual canonical MachineTool-qualified child.
        /// </summary>
        public DataValue EngineeringUnits { get; init; }

        /// <summary>
        /// Gets the original indication sample.
        /// </summary>
        public DataValue Indication { get; init; }

        /// <summary>
        /// Gets the original counting-direction sample.
        /// </summary>
        public DataValue IsCountingUp { get; init; }

        /// <summary>
        /// Gets the optional initial-value sample.
        /// </summary>
        public DataValue StartValue { get; init; }

        /// <summary>
        /// Gets the optional warning threshold sample.
        /// </summary>
        public DataValue WarningValue { get; init; }

        /// <summary>
        /// Gets the optional replacement threshold sample.
        /// </summary>
        public DataValue LimitValue { get; init; }
    }
}
