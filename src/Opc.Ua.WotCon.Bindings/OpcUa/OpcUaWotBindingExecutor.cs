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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// Executes OPC UA WoT binding forms compiled by the
    /// <see cref="OpcUaBindingPlanner"/> by connecting an <see cref="ISession"/> to
    /// the target endpoint through the injectable session factory.
    /// </summary>
    public sealed class OpcUaWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>
        /// Initializes a new OPC UA executor.
        /// </summary>
        public OpcUaWotBindingExecutor(OpcUaWotBindingOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("opc.opcua", "10101", OpcUaBindingPlanner.BindingUri, "OPC UA WoT Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
        {
            return form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public async ValueTask<IWotBindingChannel> ActivateAsync(
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
            if (m_options.ConstrainedSessionFactory is null &&
                m_options.SessionFactory is null &&
                !HasBuiltInSelection(m_options))
            {
                throw new InvalidOperationException(
                    "No OPC UA session factory is configured on the executor options.");
            }
            string endpoint = BuildEndpoint(form.Endpoint);
            WotSecurityFloor? floor = form.SecurityFloor;
            ISession session = await ConnectAsync(
                endpoint, floor, form, cancellationToken).ConfigureAwait(false);
            try
            {
                EnforceSecurityFloor(session, form, floor);
            }
            catch (ServiceResultException)
            {
                if (m_options.DisposeSession)
                {
                    session.Dispose();
                }
                throw;
            }
            return new OpcUaWotBindingChannel(session, m_options.DisposeSession, form, context, m_options);
        }

        /// <summary>
        /// Opens the session a compiled form needs, applying the endpoint
        /// selection of WoT Binding Section 5.7.1 where the form states a
        /// security floor.
        /// </summary>
        /// <remarks>
        /// A floor constrains a <em>choice</em>, so it has to be applied where
        /// the choice is made. Three configurations can make one:
        /// <see cref="OpcUaWotBindingOptions.ConstrainedSessionFactory"/>, where
        /// the caller's factory receives the floor and applies it; the built-in
        /// path, where the caller supplies discovery and an endpoint-taking
        /// factory and <see cref="OpcUaWotEndpointSelector"/> makes the
        /// deterministic choice here; and no floor at all, where the
        /// endpoint-blind legacy factory is exactly right. A floor with only the
        /// legacy factory is the one combination that cannot work, and it fails
        /// with a message naming what to configure rather than opening a session
        /// and rejecting whatever endpoint the factory happened to pick - a
        /// false negative that reads as "no endpoint is strong enough" even when
        /// the Server offers one.
        /// </remarks>
        private async ValueTask<ISession> ConnectAsync(
            string endpoint,
            WotSecurityFloor? floor,
            WotCompiledForm form,
            CancellationToken cancellationToken)
        {
            var request = new OpcUaWotSessionRequest(endpoint, floor, form.AffordanceName);
            if (m_options.ConstrainedSessionFactory is not null)
            {
                return await m_options.ConstrainedSessionFactory(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            bool constrained = floor is not null && !floor.IsEmpty;
            if (constrained && HasBuiltInSelection(m_options))
            {
                return await SelectAndConnectAsync(request, form, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (constrained && m_options.SessionFactory is not null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    $"The '{form.AffordanceName}' form states the security floor {floor}, but the " +
                    "executor is configured only with the endpoint-blind SessionFactory, which " +
                    "cannot discard an endpoint below the floor before connecting to it. " +
                    "Configure ConstrainedSessionFactory, or EndpointDiscovery together with " +
                    "SelectedEndpointSessionFactory, so the floor is applied where the endpoint " +
                    "is chosen (WoT Binding Section 5.7.1).");
            }
            if (m_options.SessionFactory is not null)
            {
                return await m_options.SessionFactory(endpoint, cancellationToken)
                    .ConfigureAwait(false);
            }
            return await SelectAndConnectAsync(request, form, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Applies the deterministic selection of WoT Binding Section 5.7.1 to
        /// the discovered endpoints and connects to the one it names.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown when no discovered endpoint satisfies the floor. A client
        /// <em>shall</em> fail and report rather than fall back below a stated
        /// floor.
        /// </exception>
        private async ValueTask<ISession> SelectAndConnectAsync(
            OpcUaWotSessionRequest request,
            WotCompiledForm form,
            CancellationToken cancellationToken)
        {
            ArrayOf<EndpointDescription> discovered = await m_options
                .EndpointDiscovery!(request.EndpointUrl, cancellationToken)
                .ConfigureAwait(false);
            EndpointDescription? selected = OpcUaWotEndpointSelector.Select(
                discovered, request.MinimumSecurity);
            if (selected is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    $"The '{form.AffordanceName}' form states the security floor " +
                    $"{request.MinimumSecurity}, and none of the " +
                    $"{(discovered.IsNull ? 0 : discovered.Count)} endpoints " +
                    $"'{request.EndpointUrl}' offers is at or above it. A client shall fail and " +
                    "report rather than fall back below a stated floor " +
                    "(WoT Binding Section 5.7.1).");
            }
            return await m_options
                .SelectedEndpointSessionFactory!(selected, request, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets whether the options carry the pair the built-in deterministic
        /// endpoint selection needs.
        /// </summary>
        private static bool HasBuiltInSelection(OpcUaWotBindingOptions options)
        {
            return options.EndpointDiscovery is not null &&
                options.SelectedEndpointSessionFactory is not null;
        }

        /// <summary>
        /// Verifies that the session a factory returned sits at or above the
        /// security floor the document states (WoT Binding Section 5.7.1).
        /// </summary>
        /// <remarks>
        /// The factory chooses the endpoint, so the floor is enforced by
        /// checking the endpoint the session reports rather than by trusting
        /// the factory to have applied it: a client <em>shall not</em> silently
        /// fall back below a floor, and a floor whose enforcement this executor
        /// merely assumed would be a claim rather than a guarantee. A session
        /// that cannot state its endpoint at all fails the same way, because an
        /// endpoint that cannot be inspected cannot be shown to satisfy
        /// anything.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when the selected endpoint is below the floor, or when it
        /// cannot be inspected.
        /// </exception>
        private static void EnforceSecurityFloor(
            ISession session, WotCompiledForm form, WotSecurityFloor? floor)
        {
            if (floor is null || floor.IsEmpty)
            {
                return;
            }
            EndpointDescription? description = session.ConfiguredEndpoint?.Description;
            if (description is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    $"The '{form.AffordanceName}' form states the security floor {floor}, but the " +
                    "session does not report the endpoint it selected, so the floor cannot be " +
                    "shown to hold.");
            }
            if (!OpcUaWotEndpointSelector.Satisfies(description, floor))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    $"The '{form.AffordanceName}' form states the security floor {floor}, but the " +
                    $"session selected '{description.EndpointUrl}' with security mode " +
                    $"{description.SecurityMode} and policy " +
                    $"'{description.SecurityPolicyUri}', which is below it. A client shall fail " +
                    "and report rather than fall back below a stated floor " +
                    "(WoT Binding Section 5.7.1).");
            }
        }

        private static string BuildEndpoint(WotEndpointDescriptor endpoint)
        {
            if (!string.IsNullOrEmpty(endpoint.BaseUri))
            {
                return endpoint.BaseUri;
            }
            string authority = FormatHost(endpoint.Host);
            int defaultPort = GetDefaultPort(endpoint.Scheme);
            if (endpoint.Port >= 0 && endpoint.Port != defaultPort)
            {
                authority += ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);
            }
            return endpoint.Scheme + "://" + authority;
        }

        private static string FormatHost(string? host)
        {
            if (string.IsNullOrEmpty(host) ||
                host[0] == '[' ||
                !host.Contains(':', StringComparison.Ordinal))
            {
                return host ?? string.Empty;
            }
            return "[" + host + "]";
        }

        private static int GetDefaultPort(string scheme)
        {
            if (string.Equals(scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase))
            {
                return 4840;
            }
            if (string.Equals(scheme, "opc.https", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scheme, "opc.wss", StringComparison.OrdinalIgnoreCase))
            {
                return 443;
            }
            return -1;
        }

        private readonly OpcUaWotBindingOptions m_options;
    }
}
