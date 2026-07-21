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
    /// The concrete binder registry. It aggregates independently injected protocol
    /// binders (planner + identification) and optional executors, performs
    /// deterministic selection using pinned identification rules (not the URI
    /// scheme alone), compiles forms into immutable plans, and drives the
    /// Prepare / Activate / Deactivate lifecycle. Multiple versions of a binding
    /// can coexist; the executor for a binder is matched by id so a protocol can be
    /// validated without an executor and executed once one is registered.
    /// </summary>
    public sealed class WotProtocolBinderRegistry : IWotBinderRegistry
    {
        /// <summary>Initializes a new binder registry.</summary>
        /// <param name="binders">The protocol binders (planner + identification).</param>
        /// <param name="executors">The optional runtime executors.</param>
        /// <param name="credentials">The credential provider used at activation time.</param>
        /// <param name="codecs">The codec registry used to select payload codecs.</param>
        /// <param name="bounds">The safety bounds enforced during planning.</param>
        public WotProtocolBinderRegistry(
            IEnumerable<IWotProtocolBinder> binders,
            IEnumerable<IWotBindingExecutor>? executors = null,
            IWotCredentialProvider? credentials = null,
            IWotCodecRegistry? codecs = null,
            WotBindingBounds? bounds = null)
        {
            if (binders is null)
            {
                throw new ArgumentNullException(nameof(binders));
            }
            m_credentials = credentials ?? NullWotCredentialProvider.Instance;
            m_codecs = codecs ?? WotPayloadCodecRegistry.Default;
            m_bounds = bounds ?? WotBindingBounds.Default;

            var seenBinderKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (IWotProtocolBinder binder in binders)
            {
                if (binder is null)
                {
                    continue;
                }
                // Multiple versions coexist; the same id@version is deduplicated.
                if (seenBinderKeys.Add(binder.Identity.Key))
                {
                    m_binders[binder.Identity.Key] = binder;
                    m_ordered.Add(binder);
                }
            }
            // Deterministic evaluation order: ordinal by id@version.
            m_ordered.Sort(static (a, b) =>
                string.CompareOrdinal(a.Identity.Key, b.Identity.Key));

            if (executors is not null)
            {
                foreach (IWotBindingExecutor executor in executors)
                {
                    if (executor is null)
                    {
                        continue;
                    }
                    m_executorsByKey[executor.Identity.Key] = executor;
                    // Last executor for an id wins as the id-level default.
                    m_executorsById[executor.Identity.Id] = executor;
                }
            }

            var capabilities = ImmutableArray.CreateBuilder<WoTBindingCapabilityDataType>(m_ordered.Count);
            foreach (IWotProtocolBinder binder in m_ordered)
            {
                capabilities.Add(binder.Capability.ToDataType());
            }
            Capabilities = capabilities.ToImmutable();
        }

        /// <inheritdoc/>
        public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }

        /// <summary>Gets the registered binders in deterministic evaluation order.</summary>
        public IReadOnlyList<IWotProtocolBinder> Binders => m_ordered;

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

            WotBindingPlanContext context = request.CreateContext(m_codecs, m_bounds);
            var compiled = ImmutableArray.CreateBuilder<WotCompiledForm>();
            var unsupported = ImmutableArray.CreateBuilder<WotAffordanceForm>();
            var diagnostics = ImmutableArray.CreateBuilder<WotBindingDiagnostic>();
            var participating = new Dictionary<string, WoTBindingCapabilityDataType>(StringComparer.Ordinal);

            foreach (WotAffordanceForm form in request.Forms)
            {
                IWotProtocolBinder? binder = Select(form, request.Selection);
                if (binder is null)
                {
                    unsupported.Add(form);
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.UnsupportedScheme,
                        $"No binder handles the '{form.AffordanceName}' form.",
                        form.Pointer("href")));
                    continue;
                }

                WotBindingCompilation compilation = binder.Planner.Compile(form, context);
                diagnostics.AddRange(compilation.Diagnostics);
                if (!compilation.IsSupported || compilation.HasErrors || compilation.Entries.IsEmpty)
                {
                    unsupported.Add(form);
                    continue;
                }

                participating[binder.Identity.Key] = binder.Capability.ToDataType();
                bool executorPresent = HasExecutor(binder.Identity);
                foreach (WotCompiledForm entry in compilation.Entries)
                {
                    bool effective = entry.IsExecutable && executorPresent;
                    compiled.Add(entry.WithExecutable(effective));
                    if (!effective)
                    {
                        diagnostics.Add(WotBindingDiagnostic.Info(
                            WotBindingDiagnosticCode.NonExecutableBinding,
                            $"The binding '{binder.Identity.Id}' validated the '{form.AffordanceName}' " +
                            "form but no runtime executor is available; it is materialized as non-executable.",
                            entry.JsonPointer));
                    }
                }
            }

            return new WotBindingPlan(
                request.ResourceXid,
                participating.Values.ToImmutableArray(),
                compiled.ToImmutable(),
                unsupported.ToImmutable(),
                diagnostics.ToImmutable());
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
        {
            if (plan is null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            lock (m_activeLock)
            {
                m_activeResources.Add(plan.ResourceXid);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
        {
            if (plan is null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            lock (m_activeLock)
            {
                m_activeResources.Remove(plan.ResourceXid);
            }
            return default;
        }

        /// <summary>Gets whether a resource's plan is currently activated.</summary>
        public bool IsActive(string resourceXid)
        {
            lock (m_activeLock)
            {
                return m_activeResources.Contains(resourceXid);
            }
        }

        /// <summary>Attempts to resolve the executor registered for a binder identity.</summary>
        public bool TryGetExecutor(WotBindingIdentity identity, out IWotBindingExecutor executor)
        {
            if (identity is null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (m_executorsByKey.TryGetValue(identity.Key, out IWotBindingExecutor? exact) && exact is not null)
            {
                executor = exact;
                return true;
            }
            if (m_executorsById.TryGetValue(identity.Id, out IWotBindingExecutor? byId) && byId is not null)
            {
                executor = byId;
                return true;
            }
            executor = null!;
            return false;
        }

        /// <summary>
        /// Opens a live channel for an executable compiled form using the
        /// registry's credential provider, codecs and bounds. Used by the runtime
        /// value adapter and by end-to-end tests.
        /// </summary>
        public ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, CancellationToken cancellationToken = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            if (!TryGetExecutor(form.Binding, out IWotBindingExecutor executor))
            {
                throw new InvalidOperationException(
                    $"No executor is registered for binding '{form.Binding.Key}'.");
            }
            var context = new WotExecutorContext(m_credentials, m_codecs, m_bounds);
            return executor.ActivateAsync(form, context, cancellationToken);
        }

        private IWotProtocolBinder? Select(WotAffordanceForm form, WotBindingSelectionContext selection)
        {
            IWotProtocolBinder? best = null;
            WotBindingMatch bestMatch = WotBindingMatch.NoMatch;
            foreach (IWotProtocolBinder binder in m_ordered)
            {
                WotBindingMatch match = binder.Identification.Match(form, selection);
                if (!match.IsMatch)
                {
                    continue;
                }
                // Higher priority wins; ties are broken by ordinal id@version, which
                // is guaranteed because m_ordered is sorted and evaluated in order.
                if (best is null || match.Priority > bestMatch.Priority)
                {
                    best = binder;
                    bestMatch = match;
                }
            }
            return best;
        }

        private bool HasExecutor(WotBindingIdentity identity)
            => m_executorsByKey.ContainsKey(identity.Key) || m_executorsById.ContainsKey(identity.Id);

        private readonly IWotCredentialProvider m_credentials;
        private readonly IWotCodecRegistry m_codecs;
        private readonly WotBindingBounds m_bounds;
        private readonly Dictionary<string, IWotProtocolBinder> m_binders =
            new Dictionary<string, IWotProtocolBinder>(StringComparer.Ordinal);
        private readonly List<IWotProtocolBinder> m_ordered = new List<IWotProtocolBinder>();
        private readonly Dictionary<string, IWotBindingExecutor> m_executorsByKey =
            new Dictionary<string, IWotBindingExecutor>(StringComparer.Ordinal);
        private readonly Dictionary<string, IWotBindingExecutor> m_executorsById =
            new Dictionary<string, IWotBindingExecutor>(StringComparer.Ordinal);
        private readonly object m_activeLock = new object();
        private readonly HashSet<string> m_activeResources = new HashSet<string>(StringComparer.Ordinal);
    }
}
