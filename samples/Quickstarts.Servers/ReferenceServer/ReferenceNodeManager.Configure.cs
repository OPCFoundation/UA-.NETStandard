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

using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Fluent wiring for the CTT reference server methods.
    /// </summary>
    /// <remarks>
    /// The method nodes and their input/output argument declarations are
    /// baked into the NodeSet2 model via <c>&lt;Name&gt;MethodType</c>
    /// declaration nodes that the instance methods point at through
    /// <c>MethodDeclarationId</c>. The source generator therefore emits a
    /// typed <c>OnCall</c> overload for every method, so the call handlers
    /// can be expressed as plain lambdas instead of hand-written
    /// <c>GenericMethodCalledEventHandler</c> callbacks that unpack and
    /// repack <see cref="Opc.Ua.Variant"/> arguments by index.
    /// </remarks>
    public partial class ReferenceNodeManager
    {
        partial void Configure(IReferenceNodeManagerBuilder builder)
        {
            // No inputs, no outputs.
            builder.CTT.Methods.Methods_Void
                .OnCall(() => { });

            // float + uint -> float.
            builder.CTT.Methods.Methods_Add
                .OnCall((value, count) => value + count);

            // short * ushort -> int.
            builder.CTT.Methods.Methods_Multiply
                .OnCall((op1, op2) => op1 * op2);

            // int / ushort -> float.
            builder.CTT.Methods.Methods_Divide
                .OnCall((op1, op2) => op1 / (float)op2);

            // short - byte -> short.
            builder.CTT.Methods.Methods_Substract
                .OnCall((op1, op2) => (short)(op1 - op2));

            // string -> string.
            builder.CTT.Methods.Methods_Hello
                .OnCall(value => "hello " + value);

            // string -> void.
            builder.CTT.Methods.Methods_Input
                .OnCall(_ => { });

            // void -> string.
            builder.CTT.Methods.Methods_Output
                .OnCall(() => "Output");

            // Value-write handlers for the simulation control and event
            // trigger variables. The nodes and their values are baked into the
            // NodeSet2 model; only the write behaviour is wired here (Prio 2).
            builder.CTT.Scalar.Scalar_Simulation.Scalar_Simulation_Interval
                .OnWrite(OnWriteInterval);
            builder.CTT.Scalar.Scalar_Simulation.Scalar_Simulation_Enabled
                .OnWrite(OnWriteEnabled);
            builder.CTT.NodeIds.NodeIds_Events.NodeIds_Events_TriggerNode01
                .OnWrite(OnWriteTriggerNode);
            builder.CTT.NodeIds.NodeIds_Events.NodeIds_Events_TriggerNode02
                .OnWrite(OnWriteTriggerNode);

            // The selection-list write validation rejects values outside the
            // baked Selections array. The node, its Value and the Selections /
            // SelectionDescriptions / RestrictToList properties are all baked
            // into the NodeSet2 model; only the write behaviour is wired here
            // (Prio 2).
            builder.CTT.DataAccess.DataAccess_SelectionList.DataAccess_SelectionList_Colors
                .OnWrite(OnWriteSelectionList);

            // AccessLevelEx advertises non-atomic read/write on the read/write
            // static scalar. It cannot be expressed in the NodeSet2 model (the
            // UANodeSet schema has no AccessLevelEx attribute), so it is applied
            // here through the fluent .Node escape hatch (Prio 2). The divergent
            // UserAccessLevel values, in contrast, are now baked directly into
            // the NodeSet2 model (Prio 1) and loaded by the source generator.
            ((BaseVariableState)builder.CTT.Scalar.Scalar_Static
                .Scalar_Static_NonatomicReadWrite.Node).AccessLevelEx =
                AccessLevels.CurrentReadOrWrite
                | (uint)AccessLevelExType.NonatomicRead
                | (uint)AccessLevelExType.NonatomicWrite;
        }
    }
}
