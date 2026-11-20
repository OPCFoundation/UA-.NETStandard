/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Builds the per-generation OPC UA projection binding runtime for a
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
        /// Wires the property mappings and local method/event bindings declared by
        /// <paramref name="bindingPlans"/> onto the freshly imported predefined
        /// nodes exposed by <paramref name="builder"/>.
        /// </summary>
        /// <param name="builder">
        /// The fluent builder for the node manager generation being activated.
        /// </param>
        /// <param name="bindingPlans">
        /// The prepared binding plans for the projected closure. Unmapped
        /// properties retain their existing behavior. A local Method or event
        /// declaration must have a resolvable identity and an executable form.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The generation-owned binding runtime, or <c>null</c> for an empty
        /// <paramref name="bindingPlans"/> collection.
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
