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
    /// A bounded prognosis read with original quality, times and optional-member presence.
    /// </summary>
    public sealed record MachineToolPrognosisReadSnapshot
    {
        /// <summary>
        /// Gets the actual machine identifier.
        /// </summary>
        public NodeId MachineId { get; init; }

        /// <summary>
        /// Gets the original prognosis list, or NodeId.Null when the optional surface is absent.
        /// </summary>
        public NodeId ListNodeId { get; init; }

        /// <summary>
        /// Gets the original optional list version sample.
        /// </summary>
        public DataValue NodeVersion { get; init; }

        /// <summary>
        /// Gets the complete bounded forecast inventory.
        /// </summary>
        public ArrayOf<MachineToolPrognosisReadEntry> Entries { get; init; }
    }

    /// <summary>
    /// The raw samples of a concrete prognosis. DataValue.IsNull denotes an absent member, not bad quality.
    /// </summary>
    public sealed record MachineToolPrognosisReadEntry
    {
        /// <summary>
        /// Gets the actual forecast node identifier.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets its original qualified browse name.
        /// </summary>
        public QualifiedName BrowseName { get; init; }

        /// <summary>
        /// Gets the actual type definition, including generated companion and vendor subtypes.
        /// </summary>
        public NodeId TypeDefinitionId { get; init; }

        /// <summary>
        /// Gets the recognized concrete base kind.
        /// </summary>
        public MachineToolPrognosisKind Kind { get; init; }

        /// <summary>
        /// Gets the provider's original UtcTime prediction sample.
        /// </summary>
        public DataValue PredictedTime { get; init; }

        /// <summary>
        /// Gets the activity when declared by this prognosis kind.
        /// </summary>
        public DataValue Activity { get; init; }

        /// <summary>
        /// Gets the operation location, when declared.
        /// </summary>
        public DataValue Location { get; init; }

        /// <summary>
        /// Gets the optional tool identifier sample.
        /// </summary>
        public DataValue ToolIdentifier { get; init; }

        /// <summary>
        /// Gets the optional tool name sample.
        /// </summary>
        public DataValue ToolName { get; init; }

        /// <summary>
        /// Gets the actual affected tool's NodeId sample, when declared and represented.
        /// </summary>
        public DataValue ToolNodeId { get; init; }

        /// <summary>
        /// Gets the optional part identifier sample.
        /// </summary>
        public DataValue PartIdentifier { get; init; }

        /// <summary>
        /// Gets the part name sample.
        /// </summary>
        public DataValue PartName { get; init; }

        /// <summary>
        /// Gets the retained standard affected-part reference sample.
        /// </summary>
        public DataValue PartNodeId { get; init; }

        /// <summary>
        /// Gets the production job identifier sample.
        /// </summary>
        public DataValue SourceIdentifier { get; init; }

        /// <summary>
        /// Gets the retained standard affected-job reference sample.
        /// </summary>
        public DataValue JobNodeId { get; init; }

        /// <summary>
        /// Gets the utility name sample.
        /// </summary>
        public DataValue UtilityName { get; init; }
    }
}
