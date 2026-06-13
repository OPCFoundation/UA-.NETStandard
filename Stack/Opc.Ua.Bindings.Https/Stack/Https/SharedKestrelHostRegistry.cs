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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Identifies a single Kestrel host that can be shared across multiple
    /// <see cref="HttpsTransportListener"/> instances. The key is the
    /// <c>(host, port)</c> the listener wants to bind to. The first
    /// listener for a key configures the host's TLS certificate; later
    /// listeners must use a matching certificate (one TLS cert per
    /// <c>(host, port)</c> per TCP/TLS layering).
    /// </summary>
    /// <remarks>
    /// <para>
    /// TLS handshake happens before HTTP routing, so a single
    /// <c>HttpsConnectionAdapterOptions</c> applies to *all* requests on
    /// a given <c>(host, port)</c>; this implementation does not configure
    /// SNI selectors.
    /// </para>
    /// </remarks>
    internal readonly struct SharedHostKey : IEquatable<SharedHostKey>
    {
        public SharedHostKey(string host, int port)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }

        public bool Equals(SharedHostKey other)
        {
            return Port == other.Port &&
                string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => obj is SharedHostKey other && Equals(other);
        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port);
        }

        public override string ToString() => $"{Host}:{Port}";
    }

    /// <summary>
    /// DI-resolvable accessor that bridges <see cref="SharedHostStartup"/>
    /// to the late-bound <see cref="SharedKestrelHost"/>. Used because
    /// <c>Startup.Configure</c> runs synchronously during
    /// <c>IHost.StartAsync</c>, which is itself invoked from inside the
    /// <see cref="SharedKestrelHost"/> constructor — we cannot register
    /// <see cref="SharedKestrelHost"/> as a singleton up front because
    /// its instance does not exist yet at DI-registration time. The
    /// registry sets <see cref="Instance"/> immediately before starting
    /// the host so the request pipeline can resolve it on first request.
    /// </summary>
    internal sealed class SharedHostAccessor
    {
        public SharedKestrelHost? Instance { get; set; }
    }

    /// <summary>
    /// Process-wide registry of <see cref="SharedKestrelHost"/> instances
    /// keyed by <see cref="SharedHostKey"/>. The first
    /// <see cref="HttpsTransportListener"/> to <see cref="Acquire"/> a key
    /// constructs the underlying Kestrel <see cref="IHost"/>; subsequent
    /// listeners that share the same key are registered as additional
    /// path-prefix handlers on the same host. On <see cref="SharedHostLease.Dispose"/>
    /// the lease is decremented; when the last lease is released the
    /// host is stopped and removed from the registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Listeners bound to <see cref="System.Net.IPEndPoint.MinPort"/> (i.e.
    /// random port 0) cannot meaningfully share a key with another listener
    /// because the OS picks the port dynamically; those listeners bypass
    /// the registry entirely.
    /// </para>
    /// </remarks>
    internal sealed class SharedKestrelHostRegistry
    {
        /// <summary>
        /// Process-wide singleton instance.
        /// </summary>
        public static SharedKestrelHostRegistry Instance { get; } = new();

        private SharedKestrelHostRegistry()
        {
        }

        private readonly object m_lock = new();
        private readonly Dictionary<SharedHostKey, SharedKestrelHost> m_hosts = new();

        /// <summary>
        /// Acquires (or creates) the shared host for <paramref name="key"/>
        /// and registers <paramref name="listener"/> with it. Returns a
        /// lease that must be disposed when the listener no longer needs
        /// the host (typically from <c>HttpsTransportListener.Dispose</c>).
        /// </summary>
        /// <param name="key">The <c>(host, port)</c> the listener wants to bind to.</param>
        /// <param name="listener">The listener whose dispatcher should be wired in.</param>
        /// <param name="pathPrefix">
        /// The URL path prefix (typically the listener's
        /// <see cref="HttpsTransportListener.EndpointUrl"/> absolute path)
        /// the shared host's router will match requests against. Multiple
        /// listeners on the same key must register distinct prefixes;
        /// longest-prefix-wins.
        /// </param>
        /// <param name="hostFactory">
        /// Factory used to construct an unstarted <see cref="IHost"/>
        /// when no host exists for <paramref name="key"/> yet. The factory
        /// receives a <see cref="SharedHostAccessor"/> that the caller
        /// MUST register as a DI singleton in the IHost's service
        /// collection so <see cref="SharedHostStartup"/> can resolve it
        /// at request time. Invoked at most once per key.
        /// </param>
        /// <param name="serverCertificateThumbprint">
        /// SHA-1 thumbprint of the TLS certificate the listener will
        /// configure on the host. If a host already exists for
        /// <paramref name="key"/>, the existing thumbprint must match
        /// (single TLS cert per <c>(host, port)</c> per TCP/TLS layering);
        /// a mismatch throws <see cref="InvalidOperationException"/>.
        /// </param>
        public SharedHostLease Acquire(
            SharedHostKey key,
            HttpsTransportListener listener,
            string pathPrefix,
            Func<SharedHostAccessor, IHost> hostFactory,
            string serverCertificateThumbprint)
        {
            if (hostFactory == null)
            {
                throw new ArgumentNullException(nameof(hostFactory));
            }
            if (string.IsNullOrEmpty(serverCertificateThumbprint))
            {
                throw new ArgumentException(
                    "TLS certificate thumbprint is required for shared host registration.",
                    nameof(serverCertificateThumbprint));
            }

            lock (m_lock)
            {
                if (!m_hosts.TryGetValue(key, out SharedKestrelHost? host))
                {
                    var accessor = new SharedHostAccessor();
                    host = new SharedKestrelHost(key, serverCertificateThumbprint);
                    accessor.Instance = host;
                    // Build + start the host with the accessor already wired
                    // into DI so the first request can resolve the SharedHost.
                    IHost ihost = hostFactory(accessor);
                    host.AttachAndStart(ihost);
                    m_hosts[key] = host;
                }
                else if (!string.Equals(
                    host.ServerCertificateThumbprint,
                    serverCertificateThumbprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot share Kestrel host on {key}: existing TLS cert " +
                        $"thumbprint {host.ServerCertificateThumbprint} does not " +
                        $"match new listener's thumbprint {serverCertificateThumbprint}.");
                }

                host.AddListener(pathPrefix, listener);
                return new SharedHostLease(this, key, listener);
            }
        }

        /// <summary>
        /// Releases <paramref name="listener"/> from the host for
        /// <paramref name="key"/>. When the host's last listener is
        /// released the host is stopped and removed from the registry.
        /// </summary>
        internal void Release(SharedHostKey key, HttpsTransportListener listener)
        {
            SharedKestrelHost? toStop = null;
            lock (m_lock)
            {
                if (!m_hosts.TryGetValue(key, out SharedKestrelHost? host))
                {
                    return;
                }
                if (host.RemoveListener(listener))
                {
                    // Last listener gone — tear the host down.
                    m_hosts.Remove(key);
                    toStop = host;
                }
            }

            // Stop outside the lock to avoid blocking other Acquire/Release
            // calls during shutdown.
            toStop?.Stop();
        }

        /// <summary>
        /// Test helper: returns the number of distinct hosts the registry
        /// currently owns. Useful for asserting the ref-count lifecycle in
        /// <c>SharedKestrelHostTests</c>; not part of the production API.
        /// </summary>
        internal int Count
        {
            get
            {
                lock (m_lock)
                {
                    return m_hosts.Count;
                }
            }
        }

        /// <summary>
        /// Test helper: returns the registered listener count for a key,
        /// or 0 if the key is not registered.
        /// </summary>
        internal int ListenerCount(SharedHostKey key)
        {
            lock (m_lock)
            {
                return m_hosts.TryGetValue(key, out SharedKestrelHost? host)
                    ? host.ListenerCount
                    : 0;
            }
        }
    }

    /// <summary>
    /// One reference to a <see cref="SharedKestrelHost"/> held by a
    /// <see cref="HttpsTransportListener"/>. Disposing the lease unregisters
    /// the listener; when the last lease is disposed the underlying host
    /// is stopped.
    /// </summary>
    internal sealed class SharedHostLease : IDisposable
    {
        internal SharedHostLease(
            SharedKestrelHostRegistry registry,
            SharedHostKey key,
            HttpsTransportListener listener)
        {
            m_registry = registry;
            m_key = key;
            m_listener = listener;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_registry.Release(m_key, m_listener);
        }

        private readonly SharedKestrelHostRegistry m_registry;
        private readonly SharedHostKey m_key;
        private readonly HttpsTransportListener m_listener;
        private int m_disposed;
    }

    /// <summary>
    /// Wraps a Kestrel <see cref="IHost"/> shared by one or more
    /// <see cref="HttpsTransportListener"/> instances. The host's request
    /// pipeline routes by longest-prefix match on
    /// <see cref="HttpRequest.Path"/>; each registered listener handles
    /// every request whose path starts with the listener's
    /// <see cref="HttpsTransportListener.EndpointUrl"/> absolute path.
    /// </summary>
    internal sealed class SharedKestrelHost
    {
        internal SharedKestrelHost(
            SharedHostKey key,
            string serverCertificateThumbprint)
        {
            Key = key;
            ServerCertificateThumbprint = serverCertificateThumbprint;
        }

        internal SharedHostKey Key { get; }
        internal string ServerCertificateThumbprint { get; }
        internal int ListenerCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_listeners.Count;
                }
            }
        }

        /// <summary>
        /// Attaches the provided <paramref name="host"/> and starts it.
        /// Called by <see cref="SharedKestrelHostRegistry"/> after the
        /// <see cref="SharedHostAccessor"/> singleton has been wired so
        /// the request pipeline can resolve this instance on first
        /// request.
        /// </summary>
        internal void AttachAndStart(IHost host)
        {
            if (m_host != null)
            {
                throw new InvalidOperationException("Shared host is already attached.");
            }
            m_host = host;
            // Start synchronously to keep the existing single-listener
            // Open() contract (which is also sync via .GetAwaiter().GetResult()).
            m_host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        internal void AddListener(string pathPrefix, HttpsTransportListener listener)
        {
            string path = NormalisePath(pathPrefix);
            lock (m_lock)
            {
                m_listeners[path] = listener;
                RebuildRouteOrder();
            }
        }

        /// <summary>
        /// Returns true if the removal cleared the last listener — the
        /// caller is then responsible for stopping the host (the registry
        /// does so outside its own lock).
        /// </summary>
        internal bool RemoveListener(HttpsTransportListener listener)
        {
            lock (m_lock)
            {
                string? matchedPath = null;
                foreach (KeyValuePair<string, HttpsTransportListener> entry in m_listeners)
                {
                    if (ReferenceEquals(entry.Value, listener))
                    {
                        matchedPath = entry.Key;
                        break;
                    }
                }
                if (matchedPath != null)
                {
                    m_listeners.Remove(matchedPath);
                    RebuildRouteOrder();
                }
                return m_listeners.Count == 0;
            }
        }

        internal HttpsTransportListener? RouteByPath(PathString requestPath)
        {
            string normalised = requestPath.HasValue ? requestPath.Value! : "/";
            lock (m_lock)
            {
                // Longest-prefix-wins; m_routeOrder is kept sorted longest-first
                // so the first matching entry is the most specific.
                foreach (string path in m_routeOrder)
                {
                    if (PrefixMatch(normalised, path))
                    {
                        return m_listeners[path];
                    }
                }
                return null;
            }
        }

        internal void Stop()
        {
            if (m_host == null)
            {
                return;
            }
            try
            {
                m_host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort shutdown.
            }
            m_host.Dispose();
            m_host = null;
        }

        private void RebuildRouteOrder()
        {
            // sort descending by path length so longest-prefix wins
            var sorted = new List<string>(m_listeners.Keys);
            sorted.Sort((a, b) => b.Length.CompareTo(a.Length));
            m_routeOrder = sorted;
        }

        private static bool PrefixMatch(string requestPath, string prefix)
        {
            if (prefix.Length > requestPath.Length)
            {
                return false;
            }
            if (!requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            // Avoid matching "/Abc" against prefix "/A" by requiring the
            // boundary to be end-of-path, '/' or '?'.
            if (requestPath.Length == prefix.Length)
            {
                return true;
            }
            char boundary = requestPath[prefix.Length];
            return boundary is '/' or '?';
        }

        private static string NormalisePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "/";
            }
            // Drop trailing '/' (except for root) so "/A/" and "/A" match.
            if (path.Length > 1 && path[^1] == '/')
            {
                path = path[..^1];
            }
            return path;
        }

        private IHost? m_host;
        private readonly object m_lock = new();
        private readonly Dictionary<string, HttpsTransportListener> m_listeners =
            new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<string> m_routeOrder = Array.Empty<string>();
    }
}
