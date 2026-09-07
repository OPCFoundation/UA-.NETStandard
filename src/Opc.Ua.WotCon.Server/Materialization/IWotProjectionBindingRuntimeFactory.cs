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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Builds the per-generation OPC UA target-mapping binding runtime for a
    /// runtime NodeSet. Injected into <see cref="LifecycleWotProjectionHost"/>,
    /// which invokes it from <see cref="Ua.Server.RuntimeNodeSet.RuntimeNodeSetOptions.ConfigureAsync"/>
    /// once the generation's NodeSet2 content has been imported, so target
    /// mappings are resolved against the freshly materialized predefined nodes.
    /// The returned <see cref="IAsyncDisposable"/> (if any) is owned by that
    /// NodeSet generation and is disposed with it.
    /// </summary>
    public interface IWotProjectionBindingRuntimeFactory
    {
        /// <summary>
        /// Wires the OPC UA target-mapping bindings declared by
        /// <paramref name="bindingPlans"/> onto the freshly imported predefined
        /// nodes exposed by <paramref name="builder"/>.
        /// </summary>
        /// <param name="builder">
        /// The fluent builder for the node manager generation being activated.
        /// </param>
        /// <param name="bindingPlans">
        /// The prepared binding plans for the projected closure. Forms without
        /// a target mapping, and non-executable forms, are ignored.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The generation-owned binding runtime, or <c>null</c> when no target
        /// mapping was wired (for example an empty <paramref name="bindingPlans"/>).
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// A target mapping is missing, malformed, ambiguous, resolves to the
        /// wrong node class, mismatches its declared target type, conflicts
        /// with another mapping to the same target, duplicates a read or write
        /// mapping, or is declared on an unsupported operation. Structured
        /// (<c>uav:mapByFieldPath</c>) validation that depends on the
        /// target's structure type being registered — the structure lookup,
        /// root instance validation, and per-field path resolution — is
        /// deferred past this call; see the class remarks on
        /// <see cref="WotStructuredGroupState"/> for why, and for how a
        /// first structured read or write fails deterministically instead.
        /// </exception>
        ValueTask<IAsyncDisposable?> CreateAsync(
            INodeManagerBuilder builder,
            ArrayOf<WotBindingPlan> bindingPlans,
            CancellationToken cancellationToken = default);
    }
}
