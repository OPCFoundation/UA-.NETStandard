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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Resolves JWT issuer verification keys from an RFC 7517 JWKS document.
    /// </summary>
    /// <remarks>
    /// The resolver fetches keys on first use, caches them, and refreshes on
    /// <c>kid</c> misses no more frequently than the configured minimum refresh
    /// interval. Only RSA and EC public signing keys are materialized.
    /// Published keys are collected with their underlying cryptographic handles
    /// after the last reader releases them; refresh and disposal do not invalidate
    /// an already returned key.
    /// </remarks>
    public sealed class JwksIssuerKeyResolver : IIssuerKeyResolver, IDisposable
    {
        private static readonly TimeSpan s_defaultMinRefreshInterval = TimeSpan.FromMinutes(5);

        private readonly Uri m_jwksUri;
        private readonly HttpClient m_httpClient;
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_minRefreshInterval;
        private readonly HashSet<string>? m_allowedAlgorithms;
        private readonly SemaphoreSlim m_refreshLock = new(1, 1);
        private readonly Lock m_lifetimeLock = new();
        private int m_activeRequests;
        private KeyCache m_cache = KeyCache.Empty;
        private DateTimeOffset m_lastRefreshAttempt = DateTimeOffset.MinValue;
        private bool m_disposed;

        /// <summary>
        /// Creates a JWKS resolver with the default five-minute refresh throttle.
        /// </summary>
        public JwksIssuerKeyResolver(
            string issuerUri,
            string jwksUri,
            HttpClient httpClient,
            TimeProvider timeProvider)
            : this(issuerUri, jwksUri, httpClient, timeProvider, s_defaultMinRefreshInterval, null)
        {
        }

        /// <summary>
        /// Creates a JWKS resolver with a caller supplied refresh throttle.
        /// </summary>
        public JwksIssuerKeyResolver(
            string issuerUri,
            string jwksUri,
            HttpClient httpClient,
            TimeProvider timeProvider,
            TimeSpan minRefreshInterval)
            : this(issuerUri, jwksUri, httpClient, timeProvider, minRefreshInterval, null)
        {
        }

        /// <summary>
        /// Creates a JWKS resolver with a caller supplied refresh throttle and
        /// optional JWS algorithm allow-list.
        /// </summary>
        public JwksIssuerKeyResolver(
            string issuerUri,
            string jwksUri,
            HttpClient httpClient,
            TimeProvider timeProvider,
            TimeSpan minRefreshInterval,
            IEnumerable<string>? allowedAlgorithms)
        {
            if (string.IsNullOrEmpty(issuerUri))
            {
                throw new ArgumentException(
                    "Issuer URI must be supplied.",
                    nameof(issuerUri));
            }
            if (string.IsNullOrEmpty(jwksUri))
            {
                throw new ArgumentException(
                    "JWKS URI must be supplied.",
                    nameof(jwksUri));
            }
            if (!Uri.TryCreate(jwksUri, UriKind.Absolute, out Uri? parsedJwksUri))
            {
                throw new ArgumentException(
                    "JWKS URI must be an absolute URI.",
                    nameof(jwksUri));
            }

            if (minRefreshInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minRefreshInterval),
                    "Minimum refresh interval must not be negative.");
            }

            IssuerUri = issuerUri;
            m_jwksUri = parsedJwksUri;
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_minRefreshInterval = minRefreshInterval;

            if (allowedAlgorithms != null)
            {
                m_allowedAlgorithms = new HashSet<string>(StringComparer.Ordinal);
                foreach (string algorithm in allowedAlgorithms)
                {
                    if (!string.IsNullOrEmpty(algorithm))
                    {
                        m_allowedAlgorithms.Add(algorithm);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public string IssuerUri { get; }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
            string? keyId,
            CancellationToken ct = default)
        {
            BeginRequest();
            try
            {
                if (!Volatile.Read(ref m_cache).HasValue)
                {
                    await EnsureCacheAsync(ct).ConfigureAwait(false);
                }

                ThrowIfDisposed();
                KeyCache cache = Volatile.Read(ref m_cache);
                IReadOnlyList<IssuerVerificationKey> keys = cache.Get(keyId);
                if (keyId == null || keys.Count != 0)
                {
                    return keys;
                }

                if (CanRefresh(cache))
                {
                    await RefreshAfterMissAsync(cache.RefreshTime, ct).ConfigureAwait(false);
                    ThrowIfDisposed();
                    keys = Volatile.Read(ref m_cache).Get(keyId);
                }

                return keys;
            }
            finally
            {
                EndRequest();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lifetimeLock)
            {
                if (m_disposed)
                {
                    return;
                }

                Volatile.Write(ref m_disposed, true);
                Volatile.Write(ref m_cache, KeyCache.Empty);
                if (m_activeRequests == 0)
                {
                    m_refreshLock.Dispose();
                }
            }
        }

        private void BeginRequest()
        {
            lock (m_lifetimeLock)
            {
                ThrowIfDisposed();
                m_activeRequests++;
            }
        }

        private void EndRequest()
        {
            lock (m_lifetimeLock)
            {
                if (--m_activeRequests == 0 && m_disposed)
                {
                    m_refreshLock.Dispose();
                }
            }
        }

        private async ValueTask EnsureCacheAsync(CancellationToken ct)
        {
            await m_refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!m_cache.HasValue && CanRefresh(m_cache))
                {
                    try
                    {
                        await RefreshCoreAsync(ct).ConfigureAwait(false);
                    }
                    catch when (m_cache.HasValue)
                    {
                    }
                }
            }
            finally
            {
                m_refreshLock.Release();
            }
        }

        private async ValueTask RefreshAfterMissAsync(DateTimeOffset observedRefreshTime, CancellationToken ct)
        {
            await m_refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                KeyCache cache = m_cache;
                if (cache.HasValue && cache.RefreshTime > observedRefreshTime)
                {
                    return;
                }
                if (!CanRefresh(cache))
                {
                    return;
                }

                try
                {
                    await RefreshCoreAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch
                {
                    // Retain the last usable snapshot after a failed refresh.
                    // The failed attempt timestamp still throttles retries.
                }
            }
            finally
            {
                m_refreshLock.Release();
            }
        }

        private async ValueTask RefreshCoreAsync(CancellationToken ct)
        {
            m_lastRefreshAttempt = m_timeProvider.GetUtcNow();
            IReadOnlyList<IssuerVerificationKey> keys = await FetchKeysAsync(ct).ConfigureAwait(false);
            DateTimeOffset refreshTime = m_timeProvider.GetUtcNow();
            var replacement = new KeyCache(keys, refreshTime, hasValue: true);
            lock (m_lifetimeLock)
            {
                ThrowIfDisposed();
                Volatile.Write(ref m_cache, replacement);
            }
        }

        private async ValueTask<IReadOnlyList<IssuerVerificationKey>> FetchKeysAsync(CancellationToken ct)
        {
            using HttpResponseMessage response = await m_httpClient.GetAsync(m_jwksUri, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
#if NET5_0_OR_GREATER
            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#else
            ct.ThrowIfCancellationRequested();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            return ParseJwks(json);
        }

        private ReadOnlyCollection<IssuerVerificationKey> ParseJwks(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("keys", out JsonElement keysElement) ||
                keysElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("JWKS document must contain a 'keys' array.");
            }

            var keys = new List<IssuerVerificationKey>();
            foreach (JsonElement jwk in keysElement.EnumerateArray())
            {
                try
                {
                    AddJwkKeys(jwk, keys);
                }
                catch (ArgumentException)
                {
                }
                catch (CryptographicException)
                {
                }
                catch (FormatException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }

            return keys.AsReadOnly();
        }

        private void AddJwkKeys(JsonElement jwk, IList<IssuerVerificationKey> keys)
        {
            if (jwk.ValueKind != JsonValueKind.Object || !IsUsableForSigning(jwk))
            {
                return;
            }

            string? keyType = GetString(jwk, "kty");
            if (string.Equals(keyType, "RSA", StringComparison.Ordinal))
            {
                AddRsaKeys(jwk, keys);
            }
            else if (string.Equals(keyType, "EC", StringComparison.Ordinal))
            {
                AddEcKeys(jwk, keys);
            }
        }

#pragma warning disable CA2000 // IssuerVerificationKey owns parsed asymmetric keys; TODO: replace with ownership annotations when available.
        private void AddRsaKeys(JsonElement jwk, IList<IssuerVerificationKey> keys)
        {
            string? modulus = GetString(jwk, "n");
            string? exponent = GetString(jwk, "e");
            if (string.IsNullOrEmpty(modulus) || string.IsNullOrEmpty(exponent))
            {
                return;
            }

            var parameters = new RSAParameters
            {
                Modulus = Base64UrlDecode(modulus!),
                Exponent = Base64UrlDecode(exponent!)
            };

            string? algorithm = GetString(jwk, "alg");
            foreach (string selectedAlgorithm in SelectAlgorithms(algorithm, isRsa: true, curveName: null))
            {
                RSA? rsa = null;
                IssuerVerificationKey? verificationKey = null;
                try
                {
                    rsa = RSA.Create();
                    rsa.ImportParameters(parameters);
                    verificationKey = new IssuerVerificationKey(GetString(jwk, "kid"), rsa, selectedAlgorithm);
                    rsa = null;
                    keys.Add(verificationKey);
                    verificationKey = null;
                }
                finally
                {
                    verificationKey?.Dispose();
                    rsa?.Dispose();
                }
            }
        }

        private void AddEcKeys(JsonElement jwk, IList<IssuerVerificationKey> keys)
        {
            string? curveName = GetString(jwk, "crv");
            string? x = GetString(jwk, "x");
            string? y = GetString(jwk, "y");
            if (string.IsNullOrEmpty(curveName) || string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
            {
                return;
            }

            ECCurve curve = ToCurve(curveName!);
            var parameters = new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = Base64UrlDecode(x!),
                    Y = Base64UrlDecode(y!)
                }
            };

            string? algorithm = GetString(jwk, "alg");
            foreach (string selectedAlgorithm in SelectAlgorithms(algorithm, isRsa: false, curveName: curveName))
            {
                ECDsa? ecdsa = null;
                IssuerVerificationKey? verificationKey = null;
                try
                {
                    ecdsa = ECDsa.Create(parameters);
                    verificationKey = new IssuerVerificationKey(GetString(jwk, "kid"), ecdsa, selectedAlgorithm);
                    ecdsa = null;
                    keys.Add(verificationKey);
                    verificationKey = null;
                }
                finally
                {
                    verificationKey?.Dispose();
                    ecdsa?.Dispose();
                }
            }
        }

