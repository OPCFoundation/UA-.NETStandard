/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The default session factory that creates <see cref="ManagedSession"/>
    /// instances which automatically handle reconnection and failover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <c>CreateAsync</c> overload creates a raw
    /// <see cref="Session"/> via an inner <see cref="ClassicSessionFactory"/>
    /// and wraps it in a <see cref="ManagedSession"/>.
    /// </para>
    /// <para>
    /// Use <see cref="ClassicSessionFactory"/> directly if you need raw
    /// <see cref="Session"/> instances without automatic reconnection.
    /// </para>
    /// </remarks>
    public class DefaultSessionFactory : ISessionFactory
    {
        private readonly ClassicSessionFactory m_innerFactory;

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; init; }

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => m_innerFactory.ReturnDiagnostics;
            set => m_innerFactory.ReturnDiagnostics = value;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="DefaultSessionFactory"/>.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use.</param>
        public DefaultSessionFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            m_innerFactory = new ClassicSessionFactory(telemetry);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return await CreateManagedSessionAsync(
                configuration,
                endpoint,
                identity,
                sessionName,
                sessionTimeout,
                preferredLocales,
                checkDomain: false,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return await CreateManagedSessionAsync(
                configuration,
                endpoint,
                identity,
                sessionName,
                sessionTimeout,
                preferredLocales,
                checkDomain,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return await CreateManagedSessionAsync(
                configuration,
                endpoint,
                identity,
                sessionName,
                sessionTimeout,
                preferredLocales,
                checkDomain,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? userIdentity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return await CreateManagedSessionAsync(
                configuration,
                endpoint,
                userIdentity,
                sessionName,
                sessionTimeout,
                preferredLocales,
                checkDomain,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            return m_innerFactory.CreateChannelAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                ct);
        }

        /// <inheritdoc/>
        public virtual Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(sessionTemplate, ct);
        }

        /// <inheritdoc/>
        public virtual Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(
                sessionTemplate, connection, ct);
        }

        /// <inheritdoc/>
        public virtual Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(
                sessionTemplate, transportChannel, ct);
        }

        /// <inheritdoc/>
        public virtual ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2? clientCertificate = null,
            X509Certificate2Collection? clientCertificateChain = null,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default)
        {
            return m_innerFactory.Create(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                clientCertificateChain,
                availableEndpoints,
                discoveryProfileUris);
        }

        private Task<ManagedSession> CreateManagedSessionAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            IUserIdentity? identity,
            string sessionName,
            uint sessionTimeout,
            ArrayOf<string> preferredLocales,
            bool checkDomain,
            CancellationToken ct)
        {
            return ManagedSession.CreateAsync(
                configuration,
                endpoint,
                m_innerFactory,
                identity,
                telemetry: Telemetry,
                sessionName: sessionName,
                sessionTimeout: sessionTimeout,
                preferredLocales: preferredLocales,
                checkDomain: checkDomain,
                ct: ct);
        }
    }
}
