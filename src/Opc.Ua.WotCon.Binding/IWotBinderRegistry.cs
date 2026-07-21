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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// The runtime-neutral binder seam the materialization coordinator uses to
    /// discover binding capabilities and to prepare / activate / deactivate
    /// bindings around a projection. Concrete binders and executors are registered
    /// with <see cref="WotProtocolBinderRegistry"/>; the default
    /// <see cref="NullWotBinderRegistry"/> reports no binders, so every form is
    /// unsupported (strict closures fail; non-strict closures materialize degraded
    /// nodes with <see cref="StatusCodes.BadConfigurationError"/>).
    /// </summary>
    public interface IWotBinderRegistry
    {
        /// <summary>Gets the binding capability snapshots advertised by the registry.</summary>
        IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }

        /// <summary>
        /// Validates and compiles a resource's forms into an immutable binding
        /// plan. Prepare is side-effect free: it classifies forms as supported or
        /// unsupported and compiles supported forms, but never performs transport
        /// I/O.
        /// </summary>
        WotBindingPlan Prepare(WotBindingPlanRequest request);

        /// <summary>
        /// Activates a prepared plan after the projection has been committed as the
        /// active generation.
        /// </summary>
        ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default);

        /// <summary>Deactivates a plan when its projection is retired.</summary>
        ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The default binder registry: it advertises no capabilities and treats every
    /// form as unsupported. It provides the "no concrete network protocol"
    /// baseline used when no binders are registered.
    /// </summary>
    public sealed class NullWotBinderRegistry : IWotBinderRegistry
    {
        /// <summary>Gets the shared instance.</summary>
        public static NullWotBinderRegistry Instance { get; } = new NullWotBinderRegistry();

        /// <inheritdoc/>
        public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }
            = Array.Empty<WoTBindingCapabilityDataType>();

        /// <inheritdoc/>
        public WotBindingPlan Prepare(WotBindingPlanRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.Forms.IsEmpty)
            {
                return WotBindingPlan.Empty;
            }
            return new WotBindingPlan(
                request.ResourceXid,
                ImmutableArray<WoTBindingCapabilityDataType>.Empty,
                ImmutableArray<WotCompiledForm>.Empty,
                request.Forms,
                ImmutableArray.Create(WotBindingDiagnostic.Warning(
                    WotBindingDiagnosticCode.NonExecutableBinding,
                    "No binder is registered; affordance forms are materialized as " +
                    "degraded nodes (BadConfigurationError) or fail a strict closure.")));
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            => default;

        /// <inheritdoc/>
        public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            => default;
    }
}
