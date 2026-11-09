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

namespace Opc.Ua.Client.Programs
{
    /// <summary>
    /// A Program value with its original quality and timestamps. An absent optional node is
    /// represented by a null NodeId and BadNotFound, not by a successful default value.
    /// </summary>
    /// <param name="NodeId">The resolved node, or the null NodeId when absent.</param>
    /// <param name="DataValue">The unmodified read result.</param>
    public sealed record ProgramValue(NodeId NodeId, DataValue DataValue)
    {
        /// <summary>
        /// Whether the node exists, independently of whether its value can be read.
        /// </summary>
        public bool IsAvailable => !NodeId.IsNull;
    }

    /// <summary>
    /// Type-level lifetime properties. UInt32.MaxValue denotes unlimited limits only for the SDK
    /// model; this is not a universal convention for vendor Program types.
    /// </summary>
    /// <param name="Creatable">Whether clients may create an invocation.</param>
    /// <param name="InstanceCount">The live invocation count.</param>
    /// <param name="MaxInstanceCount">The invocation limit.</param>
    /// <param name="MaxRecycleCount">The recycle limit.</param>
    public sealed record ProgramTypeCapabilities(
        ProgramValue Creatable,
        ProgramValue InstanceCount,
        ProgramValue MaxInstanceCount,
        ProgramValue MaxRecycleCount);

    /// <summary>
    /// Published method metadata, including vendor input and output signatures.
    /// Executability is a read-time observation, not a reservation for a later Call.
    /// </summary>
    /// <param name="MethodId">The instance method NodeId.</param>
    /// <param name="BrowseName">The namespace-qualified method name.</param>
    /// <param name="Executable">The executable attribute.</param>
    /// <param name="UserExecutable">The executable attribute for this session's user.</param>
    /// <param name="InputArguments">The optional input argument definitions.</param>
    /// <param name="OutputArguments">The optional output argument definitions.</param>
    public sealed record ProgramMethod(
        NodeId MethodId,
        QualifiedName BrowseName,
        ProgramValue Executable,
        ProgramValue UserExecutable,
        ProgramValue InputArguments,
        ProgramValue OutputArguments);

    /// <summary>
    /// Instance capabilities, separate from type-level lifetime properties.
    /// RecycleCount may legitimately have BadOutOfRange quality after Int32 capacity is exceeded.
    /// No atomic snapshot across these reads is implied.
    /// </summary>
    /// <param name="Deletable">Whether client deletion is enabled.</param>
    /// <param name="AutoDelete">Whether execution termination removes the invocation.</param>
    /// <param name="RecycleCount">The recycle count with its original status.</param>
    /// <param name="DiagnosticId">The optional ProgramDiagnostic node.</param>
    /// <param name="FinalResultId">The optional FinalResultData object.</param>
    /// <param name="Methods">Published methods and their signatures.</param>
    public sealed record ProgramCapabilities(
        ProgramValue Deletable,
        ProgramValue AutoDelete,
        ProgramValue RecycleCount,
        NodeId DiagnosticId,
        NodeId FinalResultId,
        ArrayOf<ProgramMethod> Methods);

    /// <summary>
    /// A modern ProgramDiagnostic2 read, retaining quality and timestamps.
    /// Diagnostic is absent when the node is absent or has bad quality. Legacy diagnostics are
    /// not silently converted into the modern structure.
    /// </summary>
    /// <param name="Value">The original read result.</param>
    /// <param name="Diagnostic">The decoded modern diagnostic, when readable.</param>
    public sealed record ProgramDiagnosticResult(
        ProgramValue Value,
        ProgramDiagnostic2DataType? Diagnostic);

    /// <summary>
    /// A final-result variable, its relative published browse path, and status-preserving attributes.
    /// </summary>
    /// <param name="NodeId">The result variable.</param>
    /// <param name="BrowsePath">The path relative to FinalResultData.</param>
    /// <param name="Value">The value, including quality and timestamps.</param>
    /// <param name="DataType">The published DataType attribute.</param>
    /// <param name="ValueRank">The published ValueRank attribute.</param>
    public sealed record ProgramResultValue(
        NodeId NodeId,
        ArrayOf<QualifiedName> BrowsePath,
        DataValue Value,
        DataValue DataType,
        DataValue ValueRank);

    /// <summary>
    /// Published final results. A null NodeId explicitly identifies an absent optional result object.
    /// Values are ordinary reads and are not an atomic cross-variable snapshot.
    /// </summary>
    /// <param name="NodeId">The FinalResultData object, or null when absent.</param>
    /// <param name="Values">Its recursively discovered result variables.</param>
    public sealed record ProgramFinalResults(NodeId NodeId, ArrayOf<ProgramResultValue> Values)
    {
        /// <summary>
        /// Whether FinalResultData exists, even when it currently contains no values.
        /// </summary>
        public bool IsAvailable => !NodeId.IsNull;
    }
}
