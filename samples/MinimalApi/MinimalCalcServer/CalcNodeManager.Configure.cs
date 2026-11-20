/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;

namespace Calc
{
    /// <summary>
    /// Source-generated <c>CustomNodeManager2</c> for the calculator
    /// sample. The <c>[NodeManager]</c> attribute opts this partial class
    /// in to source generation: the generator emits the sibling partial
    /// that owns the predefined-node load and calls back into the
    /// <c>Configure</c> partials in <c>CalcNodeManager.Configure.cs</c>.
    /// </summary>
    /// <remarks>
    /// The calculator model intentionally exposes three method shapes —
    /// sync int+int→int, async double+double→double, and reference-typed
    /// string+string→string — to exercise the generator's typed
    /// <c>OnCall</c> input-unpack and output-box code paths end-to-end.
    /// The wiring lives in the second partial; this file holds only the
    /// attribute-bearing class declaration so that the source generator
    /// pipeline (<c>[NodeManager]</c> + <c>AdditionalFiles</c> NodeSet2)
    /// can be reasoned about in one glance.
    /// </remarks>
    [NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Calc/")]
    public partial class CalcNodeManager
    {
        partial void Configure(INodeManagerBuilder builder)
        {
            // Intentionally empty. Kept to mirror the Boiler sample and
            // demonstrate that the typed and non-typed Configure partials
            // coexist on the same class — the generated address-space
            // bootstrap invokes both. The calculator sample wires every
            // node through the typed surface in
            // Configure(ICalcNodeManagerBuilder).
        }

        partial void Configure(ICalcNodeManagerBuilder builder)
        {
            // Sync int+int→int — exercises Variant.TryGetValue<int> on
            // each input arg and Variant.From<int> on the boxed result.
            builder.Calculator.Add
                .OnCall((a, b) => a + b);

            // Async double+double→double — exercises the typed async
            // OnCall overload (Func<double, double, CancellationToken,
            // ValueTask<double>>) end-to-end through
            // AsyncCustomNodeManager.CallAsync, plus Variant.From<double>
            // on the boxed result.
            builder.Calculator.Multiply
                .OnCall(async (x, y, ct) =>
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return x * y;
                });

            // Sync string+string→string — exercises reference-type
            // marshalling on both inputs and the output. Coalesces null
            // inputs to empty so the handler is well-defined when a
            // client passes a null Variant in either slot.
            builder.Calculator.Concat
                .OnCall((left, right) =>
                    (left ?? string.Empty) + (right ?? string.Empty));
        }
    }
}
