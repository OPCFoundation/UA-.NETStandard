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
using System.Threading;
using System.Threading.Tasks;
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
#pragma warning disable IDE0001 // "Variant" is ambiguous with System.Variant on net472/net48 (CS0419).
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

            // Value-write handler for the simulation Enabled control variable.
            // The node and its value are baked into the NodeSet2 model; only the
            // write behaviour is wired here (Prio 2).
            builder.CTT.Scalar.Scalar_Simulation.Scalar_Simulation_Enabled
                .OnWrite((context, node, ref value) =>
                {
                    try
                    {
                        m_simulationEnabled = (bool)value;
                        return ServiceResult.Good;
                    }
                    catch (Exception e)
                    {
                        m_logger.ErrorWritingEnabledVariable(e);
                        return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Enabled variable.");
                    }
                });
            builder.CTT.Scalar.Scalar_Simulation.Scalar_Simulation_Interval
                .OnWrite(OnWriteInterval);

            // The two event-trigger variables raise a simple event when written.
            builder.CTT.NodeIds.NodeIds_Events.NodeIds_Events_TriggerNode01
                .OnWrite(RaiseTriggerEventAsync);
            builder.CTT.NodeIds.NodeIds_Events.NodeIds_Events_TriggerNode02
                .OnWrite(RaiseTriggerEventAsync);

            async ValueTask<AttributeWriteResult> RaiseTriggerEventAsync(
                ISystemContext context,
                NodeState node,
                Variant value,
                CancellationToken cancellationToken)
            {
                _ = value;
                var e = new BaseEventState(null);
                e.Initialize(
                    context,
                    node,
                    EventSeverity.Medium,
                    new LocalizedText($"Trigger event from '{node.DisplayName.Text}'"));
                BaseObjectState notifier = m_historicalEventNotifier ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "The historical CTT event notifier is not configured.");
                await notifier.ReportEventAsync(
                    context,
                    e,
                    cancellationToken).ConfigureAwait(false);
                return new AttributeWriteResult(ServiceResult.Good);
            }

            // AccessLevelEx advertises non-atomic read/write on the read/write
            // static scalar. It cannot be expressed in the NodeSet2 model (the
            // UANodeSet schema has no AccessLevelEx attribute), so it is applied
            // here through the fluent .Node escape hatch (Prio 2). The divergent
            // UserAccessLevel values, in contrast, are now baked directly into
            // the NodeSet2 model (Prio 1) and loaded by the source generator.
            builder.CTT.Scalar.Scalar_Static
                .Scalar_Static_NonatomicReadWrite.Node.AccessLevelEx =
                AccessLevels.CurrentReadOrWrite |
                (uint)AccessLevelExType.NonatomicRead |
                (uint)AccessLevelExType.NonatomicWrite;
        }

        /// <summary>
        /// Registers the periodic value simulation on the fluent builder. The
        /// dynamic nodes are collected imperatively in
        /// <see cref="RegisterSimulationVariables"/> (they are baked into the
        /// NodeSet2 model, but the fluent surface has no per-variable
        /// random-value model), while the periodic loop that pushes fresh
        /// random values to them is expressed here via
        /// <c>Simulation().OnTick(...)</c>. The loop fires at a short fixed
        /// resolution and the tick handler applies the writable
        /// <c>Scalar_Simulation_Interval</c> value.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            builder
                .Simulation(s_simulationTickInterval)
                .OnTick((_, elapsed, cancellationToken) => RunSimulationStepAsync(elapsed, cancellationToken));
        }
    }
#pragma warning restore IDE0001
}
