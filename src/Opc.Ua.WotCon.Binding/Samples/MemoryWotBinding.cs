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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding.Samples
{
    /// <summary>
    /// A worked sample showing how a third party contributes a replaceable
    /// protocol binder as pure code-behind. The fictitious <c>mem</c> protocol
    /// binds property affordances to an in-process key/value store, demonstrating
    /// the full extension surface: identity, capability, deterministic
    /// identification, a planner and an executor with a live channel. Register it
    /// with <c>builder.AddWotBinder(new MemoryWotBinder())</c> and
    /// <c>builder.AddWotBindingExecutor(new MemoryWotBindingExecutor(store))</c>.
    /// </summary>
    public sealed class MemoryWotBinder : WotProtocolBinderBase
    {
        /// <summary>The sample binding vocabulary URI.</summary>
        public const string BindingUri = "urn:example:wot:mem";

        private static readonly string[] s_schemes = { "mem" };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("example.mem", "1.0", BindingUri, "Sample In-Memory Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "Sample In-Memory Binding",
            new WotBindingSource("urn:example:wot:mem", "1.0", WotBindingMaturity.UnofficialDraft,
                note: "A sample custom binding for documentation and tests."),
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty
            },
            new[] { "application/json", "text/plain" },
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "memv:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href) ||
                !TryParseUri(href, out Uri uri) ||
                !string.Equals(uri.Scheme, "mem", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid mem:// URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string key = uri.AbsolutePath.Trim('/');
            ResolveCodec(form, context, out WotPayloadDescriptor payload);
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(key);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, capability.ToString());
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload,
                    ImmutableArray<WotCredentialReference>.Empty, Capability.IsExecutable));
            }

            return entries.Count == 0
                ? WotBindingCompilation.Unsupported(diagnostics.ToArray())
                : WotBindingCompilation.Supported(entries.ToImmutable(), diagnostics.ToImmutableArray());
        }
    }

    /// <summary>The in-process key/value store the sample binding reads and writes.</summary>
    public sealed class MemoryWotStore
    {
        /// <summary>Gets the value stored under a key.</summary>
        public DataValue Get(string key)
            => m_values.TryGetValue(key, out DataValue value) ? value : new DataValue(Variant.Null);

        /// <summary>Sets the value stored under a key.</summary>
        public void Set(string key, DataValue value) => m_values[key] = value;

        private readonly ConcurrentDictionary<string, DataValue> m_values =
            new ConcurrentDictionary<string, DataValue>(StringComparer.Ordinal);
    }

    /// <summary>The executor for the sample in-memory binding.</summary>
    public sealed class MemoryWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>Initializes a new sample executor over the supplied store.</summary>
        public MemoryWotBindingExecutor(MemoryWotStore store)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("example.mem", "1.0", MemoryWotBinder.BindingUri, "Sample In-Memory Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
            => form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The channel is owned by the caller, who disposes it via DisposeAsync.")]
        public ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            IWotBindingChannel channel = new MemoryWotBindingChannel(m_store, form);
            return new ValueTask<IWotBindingChannel>(channel);
        }

        private readonly MemoryWotStore m_store;
    }

    /// <summary>The live channel for the sample in-memory binding.</summary>
    internal sealed class MemoryWotBindingChannel : IWotBindingChannel
    {
        public MemoryWotBindingChannel(MemoryWotStore store, WotCompiledForm form)
        {
            m_store = store;
            m_form = form;
            m_key = form.Addressing.Target;
        }

        public WotCompiledForm Form => m_form;

        public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => new ValueTask<WotReadResult>(new WotReadResult(StatusCodes.Good, m_store.Get(m_key)));

        public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
        {
            m_store.Set(m_key, value);
            return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
            => new ValueTask<WotInvokeResult>(new WotInvokeResult(
                StatusCodes.BadNotSupported, null, "The sample binding has no actions."));

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller, who disposes it.")]
        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            var subscription = new PollingWotSubscription(m_form, token =>
            {
                onNotification(new WotNotification(m_store.Get(m_key)));
                return default;
            }, TimeSpan.FromMilliseconds(200));
            return new ValueTask<IWotSubscription>(subscription);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            => ObserveAsync(onEvent, cancellationToken);

        public ValueTask DisposeAsync() => default;

        private readonly MemoryWotStore m_store;
        private readonly WotCompiledForm m_form;
        private readonly string m_key;
    }
}
