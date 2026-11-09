/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Host-selected endpoint, registrar credentials and namespace-stable device addresses.
    /// The endpoint certificate is discovery metadata, not evidence that a device owns an identity.
    /// </summary>
    public sealed record DeviceRegistrarPushTarget(
        ConfiguredEndpoint Endpoint,
        IUserIdentity UserIdentity,
        ExpandedNodeId ProvisionableDeviceId,
        ExpandedNodeId IsSingletonId,
        ExpandedNodeId ServerConfigurationId,
        ExpandedNodeId CertificateGroupId,
        ExpandedNodeId CertificateTypeId,
        ExpandedNodeId TrustListId);

    /// <summary>
    /// Creates a fresh stack-validated session for each push attempt and installed-certificate proof.
    /// The host selects an authorized endpoint and credentials; this provider never changes global trust.
    /// </summary>
    public sealed class SessionDeviceRegistrarPushConnectionProvider : IDeviceRegistrarPushConnectionProvider
    {
        /// <summary>
        /// Creates an owned-session adapter using the existing session factory and explicit target selection.
        /// Trust anchors and registrar application credentials must already be configured by the host.
        /// The supplied factory must create a fresh, exclusively owned session for every call, never a cached
        /// or borrowed session. Each returned session is permanently retired when its lease is disposed.
        /// </summary>
        public SessionDeviceRegistrarPushConnectionProvider(
            ApplicationConfiguration configuration,
            ISessionFactory sessionFactory,
            Func<ApplicationDescription, CancellationToken, ValueTask<DeviceRegistrarPushTarget>> selectTarget)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (configuration.SecurityConfiguration == null ||
                configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Push connections require explicit certificate trust.");
            }
            m_configuration = new ApplicationConfiguration(configuration)
            {
                ClientConfiguration = configuration.ClientConfiguration ?? new ClientConfiguration(),
                CertificateManager = configuration.CertificateManager
            };
            m_factory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_selectTarget = selectTarget ?? throw new ArgumentNullException(nameof(selectTarget));
        }

        /// <inheritdoc/>
        public async ValueTask<IDeviceRegistrarPushConnection> ConnectAsync(
            ApplicationDescription application,
            ByteString expectedCertificate,
            CancellationToken cancellationToken = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationDescription snapshot = CoreUtils.Clone(application)!;
            string? applicationUri = snapshot.ApplicationUri;
            ByteString expected = ByteString.From(expectedCertificate.Span);
            DeviceRegistrarPushTarget target = await m_selectTarget(snapshot, cancellationToken)
                .ConfigureAwait(false) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Push connections require explicit certificate trust.");
            }
            if (target.Endpoint == null || target.UserIdentity == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            var endpoint = new ConfiguredEndpoint(
                null, CoreUtils.Clone(target.Endpoint.Description)!, CoreUtils.Clone(target.Endpoint.Configuration)!);
            EndpointDescription offered = endpoint.Description;
            if (offered.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(offered.SecurityPolicyUri) || offered.SecurityPolicyUri == SecurityPolicies.None ||
                string.IsNullOrEmpty(applicationUri) ||
                !string.Equals(offered.Server?.ApplicationUri, applicationUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The selected endpoint does not identify the encrypted target.");
            }
            ISession session = await m_factory.CreateAsync(
                m_configuration, endpoint, updateBeforeConnect: false, checkDomain: true,
                m_configuration.ApplicationName ?? nameof(SessionDeviceRegistrarPushConnectionProvider),
                60000, target.UserIdentity, default, cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            try
            {
                SessionRequestContext context =
                    await SessionRequestContext.CaptureConfidentialAsync(session, cancellationToken)
                        .ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    EndpointDescription actual = context.Session.TransportChannel.EndpointDescription;
                    if (actual.SecurityMode != offered.SecurityMode ||
                        actual.SecurityPolicyUri != offered.SecurityPolicyUri ||
                        actual.EndpointUrl != offered.EndpointUrl ||
                        actual.Server?.ApplicationUri != applicationUri)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                    byte[] peerBytes = context.Session.TransportChannel.ServerChannelCertificate;
                    if (peerBytes == null || peerBytes.Length == 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                    }
                    using CertificateCollection chain =
                        DefaultCertificateFactory.Instance.ParseChainBlob(peerBytes.AsMemory());
                    ByteString peer = chain.Count == 0 ? ByteString.Empty : ByteString.From(chain[0].RawData.AsSpan());
                    if (peer.IsEmpty || (!expected.IsEmpty && peer != expected))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadCertificateInvalid,
                            "The actual server certificate does not prove installation.");
                    }
                    return new Connection(
                        session, offered,
                        Resolve(target.ProvisionableDeviceId, session.NamespaceUris),
                        Resolve(target.IsSingletonId, session.NamespaceUris),
                        Resolve(target.ServerConfigurationId, session.NamespaceUris),
                        Resolve(target.CertificateGroupId, session.NamespaceUris),
                        Resolve(target.CertificateTypeId, session.NamespaceUris),
                        Resolve(target.TrustListId, session.NamespaceUris));
                }
            }
            catch (Exception connectionError)
            {
                try
                {
                    await CloseAsync(session).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(connectionError, cleanupError);
                }
                throw;
            }
        }

        /// <summary>
        /// Resolves only local addresses with a namespace URI or the standard namespace zero.
        /// </summary>
        private static NodeId Resolve(ExpandedNodeId address, NamespaceTable namespaces)
        {
            if (address.IsNull || address.ServerIndex != 0 ||
                (address.NamespaceIndex != 0 && string.IsNullOrEmpty(address.NamespaceUri)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "Push model addresses require stable local namespace identifiers.");
            }
            NodeId resolved = ExpandedNodeId.ToNodeId(address, namespaces);
            if (resolved.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return resolved;
        }

        /// <summary>
        /// Closes an owned session with bounded cleanup and preserves both service and disposal failures.
        /// </summary>
        internal static async ValueTask CloseAsync(ISession session)
        {
            Exception? failure = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                StatusCode status = await session.CloseAsync(5000, true, timeout.Token).ConfigureAwait(false);
                if (StatusCode.IsBad(status))
                {
                    throw new ServiceResultException(status);
                }
            }
            catch (Exception closeError)
            {
                failure = closeError;
            }
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposeError)
            {
                failure = failure == null ? disposeError : new AggregateException(failure, disposeError);
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Owns only the fresh session created for this attempt; no container-owned session is borrowed.
        /// </summary>
        private sealed class Connection(
            ISession session,
            EndpointDescription offeredEndpoint,
            NodeId provisionableDeviceId,
            NodeId isSingletonId,
            NodeId serverConfigurationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            NodeId trustListId) : IDeviceRegistrarPushConnection
        {
            /// <inheritdoc/>
            public ISession Session { get; } = session;

            /// <inheritdoc/>
            public EndpointDescription OfferedEndpoint { get; } = offeredEndpoint;

            /// <inheritdoc/>
            public NodeId ProvisionableDeviceId { get; } = provisionableDeviceId;

            /// <inheritdoc/>
            public NodeId IsSingletonId { get; } = isSingletonId;

            /// <inheritdoc/>
            public NodeId ServerConfigurationId { get; } = serverConfigurationId;

            /// <inheritdoc/>
            public NodeId CertificateGroupId { get; } = certificateGroupId;

            /// <inheritdoc/>
            public NodeId CertificateTypeId { get; } = certificateTypeId;

            /// <inheritdoc/>
            public NodeId TrustListId { get; } = trustListId;

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return Interlocked.Exchange(ref m_disposed, 1) == 0 ? CloseAsync(Session) : default;
            }

            /// <summary>Indicates that this owned session has already been released.</summary>
            private int m_disposed;
        }

        /// <summary>Client configuration sharing the host's certificate manager without changing trust.</summary>
        private readonly ApplicationConfiguration m_configuration;

        /// <summary>Existing stack transport and authenticated-session factory.</summary>
        private readonly ISessionFactory m_factory;

        /// <summary>Explicit host selection of authorized targets and registrar credentials.</summary>
        private readonly Func<ApplicationDescription, CancellationToken, ValueTask<DeviceRegistrarPushTarget>>
            m_selectTarget;
    }
}
