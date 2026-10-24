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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adds native application/key binding to normal UASC certificate validation.
    /// A trusted forwarding application is not the authenticated native peer.
    /// </summary>
    internal sealed class DataChannelClientServerSecurity : ICertificateValidatorEx
    {
        /// <summary>
        /// Borrows the context and normal application validator.
        /// </summary>
        internal DataChannelClientServerSecurity(DataChannelContext context, ICertificateValidatorEx validator)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <inheritdoc/>
        public Func<Certificate, ServiceResult, bool>? AcceptError
        {
            get => m_validator.AcceptError;
            set => throw new NotSupportedException("Configure trust on the borrowed application certificate manager.");
        }

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            Security.Certificates.CertificateValidationOptions? options = null,
            CancellationToken ct = default)
        {
            if (chain == null || chain.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            CertificateValidationResult result =
                await m_validator.ValidateAsync(chain, trustList, options, ct).ConfigureAwait(false);
            if (result.IsValid)
            {
                ValidatePeer(chain[0]);
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<CertificateValidationResult> ValidateAsync(
            Certificate certificate,
            TrustListIdentifier? trustList = null,
            CancellationToken ct = default)
        {
            CertificateValidationResult result =
                await m_validator.ValidateAsync(certificate, trustList, ct).ConfigureAwait(false);
            if (result.IsValid)
            {
                ValidatePeer(certificate);
            }
            return result;
        }

        /// <summary>
        /// Requires a ready, confidential native association, independently of inner security.
        /// </summary>
        internal static void ValidateContext(DataChannelContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (context.CarrierKind is not (DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport) ||
                !context.IsReady || context.Identity.Peer.Protection != DataChannelProtectionMode.Confidential)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A confirmed confidential native context is required.");
            }
        }

        /// <summary>
        /// Validates an ordinary virtual Service endpoint, not an outer data-only advertisement.
        /// This strict composition requires signed UASC; TLS confidentiality does not select its policy.
        /// </summary>
        internal static void ValidateEndpoint(Uri endpointUrl, EndpointDescription? description)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }
            if (description == null || description.TransportProfileUri != Profiles.UaTcpTransport ||
                description.EndpointUrl != endpointUrl.AbsoluteUri ||
                UascDataChannelEndpoint.IsFlowResource(endpointUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "An explicit ordinary UACP Service endpoint is required for the lane.");
            }
            UascDataChannelEndpoint.ValidateProtection(
                description.SecurityMode, description.SecurityPolicyUri, allowIntegrity: true);
        }

        /// <summary>
        /// Requires the certificate's application URI and SPKI to match validated carrier evidence.
        /// </summary>
        internal static void ValidateIdentity(Certificate? certificate, string applicationUri, ByteString key)
        {
            if (certificate == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            UascDataChannelEndpoint.GetApplicationUri(certificate, applicationUri);
            if (DataChannelCertificateIdentity.GetKeyThumbprint(certificate) != key)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The inner SecureChannel certificate is not the authenticated native application's key.");
            }
        }

        /// <summary>
        /// Checks continuing context validity as well as the native peer binding.
        /// </summary>
        private void ValidatePeer(Certificate certificate)
        {
            ValidateContext(m_context);
            DataChannelPeerIdentity peer = m_context.Identity.Peer;
            ValidateIdentity(certificate, peer.PeerApplicationUri, peer.PeerKeyThumbprint);
        }

        private readonly DataChannelContext m_context;
        private readonly ICertificateValidatorEx m_validator;
    }
}
