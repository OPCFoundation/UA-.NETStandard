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

#nullable enable

using System;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

#if NET6_0_OR_GREATER
using BitFaster.Caching;
using BitFaster.Caching.Lru;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// A two-tier LRU cache for certificates.
    /// Public-key certificates use LRU eviction only.
    /// Private-key certificates use LRU + TTL eviction.
    /// On platforms older than .NET 6 this is a no-op passthrough.
    /// </summary>
    internal sealed class CertificateCache : IDisposable
    {
        private const int kDefaultPublicKeyCacheCapacity = 256;
        private const int kDefaultPrivateKeyCacheCapacity = 64;
        private static readonly TimeSpan s_defaultPrivateKeyTtl = TimeSpan.FromSeconds(30);

        private readonly ILogger<CertificateCache> m_logger;
        private readonly Meter m_meter;

#if NET6_0_OR_GREATER
        private readonly ICache<string, Certificate> m_publicKeyCache;
        private readonly ICache<string, Certificate> m_privateKeyCache;

        /// <summary>
        /// Creates a new certificate cache.
        /// </summary>
        /// <param name="telemetry">The telemetry context for logging and metrics.</param>
        /// <param name="publicKeyCacheCapacity">Maximum number of public-key certificates to cache.</param>
        /// <param name="privateKeyCacheCapacity">Maximum number of private-key certificates to cache.</param>
        /// <param name="privateKeyTtl">Time-to-live for private-key certificate entries.</param>
        public CertificateCache(
            ITelemetryContext telemetry,
            int publicKeyCacheCapacity = kDefaultPublicKeyCacheCapacity,
            int privateKeyCacheCapacity = kDefaultPrivateKeyCacheCapacity,
            TimeSpan? privateKeyTtl = null)
        {
            m_logger = telemetry.CreateLogger<CertificateCache>();
            m_meter = telemetry.CreateMeter();

            privateKeyTtl ??= s_defaultPrivateKeyTtl;

            m_publicKeyCache = new ConcurrentLruBuilder<string, Certificate>()
                .WithCapacity(publicKeyCacheCapacity)
                .WithMetrics()
                .Build();

            m_privateKeyCache = new ConcurrentLruBuilder<string, Certificate>()
                .WithCapacity(privateKeyCacheCapacity)
                .WithExpireAfterWrite(privateKeyTtl.Value)
                .WithMetrics()
                .Build();

            m_meter.CreateObservableCounter<long>(
                "opcua.certcache.hit",
                () => (m_publicKeyCache?.Metrics.Value?.Hits ?? 0) + (m_privateKeyCache?.Metrics.Value?.Hits ?? 0),
                description: "Total certificate cache hits");

            m_meter.CreateObservableCounter<long>(
                "opcua.certcache.miss",
                () => (m_publicKeyCache?.Metrics.Value?.Misses ?? 0) + (m_privateKeyCache?.Metrics.Value?.Misses ?? 0),
                description: "Total certificate cache misses");

            m_meter.CreateObservableGauge<int>(
                "opcua.certcache.size",
                () => (m_publicKeyCache?.Count ?? 0) + (m_privateKeyCache?.Count ?? 0),
                description: "Current number of cached certificate entries");

            m_meter.CreateObservableGauge<int>(
                "opcua.certcache.private_key_entries",
                () => m_privateKeyCache?.Count ?? 0,
                description: "Current number of cached entries with private keys");

            m_meter.CreateObservableCounter<long>(
                "opcua.certcache.eviction",
                () => (m_publicKeyCache?.Metrics.Value?.Evicted ?? 0) + (m_privateKeyCache?.Metrics.Value?.Evicted ?? 0),
                description: "Total certificate cache evictions");
        }

        /// <summary>
        /// Tries to get a certificate from the cache by thumbprint.
        /// Returns with an extra reference so the caller owns a reference.
        /// </summary>
        public Certificate? TryGet(string thumbprint)
        {
            if (m_privateKeyCache.TryGet(thumbprint, out Certificate? cert) && cert != null)
            {
                try
                {
                    return cert.AddRef();
                }
                catch (ObjectDisposedException)
                {
                    m_privateKeyCache.TryRemove(thumbprint);
                }
            }

            if (m_publicKeyCache.TryGet(thumbprint, out cert) && cert != null)
            {
                try
                {
                    return cert.AddRef();
                }
                catch (ObjectDisposedException)
                {
                    m_publicKeyCache.TryRemove(thumbprint);
                }
            }

            return null;
        }

        /// <summary>
        /// Adds or updates a certificate in the appropriate cache tier.
        /// Private-key certificates go into the TTL cache, public-key
        /// certificates go into the LRU-only cache.
        /// The cache stores an AddRef'd copy; the matching Dispose is
        /// handled automatically by the LRU when an entry is evicted.
        /// No explicit ItemRemoved handler is needed because the LRU
        /// already calls <see cref="IDisposable.Dispose"/> on evicted values.
        /// </summary>
        public void Set(string thumbprint, Certificate certificate)
        {
            if (certificate.HasPrivateKey)
            {
                m_privateKeyCache.AddOrUpdate(thumbprint, certificate.AddRef());
            }
            else
            {
                m_publicKeyCache.AddOrUpdate(thumbprint, certificate.AddRef());
            }
        }

        /// <summary>
        /// Removes a certificate from both cache tiers.
        /// </summary>
        public void Remove(string thumbprint)
        {
            m_publicKeyCache.TryRemove(thumbprint);
            m_privateKeyCache.TryRemove(thumbprint);
        }

        /// <summary>
        /// Clears all entries from both cache tiers.
        /// </summary>
        public void Clear()
        {
            m_publicKeyCache.Clear();
            m_privateKeyCache.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_publicKeyCache.Clear();
            m_privateKeyCache.Clear();
            m_meter.Dispose();
        }
#else
        /// <summary>
        /// Creates a new certificate cache (no-op on this platform).
        /// </summary>
        public CertificateCache(
            ITelemetryContext telemetry,
            int publicKeyCacheCapacity = kDefaultPublicKeyCacheCapacity,
            int privateKeyCacheCapacity = kDefaultPrivateKeyCacheCapacity,
            TimeSpan? privateKeyTtl = null)
        {
            m_logger = telemetry.CreateLogger<CertificateCache>();
            m_meter = telemetry.CreateMeter();
        }

        /// <summary>
        /// No caching on this platform.
        /// </summary>
        public Certificate? TryGet(string thumbprint)
        {
            return null;
        }

        /// <summary>
        /// No caching on this platform.
        /// </summary>
        public void Set(string thumbprint, Certificate certificate) { }

        /// <summary>
        /// No caching on this platform.
        /// </summary>
        public void Remove(string thumbprint) { }

        /// <summary>
        /// No caching on this platform.
        /// </summary>
        public void Clear() { }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_meter.Dispose();
        }
#endif
    }
}
