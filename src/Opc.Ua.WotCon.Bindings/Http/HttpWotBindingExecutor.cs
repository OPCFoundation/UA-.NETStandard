/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Http
{
    /// <summary>
    /// Executes HTTP / HTTPS WoT binding forms compiled by the
    /// <see cref="HttpBindingPlanner"/>. It opens a per-form
    /// <see cref="HttpWotBindingChannel"/> using an injectable
    /// <see cref="HttpClient"/> factory.
    /// </summary>
    public sealed class HttpWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>
        /// Initializes a new HTTP executor.
        /// </summary>
        public HttpWotBindingExecutor(HttpWotBindingOptions? options = null)
        {
            m_options = options ?? new HttpWotBindingOptions();
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.http", "1.1", HttpBindingPlanner.BindingUri, "W3C WoT HTTP Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
        {
            if (form is null)
            {
                return false;
            }
            return string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal) &&
                (string.Equals(form.Endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(form.Endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Opens a live channel for the supplied compiled form.
        /// </summary>
        /// <inheritdoc/>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="context">The WoT executor context.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The client and channel are owned by the returned channel, disposed via DisposeAsync.")]
        public ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            bool registered = context.Codecs.TrySelect(form.Payload.ContentType, out IWotPayloadCodec codec);
            if (form.AffordanceKind != WotAffordanceKind.Property &&
                (!registered || !string.Equals(codec.Id, form.Payload.CodecId, StringComparison.Ordinal)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The compiled HTTP payload codec is not registered.");
            }
            ImmutableArray<KeyValuePair<string, string>> defaultHeaders =
                SnapshotDefaultHeaders(m_options.DefaultHeaders);
            Func<HttpClient>? clientFactory = m_options.ClientFactory;
            bool ownsClient = clientFactory is null;
            if (!ownsClient && !m_options.CallerClientHandlesRedirectSafety)
            {
                // Fail closed: the executor cannot control a caller-supplied client's
                // redirect handler, cookie jar, DefaultRequestHeaders or later
                // mutations. Require explicit confirmation even when the current form
                // and options appear not to carry credentials.
                throw new InvalidOperationException(
                    "Every caller-supplied HttpClient requires " +
                    "HttpWotBindingOptions.CallerClientHandlesRedirectSafety to be set. The supplied client must " +
                    "disable automatic redirects, or follow them without forwarding cookies or credentials " +
                    "across origins, " +
                    "because its handler, cookie behavior, mutable DefaultRequestHeaders and later mutations " +
                    "are outside the executor's control.");
            }
            HttpClient client = ownsClient
                ? new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    CheckCertificateRevocationList = true
                })
                : clientFactory!.Invoke();
            IWotBindingChannel channel = new HttpWotBindingChannel(
                client, ownsClient, manualRedirects: ownsClient, defaultHeaders, form, context, m_options, codec);
            return new ValueTask<IWotBindingChannel>(channel);
        }

        private static ImmutableArray<KeyValuePair<string, string>> SnapshotDefaultHeaders(
            IReadOnlyDictionary<string, string>? defaultHeaders)
        {
            return defaultHeaders is null
                ? []
                : ImmutableArray.CreateRange(defaultHeaders);
        }

        private readonly HttpWotBindingOptions m_options;
    }
}
