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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Restricts a pre-acquired JWT to supported, confidential policies and a trusted server.
    /// </summary>
    internal sealed class McpJwtIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Wraps the existing issued-token provider without interpreting unverified JWT claims.
        /// </summary>
        public McpJwtIdentityProvider(
            string jwt, ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(telemetry);
            ValidateToken(jwt, configuration.TransportQuotas);
            m_configuration = configuration;
            m_telemetry = telemetry;
            m_provider = new IssuedTokenIdentityProvider(
                JwtBearerAccessTokenProvider.FromJwtString(string.Empty, jwt, DateTime.MaxValue));
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes => m_provider.SupportedTokenTypes;

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris => m_provider.SupportedIssuedTokenProfileUris;

        /// <inheritdoc/>
        public DateTime ExpiresAt => DateTime.MaxValue;

        /// <inheritdoc/>
        public ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy, IdentitySelectionContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (policy == null ||
                policy.TokenType != UserTokenType.IssuedToken ||
                !string.Equals(policy.IssuedTokenType, Profiles.JwtUserToken, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(CanSatisfyResult.No("A JWT issued-token policy is required."));
            }

            EndpointDescription endpoint = context.EndpointDescription;
            string effectivePolicy = string.IsNullOrEmpty(policy.SecurityPolicyUri)
                ? endpoint.SecurityPolicyUri ?? SecurityPolicies.None
                : policy.SecurityPolicyUri;
            ISecurityPolicyRegistry registry = context.SecurityPolicyRegistry ?? SecurityPolicies.Default;
            SecurityPolicyInfo? info = registry.GetInfo(effectivePolicy);
            if (info == null ||
                (effectivePolicy != SecurityPolicies.None && !info.MeetsMinimumSecurityRequirements) ||
                (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                    (effectivePolicy == SecurityPolicies.None ||
                        (info.AsymmetricEncryptionAlgorithm == AsymmetricEncryptionAlgorithm.None &&
                            info.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None))))
            {
                return ValueTask.FromResult(CanSatisfyResult.No(
                    "The JWT requires a supported encrypted channel or user-token policy."));
            }

            if (endpoint.SecurityMode is not (MessageSecurityMode.None or
                MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) ||
                endpoint.ServerCertificate.IsNull || endpoint.ServerCertificate.Length == 0)
            {
                return ValueTask.FromResult(CanSatisfyResult.No("A server application certificate is required."));
            }

            return m_provider.CanSatisfyAsync(policy, context, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy, IdentitySelectionContext context, CancellationToken ct = default)
        {
            CanSatisfyResult result = await CanSatisfyAsync(policy, context, ct).ConfigureAwait(false);
            if (!result.CanSatisfy)
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                    "The endpoint cannot accept a JWT securely.");
            }

            ICertificateManager certificates = m_configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The certificate manager is not initialized.");
            using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                context.EndpointDescription.ServerCertificate.Memory, m_telemetry);
            CertificateValidationResult validation = await certificates.ValidateAsync(chain, ct: ct)
                .ConfigureAwait(false);
            validation.ThrowIfInvalid();

            return await m_provider.GetIdentityAsync(policy, context, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects empty tokens and token values exceeding the existing transport quotas.
        /// </summary>
        internal static void ValidateToken(string jwt, TransportQuotas? quotas)
        {
            if (string.IsNullOrWhiteSpace(jwt))
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid,
                    "A pre-acquired JWT is required.");
            }

            int byteCount = Encoding.UTF8.GetByteCount(jwt);
            if (quotas != null &&
                ((quotas.MaxByteStringLength > 0 && byteCount > quotas.MaxByteStringLength) ||
                    (quotas.MaxStringLength > 0 && jwt.Length > quotas.MaxStringLength) ||
                    (quotas.MaxMessageSize > 0 && byteCount > quotas.MaxMessageSize)))
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The JWT exceeds the configured transport quotas.");
            }

            foreach (char character in jwt)
            {
                if (character is <= ' ' or > '~')
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid,
                        "Supply a raw compact JWT without a Bearer prefix or whitespace.");
                }
            }
        }

        /// <summary>The underlying issued-token implementation.</summary>
        private readonly IssuedTokenIdentityProvider m_provider;

        /// <summary>The connection's isolated trust configuration.</summary>
        private readonly ApplicationConfiguration m_configuration;

        /// <summary>The telemetry context used by certificate parsing.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
