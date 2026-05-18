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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Caching alias-name resolver — wraps an
    /// <see cref="AliasNameClient"/> with an in-memory
    /// alias-name → <see cref="ExpandedNodeId"/>[] lookup (plus
    /// reverse lookup) populated by calling <c>FindAlias</c> /
    /// <c>FindAliasVerbose</c> once and reused on subsequent
    /// resolutions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default refresh mode is
    /// <see cref="AliasNameResolverRefreshMode.Manual"/> — callers
    /// must invoke <see cref="RefreshAsync"/> (or
    /// <see cref="EnsureLoadedAsync"/>) to populate the cache. This is
    /// the safe default for servers that do not allow subscriptions or
    /// cannot afford the overhead of a recurring poll.
    /// </para>
    /// <para>
    /// Opt in to automatic invalidation by setting
    /// <see cref="AliasNameResolverOptions.RefreshMode"/> to
    /// <see cref="AliasNameResolverRefreshMode.AutoOnLastChange"/> —
    /// the resolver will read the category's
    /// <c>LastChange</c> property (Part 17 §6.3.1) every
    /// <see cref="AliasNameResolverOptions.PublishingIntervalMs"/> and
    /// invalidate the cache on any value-difference. Wraparound of the
    /// underlying <c>VersionTime</c> (<c>uint</c>) is honoured — the
    /// comparison is for inequality, not strictly greater-than.
    /// </para>
    /// <para>
    /// The resolver is <see cref="IAsyncDisposable"/> — disposing it
    /// stops the auto-refresh timer and releases any cached state.
    /// </para>
    /// </remarks>
    public sealed class AliasNameResolver : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new resolver over the supplied
        /// <see cref="AliasNameClient"/>.
        /// </summary>
        public AliasNameResolver(
            AliasNameClient client,
            AliasNameResolverOptions? options = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Options = (options ?? new AliasNameResolverOptions()).Clone();

            if (Options.RefreshMode == AliasNameResolverRefreshMode.AutoOnLastChange)
            {
                int periodMs = (int)Math.Max(100,
                    Options.PublishingIntervalMs);
                m_pollTimer = new Timer(
                    PollLastChange,
                    state: null,
                    dueTime: periodMs,
                    period: periodMs);
            }
        }

        /// <summary>The wrapped <see cref="AliasNameClient"/>.</summary>
        public AliasNameClient Client { get; }

        /// <summary>The (cloned, immutable) configuration.</summary>
        public AliasNameResolverOptions Options { get; }

        /// <summary>
        /// Ensures the cache is populated; performs a refresh only on
        /// the first call (or after an invalidation).
        /// </summary>
        public async Task EnsureLoadedAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref m_loaded) == 1)
            {
                return;
            }
            await RefreshAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Force-reloads the alias cache.
        /// </summary>
        public async Task RefreshAsync(CancellationToken ct = default)
        {
            uint? lastChange = await Client.ReadLastChangeAsync(ct).ConfigureAwait(false);

            var forward = new Dictionary<string, ExpandedNodeId[]>(StringComparer.Ordinal);
            var serverUris = new Dictionary<string, string?[]>(StringComparer.Ordinal);
            var reverse = new Dictionary<ExpandedNodeId, string>();

            if (Options.UseVerbose)
            {
                IReadOnlyList<AliasNameVerboseDataType> verbose;
                try
                {
                    verbose = await Client.FindAliasVerboseAsync(
                        "%", NodeId.Null, ct).ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    verbose = [];
                    Options.UseVerbose = false; // fall back to non-verbose
                }
                if (verbose.Count == 0 && !Options.UseVerbose)
                {
                    // try non-verbose fallback
                    IReadOnlyList<AliasNameDataType> nonVerbose =
                        await Client.FindAliasAsync("%", NodeId.Null, ct)
                            .ConfigureAwait(false);
                    PopulateFromNonVerbose(nonVerbose, forward, reverse);
                }
                else
                {
                    PopulateFromVerbose(verbose, forward, serverUris, reverse);
                }
            }
            else
            {
                IReadOnlyList<AliasNameDataType> aliases =
                    await Client.FindAliasAsync("%", NodeId.Null, ct)
                        .ConfigureAwait(false);
                PopulateFromNonVerbose(aliases, forward, reverse);
            }

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_forward = forward;
                m_serverUris = serverUris;
                m_reverse = reverse;
                m_lastSeenChange = lastChange;
                Volatile.Write(ref m_loaded, 1);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Returns every <see cref="ExpandedNodeId"/> mapped to the
        /// alias name <paramref name="aliasName"/>, or an empty list
        /// when no mapping exists. Loads the cache on demand.
        /// </summary>
        public async Task<IReadOnlyList<ExpandedNodeId>> ResolveAsync(
            string aliasName,
            CancellationToken ct = default)
        {
            if (aliasName == null)
            {
                throw new ArgumentNullException(nameof(aliasName));
            }
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_forward.TryGetValue(aliasName, out ExpandedNodeId[]? targets))
                {
                    return targets;
                }
                return [];
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Reverse lookup — returns the alias name for the supplied
        /// target NodeId, or <c>null</c> if no alias points at it. Loads
        /// the cache on demand.
        /// </summary>
        public async Task<string?> ResolveAliasNameAsync(
            ExpandedNodeId target,
            CancellationToken ct = default)
        {
            if (target.IsNull)
            {
                return null;
            }
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_reverse.TryGetValue(target, out string? name))
                {
                    return name;
                }
                return null;
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Returns the server URIs associated with the named alias's
        /// targets (parallel to the result of
        /// <see cref="ResolveAsync"/>). Only populated when the
        /// resolver was loaded via
        /// <see cref="AliasNameResolverOptions.UseVerbose"/>; returns an
        /// empty list otherwise.
        /// </summary>
        public async Task<IReadOnlyList<string?>> ResolveServerUrisAsync(
            string aliasName,
            CancellationToken ct = default)
        {
            if (aliasName == null)
            {
                throw new ArgumentNullException(nameof(aliasName));
            }
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_serverUris.TryGetValue(aliasName, out string?[]? uris))
                {
                    return uris;
                }
                return [];
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Invalidates the cache; the next resolve will refresh.
        /// </summary>
        public void Invalidate()
        {
            Volatile.Write(ref m_loaded, 0);
        }

        /// <summary>
        /// Stops auto-refresh polling (if enabled) and releases the
        /// internal cache. Idempotent.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Timer? timer = m_pollTimer;
            m_pollTimer = null;
            timer?.Dispose();
            m_semaphore.Dispose();
            return default;
        }

        private void PollLastChange(object? _)
        {
            // Fire-and-forget; exceptions are swallowed so a broken
            // server cannot crash the resolver.
            _ = PollLastChangeAsync();
        }

        private async Task PollLastChangeAsync()
        {
            try
            {
                uint? current = await Client.ReadLastChangeAsync().ConfigureAwait(false);
                if (current == null)
                {
                    return;
                }
                uint? seen = m_lastSeenChange;
                // Compare on inequality only — VersionTime may wrap and
                // a strict greater-than would miss the wraparound case.
                if (seen != current)
                {
                    Invalidate();
                }
            }
            catch
            {
                // Ignore transient errors; the next poll cycle will retry.
            }
        }

        private static void PopulateFromNonVerbose(
            IReadOnlyList<AliasNameDataType> aliases,
            Dictionary<string, ExpandedNodeId[]> forward,
            Dictionary<ExpandedNodeId, string> reverse)
        {
            foreach (AliasNameDataType a in aliases)
            {
                string key = a.AliasName.Name ?? string.Empty;
                int count = a.ReferencedNodes.Count;
                var arr = new ExpandedNodeId[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = a.ReferencedNodes[i];
                    reverse[arr[i]] = key;
                }
                forward[key] = arr;
            }
        }

        private static void PopulateFromVerbose(
            IReadOnlyList<AliasNameVerboseDataType> aliases,
            Dictionary<string, ExpandedNodeId[]> forward,
            Dictionary<string, string?[]> serverUris,
            Dictionary<ExpandedNodeId, string> reverse)
        {
            foreach (AliasNameVerboseDataType a in aliases)
            {
                string key = a.AliasName.Name ?? string.Empty;
                int count = a.ReferencedNodes.Count;
                var arr = new ExpandedNodeId[count];
                var uris = new string?[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = a.ReferencedNodes[i];
                    uris[i] = i < a.ServerUris.Count ? a.ServerUris[i] : null;
                    reverse[arr[i]] = key;
                }
                forward[key] = arr;
                serverUris[key] = uris;
            }
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private Dictionary<string, ExpandedNodeId[]> m_forward
            = new(StringComparer.Ordinal);
        private Dictionary<string, string?[]> m_serverUris
            = new(StringComparer.Ordinal);
        private Dictionary<ExpandedNodeId, string> m_reverse = [];
        private uint? m_lastSeenChange;
        private int m_loaded;
        private Timer? m_pollTimer;
    }
}
