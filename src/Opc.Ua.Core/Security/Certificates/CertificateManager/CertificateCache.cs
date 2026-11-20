// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.Metrics;
using BitFaster.Caching;
using BitFaster.Caching.Lru;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A two-tier LRU cache for certificates. Public-key certificates use LRU
    /// eviction only. Private-key certificates use LRU + TTL eviction.
    /// </summary>
    internal sealed class CertificateCache : IDisposable
    {
        /// <summary>
        /// Creates a new certificate cache.
        /// </summary>
        /// <param name="telemetry">The telemetry context for logging and
        /// metrics.</param>
        /// <param name="publicKeyCacheCapacity">Maximum number of public-key
        /// certificates to cache.</param>
        /// <param name="privateKeyCacheCapacity">Maximum number of private-key
        /// certificates to cache.</param>
        /// <param name="privateKeyTtl">Time-to-live for private-key certificate
        /// entries.</param>
        public CertificateCache(
            ITelemetryContext telemetry,
            int publicKeyCacheCapacity = kDefaultPublicKeyCacheCapacity,
            int privateKeyCacheCapacity = kDefaultPrivateKeyCacheCapacity,
            TimeSpan? privateKeyTtl = null)
        {
            m_meter = telemetry?.CreateMeter();

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

            if (m_meter != null)
            {
                m_meter.CreateObservableCounter(
                    "opc.ua.certcache.hit",
                    () =>
                        (m_publicKeyCache?.Metrics.Value?.Hits ?? 0) +
                        (m_privateKeyCache?.Metrics.Value?.Hits ?? 0),
                    description: "Total certificate cache hits");

                m_meter.CreateObservableCounter(
                    "opc.ua.certcache.miss",
                    () =>
                        (m_publicKeyCache?.Metrics.Value?.Misses ?? 0) +
                        (m_privateKeyCache?.Metrics.Value?.Misses ?? 0),
                    description: "Total certificate cache misses");

                m_meter.CreateObservableGauge(
                    "opc.ua.certcache.size",
                    () =>
                        (m_publicKeyCache?.Count ?? 0) +
                        (m_privateKeyCache?.Count ?? 0),
                    description: "Current number of cached certificate entries");

                m_meter.CreateObservableGauge(
                    "opc.ua.certcache.private_key_entries",
                    () => m_privateKeyCache?.Count ?? 0,
                    description: "Current number of cached entries with private keys");

                m_meter.CreateObservableCounter(
                    "opc.ua.certcache.eviction",
                    () =>
                        (m_publicKeyCache?.Metrics.Value?.Evicted ?? 0) +
                        (m_privateKeyCache?.Metrics.Value?.Evicted ?? 0),
                    description: "Total certificate cache evictions");
            }
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
            m_meter?.Dispose();
        }

        private const int kDefaultPublicKeyCacheCapacity = 256;
        private const int kDefaultPrivateKeyCacheCapacity = 64;
        private static readonly TimeSpan s_defaultPrivateKeyTtl = TimeSpan.FromSeconds(30);
        private readonly Meter? m_meter;
        private readonly ICache<string, Certificate> m_publicKeyCache;
        private readonly ICache<string, Certificate> m_privateKeyCache;
    }
}