#pragma warning restore CA2000

        private IEnumerable<string> SelectAlgorithms(string? algorithm, bool isRsa, string? curveName)
        {
            if (!string.IsNullOrEmpty(algorithm))
            {
                if (IsAllowedAlgorithm(algorithm!) && IsCompatibleAlgorithm(algorithm!, isRsa, curveName))
                {
                    yield return algorithm!;
                }
                yield break;
            }

            if (m_allowedAlgorithms == null || m_allowedAlgorithms.Count == 0)
            {
                string defaultAlgorithm = isRsa
                    ? "RS256"
                    : DefaultEcAlgorithm(curveName);
                if (defaultAlgorithm.Length != 0)
                {
                    yield return defaultAlgorithm;
                }
                yield break;
            }

            foreach (string allowedAlgorithm in m_allowedAlgorithms)
            {
                if (IsCompatibleAlgorithm(allowedAlgorithm, isRsa, curveName))
                {
                    yield return allowedAlgorithm;
                }
            }
        }

        private bool IsAllowedAlgorithm(string algorithm)
        {
            return m_allowedAlgorithms == null ||
                m_allowedAlgorithms.Count == 0 ||
                m_allowedAlgorithms.Contains(algorithm);
        }

        private bool CanRefresh(KeyCache cache)
        {
            DateTimeOffset lastAttempt = cache.HasValue && cache.RefreshTime > m_lastRefreshAttempt
                ? cache.RefreshTime
                : m_lastRefreshAttempt;
            return lastAttempt + m_minRefreshInterval <= m_timeProvider.GetUtcNow();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(JwksIssuerKeyResolver));
            }
        }

        private static bool IsUsableForSigning(JsonElement jwk)
        {
            string? use = GetString(jwk, "use");
            if (use != null && !string.Equals(use, "sig", StringComparison.Ordinal))
            {
                return false;
            }

            if (!jwk.TryGetProperty("key_ops", out JsonElement keyOps))
            {
                return true;
            }
            if (keyOps.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement operation in keyOps.EnumerateArray())
            {
                if (operation.ValueKind == JsonValueKind.String)
                {
                    string? value = operation.GetString();
                    if (string.Equals(value, "verify", StringComparison.Ordinal) ||
                        string.Equals(value, "sign", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCompatibleAlgorithm(string algorithm, bool isRsa, string? curveName)
        {
            if (isRsa)
            {
                return algorithm.StartsWith("RS", StringComparison.Ordinal) ||
                    algorithm.StartsWith("PS", StringComparison.Ordinal);
            }

            return string.Equals(algorithm, DefaultEcAlgorithm(curveName), StringComparison.Ordinal);
        }

        private static string DefaultEcAlgorithm(string? curveName)
        {
            return curveName switch
            {
                "P-256" or "prime256v1" or "secp256r1" => "ES256",
                "P-384" or "secp384r1" => "ES384",
                "P-521" or "secp521r1" => "ES512",
                _ => string.Empty
            };
        }

        private static ECCurve ToCurve(string curveName)
        {
            return curveName switch
            {
                "P-256" or "prime256v1" or "secp256r1" => ECCurve.NamedCurves.nistP256,
                "P-384" or "secp384r1" => ECCurve.NamedCurves.nistP384,
                "P-521" or "secp521r1" => ECCurve.NamedCurves.nistP521,
                _ => throw new NotSupportedException(
                    $"JWK EC curve '{curveName}' is not supported.")
            };
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }

        private sealed class KeyCache
        {
            public static readonly KeyCache Empty = new(
                [],
                DateTimeOffset.MinValue,
                hasValue: false);

            private readonly IReadOnlyList<IssuerVerificationKey> m_keys;
            private readonly Dictionary<string, IReadOnlyList<IssuerVerificationKey>> m_keysById;

            public KeyCache(
                IReadOnlyList<IssuerVerificationKey> keys,
                DateTimeOffset refreshTime,
                bool hasValue)
            {
                m_keys = keys;
                RefreshTime = refreshTime;
                HasValue = hasValue;

                var keyed = new Dictionary<string, List<IssuerVerificationKey>>(StringComparer.Ordinal);
                foreach (IssuerVerificationKey key in keys)
                {
                    if (key.KeyId != null)
                    {
                        if (!keyed.TryGetValue(key.KeyId, out List<IssuerVerificationKey>? matchingKeys))
                        {
                            matchingKeys = [];
                            keyed[key.KeyId] = matchingKeys;
                        }
                        matchingKeys.Add(key);
                    }
                }

                var readOnlyById = new Dictionary<string, IReadOnlyList<IssuerVerificationKey>>(
                    StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<IssuerVerificationKey>> item in keyed)
                {
                    readOnlyById[item.Key] = item.Value.AsReadOnly();
                }
                m_keysById = readOnlyById;
            }

            public bool HasValue { get; }

            public DateTimeOffset RefreshTime { get; }

            public IReadOnlyList<IssuerVerificationKey> Get(string? keyId)
            {
                if (keyId == null)
                {
                    return m_keys;
                }

                return m_keysById.TryGetValue(keyId, out IReadOnlyList<IssuerVerificationKey>? keys)
                    ? keys
                    : [];
            }

        }
    }
}
