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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Validates a push device's actual channel certificate against its signed tickets.
    /// Discovery certificates and application descriptions do not establish device identity.
    /// </summary>
    public sealed class OnboardingPushIdentityValidator
    {
        /// <summary>
        /// Borrows the registrar's existing ticket-scoped identity selector and telemetry.
        /// </summary>
        public OnboardingPushIdentityValidator(
            IOnboardingIdentitySelector identities,
            ITelemetryContext telemetry)
        {
            m_identities = identities ?? throw new ArgumentNullException(nameof(identities));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Reads RequestTickets through a generation-bound encrypted session and validates its actual peer.
        /// This does not grant global channel trust, register an application, install credentials, or queue work.
        /// The host must keep later push operations on this validated channel generation or validate again.
        /// </summary>
        public async ValueTask<OnboardingIdentitySelection> ValidateAsync(
            ISession session,
            NodeId provisionableDeviceId,
            CancellationToken cancellationToken = default)
        {
            OnboardingPushSession admitted = await CaptureAsync(
                session, provisionableDeviceId, cancellationToken).ConfigureAwait(false);
            await using (admitted.ConfigureAwait(false))
            {
                return admitted.Identity;
            }
        }

        /// <summary>
        /// Validates the device and retains its encrypted session generation for subsequent push operations.
        /// Use only the returned session for those operations. Disposing it does not close the owner's connection.
        /// </summary>
        public async ValueTask<OnboardingPushSession> CaptureAsync(
            ISession session,
            NodeId provisionableDeviceId,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (provisionableDeviceId.IsNull)
            {
                throw new ArgumentException("A provisionable device identifier is required.",
                    nameof(provisionableDeviceId));
            }
            SessionRequestContext dispatch = await SessionRequestContext.CaptureConfidentialAsync(
                session, cancellationToken).ConfigureAwait(false);
            try
            {
                var device = new ProvisionableDeviceTypeClient(
                    dispatch.Session, provisionableDeviceId, m_telemetry);
                ArrayOf<string> tickets = await device.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                OnboardingIdentitySelection selection = await ValidatePeerAsync(
                    dispatch.Session.TransportChannel, tickets, cancellationToken).ConfigureAwait(false);
                if (dispatch.Invalidated.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                return new OnboardingPushSession(dispatch, selection);
            }
            catch
            {
                await dispatch.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Uses only the transport's authenticated certificate, never the discovery endpoint's certificate.
        /// </summary>
        internal async ValueTask<OnboardingIdentitySelection> ValidatePeerAsync(
            ITransportChannel channel,
            ArrayOf<string> tickets,
            CancellationToken cancellationToken = default)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (channel.EndpointDescription.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(channel.EndpointDescription.SecurityPolicyUri) ||
                channel.EndpointDescription.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            byte[] peer = channel.ServerChannelCertificate;
            if (peer == null || peer.Length == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid, "The authenticated device channel has no peer certificate.");
            }
            ByteString capturedPeer = ByteString.From(peer.AsSpan());
            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(capturedPeer.Memory);
            if (chain.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            ByteString leaf = ByteString.From(chain[0].RawData.AsSpan());
            var issuers = new ByteString[chain.Count - 1];
            for (int i = 1; i < chain.Count; i++)
            {
                issuers[i - 1] = ByteString.From(chain[i].RawData.AsSpan());
            }
            OnboardingIdentitySelection selection = await m_identities.SelectAsync(
                [leaf], issuers, tickets.ToArray().ToArrayOf(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (selection == null || selection.SelectedIdentity != leaf || selection.IsExternalAuthentication)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            if (!capturedPeer.Span.SequenceEqual(channel.ServerChannelCertificate))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecureChannelIdInvalid, "The device channel changed during identity validation.");
            }
            return selection;
        }

        /// <summary>The host's shared ticket and device-identity validation policy.</summary>
        private readonly IOnboardingIdentitySelector m_identities;

        /// <summary>The host's telemetry for generated device method proxies.</summary>
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Retains a validated push device's identity and the encrypted generation used to read its tickets.
    /// Requests through this session never follow a replacement connection or changed session identity.
    /// </summary>
    public sealed class OnboardingPushSession : IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership of the local request context, never the original connection.
        /// </summary>
        internal OnboardingPushSession(SessionRequestContext dispatch, OnboardingIdentitySelection identity)
        {
            m_dispatch = dispatch;
            Identity = identity;
        }

        /// <summary>
        /// Gets the identity selected from the authenticated peer and its tickets at admission.
        /// This historical evidence does not authorize requests after the session is invalidated.
        /// </summary>
        public OnboardingIdentitySelection Identity { get; }

        /// <summary>
        /// Gets the generation-bound session for generated certificate and TrustList clients.
        /// Each dispatch checks the original authentication token, namespace tables and physical channel.
        /// </summary>
        public ISession Session
        {
            get
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(OnboardingPushSession));
                }
                if (Invalidated.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                return m_dispatch.Session;
            }
        }

        /// <summary>
        /// Completes when the borrowed generation changes, recovery starts, or this local context is disposed.
        /// </summary>
        public Task Invalidated => m_dispatch.Invalidated;

        /// <summary>
        /// Releases the local request context without closing the caller's session or transport.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                await m_dispatch.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>The locally owned generation-bound request context.</summary>
        private readonly SessionRequestContext m_dispatch;

        /// <summary>Whether local disposal has begun.</summary>
        private int m_disposed;
    }
}
