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
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapts a GDS KeyCredentialServiceClient to the client-side <see cref="IAccessTokenProvider"/> surface.
    /// </summary>
    /// <remarks>
    /// The returned issued token uses the experimental vendor profile URI
    /// <c>urn:opcfoundation:netstandard:profile:authentication:keycredential</c> and is intended only for
    /// closed deployments that have enabled the matching server-side bridge authenticator.
    /// </remarks>
    public sealed class GdsKeyCredentialAccessTokenProvider : IAccessTokenProvider, IDisposable
    {
        /// <summary>
        /// Vendor profile URI used by the KeyCredential bridge token.
        /// </summary>
        public const string ProfileUri =
            "urn:opcfoundation:netstandard:profile:authentication:keycredential";

        private static readonly TimeSpan s_defaultCacheLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan s_refreshSkew = TimeSpan.FromSeconds(30);
        private readonly Func<CancellationToken, ValueTask<GdsIssuedKeyCredential>> m_acquireCredential;
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private GdsIssuedKeyCredential? m_cachedCredential;
        private bool m_disposed;

        /// <summary>
        /// Creates a provider that wraps an Opc.Ua.Gds.Client.KeyCredentialServiceClient instance.
        /// </summary>
        public GdsKeyCredentialAccessTokenProvider(
            object keyCredentialServiceClient,
            string authorityUri,
            string applicationUri,
            ByteString publicKey = default,
            string? securityPolicyUri = null,
            ArrayOf<NodeId>? requestedRoles = null,
            TimeSpan? cacheLifetime = null)
            : this(
                new ReflectionKeyCredentialClient(
                    keyCredentialServiceClient,
                    applicationUri,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles ?? []).AcquireAsync,
                authorityUri,
                cacheLifetime)
        {
        }

        /// <summary>
        /// Creates a provider from a credential acquisition delegate.
        /// </summary>
        public GdsKeyCredentialAccessTokenProvider(
            Func<CancellationToken, ValueTask<GdsIssuedKeyCredential>> acquireCredential,
            string authorityUri,
            TimeSpan? cacheLifetime = null)
        {
            m_acquireCredential = acquireCredential ?? throw new ArgumentNullException(nameof(acquireCredential));
            AuthorityUri = authorityUri ?? throw new ArgumentNullException(nameof(authorityUri));
            CacheLifetime = cacheLifetime ?? s_defaultCacheLifetime;
        }

        /// <inheritdoc/>
        public string AuthorityUri { get; }

        /// <summary>
        /// Lifetime assigned to credentials when the GDS response does not carry an expiration.
        /// </summary>
        public TimeSpan CacheLifetime { get; }

        /// <inheritdoc/>
        public async ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            GdsIssuedKeyCredential credential = await GetCredentialAsync(ct).ConfigureAwait(false);
            string nonce = CreateNonce();
            DateTime issuedAt = DateTime.UtcNow;
            long issuedAtSeconds = new DateTimeOffset(issuedAt).ToUnixTimeSeconds();
            string proof = CreateProof(
                credential.CredentialSecret,
                credential.CredentialId,
                nonce,
                issuedAtSeconds);
            byte[] tokenData = Encoding.UTF8.GetBytes(
                "{\"credentialId\":\"" +
                EscapeJson(credential.CredentialId) +
                "\",\"nonce\":\"" +
                nonce +
                "\",\"issuedAt\":" +
                issuedAtSeconds.ToString(CultureInfo.InvariantCulture) +
                ",\"proof\":\"" +
                proof +
                "\"}");

#pragma warning disable CA2000 // Ownership transfers to caller; TODO: remove when analyzer models ValueTask ownership.
            return new AccessToken(
                ProfileUri,
                tokenData,
                credential.ExpiresAt,
                credential.CredentialId,
                credential.GrantedScopes);
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_lock.Dispose();
            m_cachedCredential?.Dispose();
        }

        private async ValueTask<GdsIssuedKeyCredential> GetCredentialAsync(CancellationToken ct)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_cachedCredential != null &&
                    m_cachedCredential.ExpiresAt - s_refreshSkew > DateTime.UtcNow)
                {
                    return m_cachedCredential.Copy();
                }

                m_cachedCredential?.Dispose();
                GdsIssuedKeyCredential issued = await m_acquireCredential(ct).ConfigureAwait(false);
                if (issued.ExpiresAt == DateTime.MinValue || issued.ExpiresAt == default)
                {
                    issued = issued with { ExpiresAt = DateTime.UtcNow.Add(CacheLifetime) };
                }
                m_cachedCredential = issued.Copy();
                return issued;
            }
            finally
            {
                m_lock.Release();
            }
        }

        private static string CreateNonce()
        {
            byte[] nonce = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }
            return Base64UrlEncode(nonce);
        }

        private static string CreateProof(byte[] secret, string credentialId, string nonce, long issuedAt)
        {
            string input = credentialId +
                "\n" +
                nonce +
                "\n" +
                issuedAt.ToString(CultureInfo.InvariantCulture);
            using var hmac = new HMACSHA256(secret);
            return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string EscapeJson(string value)
        {
#pragma warning disable CA1307 // StringComparison overload is not available on every supported TFM.
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
#pragma warning restore CA1307
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(GdsKeyCredentialAccessTokenProvider));
            }
        }

        private sealed class ReflectionKeyCredentialClient
        {
            private readonly object m_client;
            private readonly MethodInfo m_startRequest;
            private readonly MethodInfo m_finishRequest;
            private readonly string m_applicationUri;
            private readonly ByteString m_publicKey;
            private readonly string? m_securityPolicyUri;
            private readonly ArrayOf<NodeId> m_requestedRoles;

            public ReflectionKeyCredentialClient(
                object client,
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles)
            {
                m_client = client ?? throw new ArgumentNullException(nameof(client));
                if (string.IsNullOrWhiteSpace(applicationUri))
                {
                    throw new ArgumentException("ApplicationUri must be supplied.", nameof(applicationUri));
                }
                m_applicationUri = applicationUri;
                m_publicKey = publicKey;
                m_securityPolicyUri = securityPolicyUri;
                m_requestedRoles = requestedRoles;
#pragma warning disable IL2075 // Reflection is limited to the source-generated KeyCredential client shape.
                Type type = client.GetType();
                m_startRequest = type.GetMethod("StartRequestAsync", BindingFlags.Public | BindingFlags.Instance) ??
                    throw new ArgumentException("KeyCredentialServiceClient must expose StartRequestAsync.", nameof(client));
                m_finishRequest = type.GetMethod("FinishRequestAsync", BindingFlags.Public | BindingFlags.Instance) ??
                    throw new ArgumentException("KeyCredentialServiceClient must expose FinishRequestAsync.", nameof(client));
#pragma warning restore IL2075
            }

            public async ValueTask<GdsIssuedKeyCredential> AcquireAsync(CancellationToken ct)
            {
                object? startResult = m_startRequest.Invoke(
                    m_client,
                    [
                        m_applicationUri,
                        m_publicKey,
                        m_securityPolicyUri,
                        m_requestedRoles,
                        ct
                    ]);
                NodeId requestId = await AwaitValueTaskAsync<NodeId>(startResult).ConfigureAwait(false);

                object? finishResult = m_finishRequest.Invoke(
                    m_client,
                    [
                        requestId,
                        false,
                        ct
                    ]);
                (string? credentialId, ByteString credentialSecret, string _, string? securityPolicyUri, ArrayOf<NodeId> grantedRoles) =
                    await AwaitValueTaskAsync<(
                            string credentialId,
                            ByteString credentialSecret,
                            string certificateThumbprint,
                            string securityPolicyUri,
                            ArrayOf<NodeId> grantedRoles)>(finishResult)
                        .ConfigureAwait(false);
                List<string> scopes = [];
                if (!string.IsNullOrWhiteSpace(securityPolicyUri))
                {
                    scopes.Add(securityPolicyUri);
                }
                foreach (NodeId role in grantedRoles)
                {
                    scopes.Add(role.ToString());
                }
                return new GdsIssuedKeyCredential(
                    credentialId,
                    credentialSecret.ToArray(),
                    DateTime.MinValue,
                    [.. scopes]);
            }

            private static async ValueTask<T> AwaitValueTaskAsync<T>(object? valueTask)
            {
                if (valueTask == null)
                {
                    throw new InvalidOperationException("GDS client returned null.");
                }
                if (valueTask is ValueTask<T> typed)
                {
                    return await typed.ConfigureAwait(false);
                }
                if (valueTask is Task<T> task)
                {
                    return await task.ConfigureAwait(false);
                }
                throw new InvalidOperationException("GDS client returned an unsupported async result.");
            }
        }
    }

    /// <summary>
    /// Credential material issued by a GDS KeyCredentialService pull request.
    /// </summary>
    public sealed record GdsIssuedKeyCredential(
        string CredentialId,
        byte[] CredentialSecret,
        DateTime ExpiresAt,
        string[]? GrantedScopes = null) : IDisposable
    {
        /// <summary>
        /// Creates a defensive copy.
        /// </summary>
        public GdsIssuedKeyCredential Copy()
        {
            return new GdsIssuedKeyCredential(
                CredentialId,
                (byte[])CredentialSecret.Clone(),
                ExpiresAt,
                GrantedScopes == null ? null : (string[])GrantedScopes.Clone());
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Array.Clear(CredentialSecret, 0, CredentialSecret.Length);
        }
    }
}
