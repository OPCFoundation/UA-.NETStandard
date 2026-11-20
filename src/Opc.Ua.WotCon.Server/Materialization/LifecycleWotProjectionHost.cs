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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The production <see cref="IWotProjectionHost"/> that projects WoT closures
    /// onto the live server through the public NodeManager lifecycle. First
    /// activation prepares an addition; updates prepare a replacement transaction,
    /// migrating compatible monitored items and invalidating items unavailable
    /// in the replacement. The previous generation is not retained for future
    /// requests or continuations. The stable WoT registry NodeManager is never
    /// touched.
    /// </summary>
    public sealed partial class LifecycleWotProjectionHost : IWotInvocationProjectionHost
    {
        /// <summary>
        /// Initializes a new host over the supplied lifecycle.
        /// </summary>
        /// <param name="lifecycle">The node manager lifecycle to project onto.</param>
        /// <param name="runtimeFactory">
        /// The optional projection binding runtime factory. When supplied, each
        /// runtime NodeSet generation created for a document that carries
        /// prepared binding work in <see cref="WotProjectionDocument.BindingPlans"/> owns its own
        /// binding runtime: it is created after the NodeSet is imported (via
        /// <see cref="RuntimeNodeSetOptions.ConfigureAsync"/>) and disposed with
        /// the generation. When <c>null</c>, no binding runtime is wired (the
        /// NodeSet is materialized as data only). The stock factory skips data-only
        /// plans that need no fluent configuration or default namespace. Custom
        /// factories retain their configuration callback for every generation.
        /// </param>
        public LifecycleWotProjectionHost(
            INodeManagerLifecycle lifecycle,
            IWotProjectionBindingRuntimeFactory? runtimeFactory = null)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            m_runtimeFactory = runtimeFactory;
        }

        /// <inheritdoc/>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            return CommitProjectionAsync(
                WotProjectionChange.Add(document), cancellationToken);
        }

        /// <inheritdoc/>
        /// <param name="current">The WoT projection handle.</param>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current,
            WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            return CommitProjectionAsync(
                current?.Registration is NodeManagerProjectionRegistration
                    ? WotProjectionChange.Replace(current, document)
                    : WotProjectionChange.Add(document),
                cancellationToken);
        }

        /// <inheritdoc/>
        /// <param name="current">The WoT projection handle.</param>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
            WotProjectionHandle current,
            WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            return CommitProjectionAsync(
                current?.Registration is NodeManagerProjectionRegistration
                    ? WotProjectionChange.Replace(current, document, WotProjectionRetirementPolicy.Invalidate)
                    : WotProjectionChange.Add(document),
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<LifecycleResult?> RemoveAsync(
            WotProjectionHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (m_lifecycle.IsShuttingDown)
            {
                return null;
            }

            IWotPreparedProjectionPublication prepared = await PrepareAsync(
                [WotProjectionChange.Remove(handle)], null, null, cancellationToken).ConfigureAwait(false);
            await using var preparedLifetime = prepared.ConfigureAwait(false);
            await prepared.CommitAsync(static _ => default, static () => { }, cancellationToken)
                .ConfigureAwait(false);
            LifecycleResult result = ((PreparedPublication)prepared).Result!;
            try
            {
                await result.Retired.ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The committed result retains the original faulted retirement task; removal must not be replayed.
                return result;
            }
            return result;
        }

        /// <summary>
        /// Publishes the runtime and registration in the same prepared transaction before readiness.
        /// </summary>
        private async ValueTask<WotProjectionHandle> CommitProjectionAsync(
            WotProjectionChange change,
            CancellationToken cancellationToken)
        {
            IWotPreparedProjectionPublication prepared = await PrepareAsync(
                [change], null, null, cancellationToken).ConfigureAwait(false);
            await using var preparedLifetime = prepared.ConfigureAwait(false);
            await prepared.CommitAsync(static _ => default, static () => { }, cancellationToken)
                .ConfigureAwait(false);
            WotProjectionHandle candidate = prepared.Projections[0];
            return new WotProjectionHandle(
                candidate.ClosureKey,
                candidate.Generation,
                candidate.Registration,
                candidate.RootNodeIds,
                candidate.MaterializedNodeCount,
                prepared.CleanupFailure is { } failure
                    ? "The projection is active, but reconciliation is pending: " + failure.Message
                    : string.Empty);
        }

        /// <summary>
        /// Rejects stale exact-generation handles before preparing on captured writer evidence.
        /// </summary>
        /// <param name="registration">The exact owner retained by the WoT handle.</param>
        private static void RequireCurrent(NodeManagerRegistration registration)
        {
            if (!registration.Slot.TryGetCurrent(out NodeManagerSlotSnapshot current) ||
                !ReferenceEquals(current.NodeManager, registration.NodeManager))
            {
                throw new InvalidOperationException("The projection handle no longer identifies the current owner.");
            }
        }

        private RuntimeNodeSetOptions BuildOptions(WotProjectionDocument document)
        {
            var sources = new RuntimeNodeSetSource[document.Sources.Length];
            for (int i = 0; i < document.Sources.Length; i++)
            {
                WotProjectionSource source = document.Sources[i];
                byte[] xml = source.NodeSetXml;
                var uris = new ArrayOf<string>(source.ModelNamespaceUris.ToArray());
                sources[i] = RuntimeNodeSetSource.FromStream(
                    source.Name,
                    _ => new ValueTask<Stream>(new MemoryStream(xml, writable: false)),
                    uris);
            }
            var options = new RuntimeNodeSetOptions
            {
                Sources = new ArrayOf<RuntimeNodeSetSource>(sources),
                DefaultNamespaceUri = document.DefaultNamespaceUri
            };
            if (m_runtimeFactory is { } runtimeFactory &&
                (runtimeFactory is not WotProjectionBindingRuntimeFactory ||
                 document.BindingPlans.Contains(plan => plan is not null &&
                    (!plan.CompiledForms.IsEmpty ||
                     !plan.ProjectedAffordances.IsEmpty ||
                     !plan.UnsupportedForms.IsEmpty))))
            {
                ArrayOf<WotBindingPlan> bindingPlans = document.BindingPlans;
                options.ConfigureAsync = (builder, cancellationToken)
                    => runtimeFactory.CreateAsync(builder, bindingPlans, cancellationToken);
            }
            return options;
        }

        private readonly INodeManagerLifecycle m_lifecycle;
        private readonly IWotProjectionBindingRuntimeFactory? m_runtimeFactory;

        /// <summary>
        /// Carries the lifecycle registration owned by this host through the
        /// host-agnostic <see cref="WotProjectionHandle"/>.
        /// </summary>
        private sealed class NodeManagerProjectionRegistration : IWotProjectionRegistration
        {
            public NodeManagerProjectionRegistration(NodeManagerRegistration registration)
            {
                Registration = registration;
            }

            public Guid Id => Registration.Id;

            public NodeManagerRegistration Registration { get; }
        }
    }
}
