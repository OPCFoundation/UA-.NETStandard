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
 *
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// A JSON Web Key (JWK) — the subset of RFC 7517 that the JWT
    /// authenticator needs to verify a signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="IIssuerKeyResolver"/> implementations to
    /// publish their verification keys without coupling to a specific
    /// JWT library or to the
    /// <c>System.IdentityModel.Tokens.Jwt</c> AOT-unfriendly stack.
    /// </para>
    /// <para>
    /// The implementation only supports asymmetric keys (RSA, ECDSA);
    /// HS256 (HMAC) is intentionally not supported because it would
    /// require sharing a symmetric secret with each verifier, which is
    /// incompatible with the multi-party OPC UA authorization model.
    /// </para>
    /// </remarks>
    public sealed class IssuerVerificationKey : IDisposable
    {
        private readonly AsymmetricAlgorithm m_key;
        private bool m_disposed;

        /// <summary>
        /// Wraps an asymmetric verification key. Ownership transfers to
        /// the <see cref="IssuerVerificationKey"/>: disposing the key
        /// disposes the underlying algorithm.
        /// </summary>
        /// <param name="keyId">
        /// JWK <c>kid</c> used to match the JWT header. May be
        /// <see langword="null"/> when the issuer does not include a
        /// <c>kid</c> in the JOSE header — in that case the resolver
        /// returns its single configured key.
        /// </param>
        /// <param name="key">
        /// Either an <see cref="RSA"/> or an <see cref="ECDsa"/> public
        /// key.
        /// </param>
        /// <param name="algorithm">
        /// The JWS algorithm name from RFC 7518 §3.1 (e.g. <c>RS256</c>,
        /// <c>RS384</c>, <c>RS512</c>, <c>ES256</c>, <c>ES384</c>,
        /// <c>ES512</c>, <c>PS256</c>).
        /// </param>
        public IssuerVerificationKey(string? keyId, AsymmetricAlgorithm key, string algorithm)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (string.IsNullOrEmpty(algorithm))
            {
                throw new ArgumentException(
                    "Algorithm must be supplied.",
                    nameof(algorithm));
            }
            if (key is not RSA && key is not ECDsa)
            {
                throw new ArgumentException(
                    "Only RSA and ECDsa keys are supported.",
                    nameof(key));
            }
            KeyId = keyId;
            m_key = key;
            Algorithm = algorithm;
        }

        /// <summary>JWK <c>kid</c> matched against the JWT header.</summary>
        public string? KeyId { get; }

        /// <summary>JWS algorithm (RFC 7518).</summary>
        public string Algorithm { get; }

        /// <summary>
        /// Verifies an RFC 7515 signed JWS over the supplied signing
        /// input (the canonical
        /// <c>base64url(header) + "." + base64url(payload)</c> bytes).
        /// </summary>
        /// <remarks>
        /// Uses <see cref="byte"/>[] parameters for compatibility with
        /// the netstandard2.1 / net472 / net48 surface.
        /// </remarks>
        public bool VerifySignature(byte[] signingInput, byte[] signature)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(IssuerVerificationKey));
            }
            if (signingInput == null)
            {
                throw new ArgumentNullException(nameof(signingInput));
            }
            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }
            HashAlgorithmName hash = HashFromAlgorithm(Algorithm);
            return m_key switch
            {
                RSA rsa => Algorithm.StartsWith("PS", StringComparison.Ordinal)
                    ? rsa.VerifyData(signingInput, signature, hash, RSASignaturePadding.Pss)
                    : rsa.VerifyData(signingInput, signature, hash, RSASignaturePadding.Pkcs1),
                ECDsa ec => ec.VerifyData(signingInput, signature, hash),
                _ => false
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_key.Dispose();
        }

        private static HashAlgorithmName HashFromAlgorithm(string alg)
        {
            return alg switch
            {
                "RS256" or "PS256" or "ES256" => HashAlgorithmName.SHA256,
                "RS384" or "PS384" or "ES384" => HashAlgorithmName.SHA384,
                "RS512" or "PS512" or "ES512" => HashAlgorithmName.SHA512,
                _ => throw new NotSupportedException(
                    $"JWS algorithm '{alg}' is not supported.")
            };
        }
    }

    /// <summary>
    /// Resolves the verification keys for an Authorization Service so a
    /// server-side <c>JwtAuthenticator</c> can verify the signature of
    /// an inbound JWT-bearing <c>IssuedIdentityToken</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>StaticIssuerKeyResolver</c> — keys baked into config /
    ///         test fixtures.</item>
    ///   <item><c>JwksIssuerKeyResolver</c> — fetches RFC 7517 JWKS from
    ///         the issuer's <c>jwks_uri</c> with caching + rotation
    ///         handling.</item>
    ///   <item><c>OidcDiscoveryIssuerKeyResolver</c> — resolves
    ///         <c>jwks_uri</c> via <c>.well-known/openid-configuration</c>.</item>
    ///   <item><c>X509CertificateIssuerKeyResolver</c> — uses the
    ///         configured signing cert of a local Authorization Service.</item>
    /// </list>
    /// <para>
    /// The interface is async because real implementations may fetch
    /// keys over HTTP. Implementations SHOULD cache to keep the
    /// hot-path synchronous after the first call.
    /// </para>
    /// </remarks>
    public interface IIssuerKeyResolver
    {
        /// <summary>
        /// The issuer URI this resolver is configured for. Used by a
        /// composite resolver to dispatch by JWT <c>iss</c> claim.
        /// </summary>
        string IssuerUri { get; }

        /// <summary>
        /// Returns candidate verification keys for the supplied
        /// <paramref name="keyId"/> (JOSE <c>kid</c>). When
        /// <paramref name="keyId"/> is <see langword="null"/> the
        /// resolver returns every key it knows of (the caller will try
        /// each in turn).
        /// </summary>
        /// <remarks>
        /// Returned keys are owned by the resolver; callers MUST NOT
        /// dispose them. The lifetime is at least the duration of the
        /// returned <see cref="ValueTask{TResult}"/>.
        /// </remarks>
        ValueTask<IReadOnlyList<IssuerVerificationKey>> GetKeysAsync(
            string? keyId,
            CancellationToken ct = default);
    }
}
