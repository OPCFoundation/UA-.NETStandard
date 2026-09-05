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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings
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
    public sealed class WotProtocolBinderRegistry : IWotBinderRegistry, IWotBindingChannelFactory
    {
        /// <summary>
        /// Initializes a new binder registry.
        /// </summary>
        /// <param name="binders">The protocol binders (planner + identification).</param>
        /// <param name="executors">The optional runtime executors.</param>
        /// <param name="credentials">The credential provider used at activation time.</param>
        /// <param name="codecs">The codec registry used to select payload codecs.</param>
        /// <param name="bounds">The safety bounds enforced during planning.</param>
        /// <param name="endpointPolicy">The endpoint policy enforced before opening live channels.</param>
        /// <param name="telemetry">The telemetry context used for executor diagnostics.</param>
        public WotProtocolBinderRegistry(
            IEnumerable<IWotProtocolBinder> binders,
            IEnumerable<IWotBindingExecutor>? executors = null,
            IWotCredentialProvider? credentials = null,
            IWotCodecRegistry? codecs = null,
            WotBindingBounds? bounds = null,
            WotEndpointPolicy? endpointPolicy = null,
            ITelemetryContext? telemetry = null)
        {
            if (binders is null)
            {
                throw new ArgumentNullException(nameof(binders));
            }
            m_credentials = credentials ?? NullWotCredentialProvider.Instance;
            m_codecs = codecs ?? WotPayloadCodecRegistry.Default;
            m_bounds = bounds ?? WotBindingBounds.Default;
            m_endpointPolicy = endpointPolicy ?? WotEndpointPolicy.Default;
            m_telemetry = telemetry ?? AmbientMessageContext.Telemetry;

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

            ImmutableArray<WoTBindingCapabilityDataType>.Builder capabilities =
                ImmutableArray.CreateBuilder<WoTBindingCapabilityDataType>(m_ordered.Count);
            foreach (IWotProtocolBinder binder in m_ordered)
            {
                // Executors are registered above, so the advertised capability
                // already reflects whether this host can actually invoke
                // anything through the binder.
                capabilities.Add(
                    binder.Capability.ToDataType(HasExecutor(binder.Identity)));
            }
            Capabilities = capabilities.ToImmutable();
        }

        /// <inheritdoc/>
        public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }

        /// <summary>
        /// Gets the registered binders in deterministic evaluation order.
        /// </summary>
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
            ImmutableArray<WotCompiledForm>.Builder compiled = ImmutableArray.CreateBuilder<WotCompiledForm>();
            ImmutableArray<WotAffordanceForm>.Builder unsupported = ImmutableArray.CreateBuilder<WotAffordanceForm>();
            ImmutableArray<WotBindingDiagnostic>.Builder diagnostics =
                ImmutableArray.CreateBuilder<WotBindingDiagnostic>();
            var participating = new Dictionary<string, WoTBindingCapabilityDataType>(StringComparer.Ordinal);

            foreach (WotAffordanceForm form in request.Forms)
            {
                var mappingDiagnostics = new List<WotBindingDiagnostic>();
                bool mappingValid = ValidateTargetMapping(form, mappingDiagnostics);
                diagnostics.AddRange(mappingDiagnostics);
                if (!mappingValid)
                {
                    unsupported.Add(form);
                    continue;
                }

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

                bool executorPresent = HasExecutor(binder.Identity);
                participating[binder.Identity.Key] =
                    binder.Capability.ToDataType(executorPresent);
                foreach (WotCompiledForm entry in compilation.Entries)
                {
                    bool effective = entry.IsExecutable && executorPresent;
                    WotCompiledForm mapped = entry
                        .WithTargetMapping(form.TargetMapping)
                        .WithExecutable(effective);
                    compiled.Add(mapped);
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
                [.. participating.Values],
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

        /// <summary>
        /// Gets whether a resource's plan is currently activated.
        /// </summary>
        public bool IsActive(string resourceXid)
        {
            lock (m_activeLock)
            {
                return m_activeResources.Contains(resourceXid);
            }
        }

        /// <summary>
        /// Attempts to resolve the executor registered for a binder identity.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
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
        /// <inheritdoc/>
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
            string endpoint = GetExecutableEndpoint(form);
            ServiceResult validation = WotEndpointValidator.Validate(endpoint, m_endpointPolicy, out _);
            if (ServiceResult.IsBad(validation))
            {
                throw new ServiceResultException(validation);
            }

            var context = new WotExecutorContext(m_credentials, m_codecs, m_bounds, m_endpointPolicy, m_telemetry);
            return executor.ActivateAsync(form, context, cancellationToken);
        }

        private static string GetExecutableEndpoint(WotCompiledForm form)
        {
            if (string.Equals(form.Binding.Id, "w3c.http", StringComparison.Ordinal))
            {
                return form.Addressing.Target;
            }
            if (string.Equals(form.Binding.Id, "w3c.modbus", StringComparison.Ordinal) ||
                string.Equals(form.Binding.Id, "w3c.mqtt", StringComparison.Ordinal) ||
                string.Equals(form.Binding.Id, "opc.opcua", StringComparison.Ordinal))
            {
                return form.Endpoint.BaseUri;
            }
            return form.Endpoint.BaseUri;
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
        {
            return m_executorsByKey.ContainsKey(identity.Key) || m_executorsById.ContainsKey(identity.Id);
        }

        /// <summary>
        /// Validates the OPC 10101 §6.5.4 target-mapping terms for a form. The
        /// terms are protocol-neutral (they may target an OPC UA NodeId from a
        /// non-OPC-UA source such as Modbus or HTTP) and are defined on the
        /// owning property affordance, never on a form. Returns <c>false</c> and
        /// adds error diagnostics when the form violates the specification.
        /// </summary>
        private static bool ValidateTargetMapping(WotAffordanceForm form, List<WotBindingDiagnostic> diagnostics)
        {
            bool isValid = true;
            foreach (string term in s_targetMappingTerms)
            {
                if (HasProperty(form.FormElement, term))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.TargetMappingAuthoredOnForm,
                        $"'{term}' is defined by OPC 10101 §6.5.4 on the property affordance, not on a form.",
                        form.Pointer(term), term));
                    isValid = false;
                }
            }

            WotTargetMappingDescriptor mapping = form.TargetMapping;
            if (!HasAnyTargetMappingProperty(form.AffordanceElement))
            {
                return isValid;
            }

            if (form.Kind != WotAffordanceKind.Property)
            {
                string kindName = form.Kind == WotAffordanceKind.Action ? "action" : "event";
                foreach (string term in s_targetMappingTerms)
                {
                    if (HasProperty(form.AffordanceElement, term))
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.TargetMappingNotOnProperty,
                            $"OPC 10101 §6.5.4 defines '{term}' on property affordances only; " +
                            $"'{form.AffordanceName}' is a {kindName} affordance.",
                            form.AffordancePointer(term),
                            term));
                    }
                }
                return false;
            }

            isValid &= ValidateStringTerm(
                form.AffordanceElement, "uav:mapToNodeId", form, diagnostics);
            isValid &= ValidateStringTerm(
                form.AffordanceElement, "uav:mapToType", form, diagnostics);
            isValid &= ValidateStringTerm(
                form.AffordanceElement, "uav:mapByFieldPath", form, diagnostics);

            if (!string.IsNullOrWhiteSpace(mapping.FieldPath) &&
                string.IsNullOrWhiteSpace(mapping.TargetTypeNodeId))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.TargetMappingFieldPathRequiresType,
                    "'uav:mapByFieldPath' is only valid together with 'uav:mapToType' (OPC 10101 §6.5.4).",
                    form.AffordancePointer("uav:mapByFieldPath"), "uav:mapByFieldPath"));
                isValid = false;
            }

            return isValid;
        }

        private static bool HasAnyTargetMappingProperty(JsonElement element)
        {
            foreach (string term in s_targetMappingTerms)
            {
                if (HasProperty(element, term))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasProperty(JsonElement element, string term)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(term, out _);
        }

        private static bool ValidateStringTerm(
            JsonElement element,
            string term,
            WotAffordanceForm form,
            List<WotBindingDiagnostic> diagnostics)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(term, out JsonElement value))
            {
                return true;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.TargetMappingInvalidValue,
                    $"'{term}' must be a string (OPC 10101 §6.5.4).",
                    form.AffordancePointer(term),
                    term));
                return false;
            }
            if (string.IsNullOrWhiteSpace(value.GetString()))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.TargetMappingEmptyValue,
                    $"'{term}' must not be empty (OPC 10101 §6.5.4).",
                    form.AffordancePointer(term), term));
                return false;
            }
            return true;
        }

        private static readonly string[] s_targetMappingTerms =
        [
            "uav:mapToNodeId", "uav:mapToType", "uav:mapByFieldPath"
        ];

        private readonly IWotCredentialProvider m_credentials;
        private readonly IWotCodecRegistry m_codecs;
        private readonly WotBindingBounds m_bounds;
        private readonly WotEndpointPolicy m_endpointPolicy;
        private readonly ITelemetryContext? m_telemetry;

        private readonly Dictionary<string, IWotProtocolBinder> m_binders =
            new(StringComparer.Ordinal);

        private readonly List<IWotProtocolBinder> m_ordered = [];

        private readonly Dictionary<string, IWotBindingExecutor> m_executorsByKey =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, IWotBindingExecutor> m_executorsById =
            new(StringComparer.Ordinal);

        private readonly Lock m_activeLock = new();
        private readonly HashSet<string> m_activeResources = new(StringComparer.Ordinal);
    }
}
