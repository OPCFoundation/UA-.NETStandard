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
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Binding.Planners;

namespace Opc.Ua.WotCon.Binding.Http
{
    /// <summary>
    /// Executes HTTP / HTTPS WoT binding forms compiled by the
    /// <see cref="HttpBindingPlanner"/>. It opens a per-form
    /// <see cref="HttpWotBindingChannel"/> using an injectable
    /// <see cref="HttpClient"/> factory.
    /// </summary>
    public sealed class HttpWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>Initializes a new HTTP executor.</summary>
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

        /// <inheritdoc/>
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
            bool ownsClient = m_options.ClientFactory is null;
            if (!ownsClient &&
                FormCarriesCredentials(form) &&
                !m_options.CallerClientHandlesRedirectSafety)
            {
                // Fail closed: the executor cannot control a caller-supplied client's
                // redirect behavior, so a credential-bearing form could leak its
                // custom header / query credentials if that client auto-redirects
                // across origins. Require the caller to explicitly confirm the client
                // handles redirects safely.
                throw new InvalidOperationException(
                    "A caller-supplied HttpClient cannot execute a credential-bearing HTTP form unless " +
                    "HttpWotBindingOptions.CallerClientHandlesRedirectSafety is set. The supplied client must " +
                    "disable automatic redirects, or follow them without forwarding credentials across origins, " +
                    "to avoid leaking custom header / query credentials on a redirect.");
            }
            HttpClient client = ownsClient
                ? new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CheckCertificateRevocationList = true
                })
                : m_options.ClientFactory!.Invoke();
            IWotBindingChannel channel = new HttpWotBindingChannel(
                client, ownsClient, manualRedirects: ownsClient, form, context, m_options);
            return new ValueTask<IWotBindingChannel>(channel);
        }

        private static bool FormCarriesCredentials(WotCompiledForm form)
            => !form.Security.IsDefaultOrEmpty &&
               form.Security.Any(reference => reference.Scheme != WotSecurityScheme.NoSecurity);

        private readonly HttpWotBindingOptions m_options;
    }
}
