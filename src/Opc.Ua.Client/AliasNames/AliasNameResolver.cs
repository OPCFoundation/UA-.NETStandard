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
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.AliasNames.Refresh;

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
    /// <see cref="AliasNameResolverRefreshMode.AutoOnLastChangePolling"/>
    /// (read-based) or
    /// <see cref="AliasNameResolverRefreshMode.AutoOnLastChangeMonitoredItem"/>
    /// (subscription-based). Custom strategies — e.g. a Part 17
    /// Annex D PubSub bridge — can be plugged in via
    /// <see cref="AliasNameResolverOptions.RefreshStrategy"/>.
    /// </para>
    /// <para>
    /// The resolver is <see cref="IAsyncDisposable"/> — disposing it
    /// stops the active refresh strategy and releases any cached state.
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
            m_strategy = Options.RefreshStrategy
                ?? BuildBuiltInStrategy(Options);
            m_logger = client.Session.MessageContext.Telemetry
                .CreateLogger<AliasNameResolver>();
        }

        /// <summary>The wrapped <see cref="AliasNameClient"/>.</summary>
        public AliasNameClient Client { get; }

        /// <summary>
        /// The (cloned, immutable) configuration.
        /// </summary>
        public AliasNameResolverOptions Options { get; }

        /// <summary>
        /// Ensures the cache is populated; performs a refresh only on
        /// the first call (or after an invalidation). On the first call
        /// the configured <see cref="IAliasNameRefreshStrategy"/> is
        /// also started.
        /// </summary>
        public async Task EnsureLoadedAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref m_loadedGeneration) ==
                Interlocked.Read(ref m_invalidationGeneration))
            {
                return;
            }
            try
            {
                await EnsureStrategyStartedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException ||
                !ct.IsCancellationRequested)
            {
                // The refresh strategy only drives invalidation; a server that
                // rejects its subscription (or lacks the optional LastChange)
                // must not make alias resolution fail. Run degraded (manual
                // refresh) and retry the start on the next load.
                m_logger.AliasRefreshStrategyStartFailed(ex, m_strategy.GetType().Name);
            }
            await RefreshAsync(ct).ConfigureAwait(false);
        }

        private async Task EnsureStrategyStartedAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_strategyStarted) == 1)
            {
                return;
            }
            await m_strategyStartLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_strategyStarted) == 1)
                {
                    return;
                }
                await m_strategy.StartAsync(Client, Invalidate, ct)
                    .ConfigureAwait(false);
                Volatile.Write(ref m_strategyStarted, 1);
            }
            finally
            {
                m_strategyStartLock.Release();
            }
        }

        /// <summary>
        /// Force-reloads the alias cache.
        /// </summary>
        public async Task RefreshAsync(CancellationToken ct = default)
        {
            // Capture the invalidation generation before the fetch: an
            // Invalidate that lands while it is in flight must not be undone by
            // marking the (already stale) result as loaded.
            long generation = Interlocked.Read(ref m_invalidationGeneration);

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

                // Publish the generation this data was fetched for rather than
                // a separate "loaded" flag: an Invalidate that lands between a
                // check and the flag write would otherwise be overwritten and
                // mark pre-invalidation data as current forever. A reader
                // compares the two generations, so the single write below can
                // never swallow an invalidation.
                Volatile.Write(ref m_loadedGeneration, generation);
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
        /// <exception cref="ArgumentNullException">
        /// <paramref name="aliasName"/> is null.
        /// </exception>
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
        /// <exception cref="ArgumentNullException">
        /// <paramref name="aliasName"/> is null.
        /// </exception>
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
            // One atomic step: a refresh that is already fetching publishes the
            // generation it fetched for, which no longer matches this one, so
            // its stale result can neither be served nor swallow this
            // invalidation.
            Interlocked.Increment(ref m_invalidationGeneration);
        }

        /// <summary>
        /// Stops the configured refresh strategy and releases the
        /// internal cache. Idempotent. Waits for any in-flight resolve /
        /// refresh / poll callback to release the internal lock before
        /// disposing it so concurrent calls cannot observe an
        /// <see cref="ObjectDisposedException"/>.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_strategy.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup; the strategy implementation is
                // responsible for not throwing during dispose, but we
                // never want disposal of the resolver to throw.
            }

            try
            {
                await m_semaphore.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed via a re-entrant call.
                return;
            }
            try
            {
                m_forward.Clear();
                m_serverUris.Clear();
                m_reverse.Clear();
            }
            finally
            {
                m_semaphore.Release();
                m_semaphore.Dispose();
                m_strategyStartLock.Dispose();
            }
        }

        private static IAliasNameRefreshStrategy BuildBuiltInStrategy(
            AliasNameResolverOptions options)
        {
#pragma warning disable CS0618 // AutoOnLastChange aliases AutoOnLastChangePolling.
            switch (options.RefreshMode)
            {
                case AliasNameResolverRefreshMode.AutoOnLastChangePolling:
                    return new PollingAliasNameRefreshStrategy(
                        TimeSpan.FromMilliseconds(
                            Math.Max(100, options.PublishingIntervalMs)));
                case AliasNameResolverRefreshMode.AutoOnLastChangeMonitoredItem:
                    return new MonitoredItemAliasNameRefreshStrategy(
                        new MonitoredItemAliasNameRefreshStrategyOptions
                        {
                            PublishingIntervalMs = options.PublishingIntervalMs,
                            SamplingIntervalMs = options.LastChangeSamplingIntervalMs
                        });
                default:
                    return new ManualAliasNameRefreshStrategy();
            }
#pragma warning restore CS0618
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
                forward[key] = Merge(forward, key, arr);
            }
        }

        /// <summary>
        /// The cache is keyed by <see cref="QualifiedName.Name"/> only, but
        /// FindAlias may legally return the same name more than once (other
        /// namespace, or defined in several sub-categories). Append the
        /// targets of a repeated name instead of replacing the earlier ones.
        /// </summary>
        /// <typeparam name="T">The element type of the cached arrays.</typeparam>
        private static T[] Merge<T>(Dictionary<string, T[]> map, string key, T[] values)
        {
            if (!map.TryGetValue(key, out T[]? existing) || existing.Length == 0)
            {
                return values;
            }
            var merged = new T[existing.Length + values.Length];
            Array.Copy(existing, merged, existing.Length);
            Array.Copy(values, 0, merged, existing.Length, values.Length);
            return merged;
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
                string?[] uris = new string?[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = a.ReferencedNodes[i];
                    uris[i] = i < a.ServerUris.Count ? a.ServerUris[i] : null;
                    reverse[arr[i]] = key;
                }
                // Merge both maps the same way so the ServerUris stay
                // parallel to the targets.
                forward[key] = Merge(forward, key, arr);
                serverUris[key] = Merge(serverUris, key, uris);
            }
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private readonly SemaphoreSlim m_strategyStartLock = new(1, 1);
        private readonly IAliasNameRefreshStrategy m_strategy;
        private readonly ILogger m_logger;

        private Dictionary<string, ExpandedNodeId[]> m_forward
            = new(StringComparer.Ordinal);

        private Dictionary<string, string?[]> m_serverUris
            = new(StringComparer.Ordinal);

        private Dictionary<ExpandedNodeId, string> m_reverse = [];

        /// <summary>
        /// The <see cref="m_invalidationGeneration"/> the cached data was
        /// fetched for. The cache is loaded exactly while this equals the
        /// current generation; <c>-1</c> is "never loaded", which no
        /// generation can collide with.
        /// </summary>
        private long m_loadedGeneration = -1;

        /// <summary>
        /// Incremented by every <see cref="Invalidate"/> so an in-flight
        /// <see cref="RefreshAsync"/> can tell whether its result is still
        /// current when it completes.
        /// </summary>
        private long m_invalidationGeneration;
        private int m_strategyStarted;
    }

    /// <summary>
    /// Source-generated logging for <see cref="AliasNameResolver"/>.
    /// </summary>
    internal static partial class AliasNameResolverLog
    {
        [LoggerMessage(EventId = ClientEventIds.AliasNameResolver + 0, Level = LogLevel.Warning,
            Message = "Alias refresh strategy {Strategy} failed to start; resolving without " +
                "automatic invalidation and retrying the start on the next load.")]
        public static partial void AliasRefreshStrategyStartFailed(
            this ILogger logger,
            Exception exception,
            string strategy);
    }
}
