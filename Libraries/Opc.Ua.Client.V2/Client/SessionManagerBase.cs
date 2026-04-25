// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using BitFaster.Caching;
    using BitFaster.Caching.Lfu;
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Configuration;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Session manager
    /// </summary>
    internal abstract class SessionManagerBase : ClientApplicationBase,
        ISessionManager
    {
        /// <summary>
        /// Reverse connect manager
        /// </summary>
        public Sessions.ReverseConnectManager ReverseConnectManager { get; }

        /// <summary>
        /// Create connection manager
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="applicationUri"></param>
        /// <param name="productUri"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        protected SessionManagerBase(ApplicationInstance instance, string applicationUri,
            string productUri, ClientOptions options, ITelemetryContext telemetry) :
            base(instance, applicationUri, productUri, options, telemetry)
        {
            _options = options;
            _observability = telemetry;
            _logger = telemetry.LoggerFactory.CreateLogger<SessionManagerBase>();

            // Connection pooling of managed sessions
            _pool = new ConcurrentLfuBuilder<PooledSessionOptions, Opc.Ua.Client.Sessions.Session>()
                .WithAtomicGetOrAdd()
                .WithKeyComparer(new PooledSessionOptionsComparer())
                .WithCapacity(options.MaxPooledSessions)
                .WithExpireAfterAccess(options.LingerTimeout)
                .AsAsyncCache()
                .AsScopedCache()
                .Build();

            ReverseConnectManager = new Sessions.ReverseConnectManager(telemetry.LoggerFactory);
            _reverseConnectStartException = new Lazy<Exception?>(
                StartReverseConnectManager, isThreadSafe: true);
            InitializeMetrics();
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> TestAsync(EndpointDescription endpoint,
            bool useReverseConnect = false, CancellationToken ct = default)
        {
            try
            {
                using var session = await ConnectCoreAsync(endpoint,
                    new SessionCreateOptions { SessionName = "Test" + Guid.NewGuid() },
                    useReverseConnect, ct).ConfigureAwait(false);
                try
                {
                    await session.CloseAsync(true, true, ct).ConfigureAwait(false);
                }
                catch
                {
                    // We close as a courtesy to the server
                }
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                return new ServiceResult(ex);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ISessionHandle> GetOrConnectAsync(PooledSessionOptions connection,
            CancellationToken ct)
        {
            // Lazy start connect manager
            var reverseConnect = connection.UseReverseConnect;
            if (reverseConnect && _reverseConnectStartException.Value != null)
            {
                throw _reverseConnectStartException.Value;
            }

            if (_pool.ScopedTryGet(connection, out var lifetime))
            {
                return ValueTask.FromResult<ISessionHandle>(new Pooled(lifetime));
            }

            return GetOrConnectAsyncCore(connection, ct);
            async ValueTask<ISessionHandle> GetOrConnectAsyncCore(PooledSessionOptions connection,
                CancellationToken ct)
            {
                return new Pooled(await _pool.ScopedGetOrAddAsync(connection,
                    ConnectAsync, (this, ct)).ConfigureAwait(false));
                static async Task<Scoped<Opc.Ua.Client.Sessions.Session>> ConnectAsync(PooledSessionOptions key,
                    (SessionManagerBase client, CancellationToken ct) context)
                {
                    var session = await context.client.ConnectWithResiliencyAsync(key.Endpoint,
                        new SessionCreateOptions
                        {
                            SessionName =
                                key.SessionOptions?.SessionName ?? "Default",
                            Identity =
                                key.User,
                            KeepAliveInterval =
                                key.SessionOptions?.KeepAliveInterval,
                            CheckDomain =
                                key.SessionOptions?.CheckDomain ?? false,
                            DisableDataTypeDictionary =
                                key.SessionOptions?.DisableDataTypeDictionary ?? false,
                            EnableComplexTypePreloading =
                                key.SessionOptions?.EnableComplexTypePreloading ?? false,
                            PreferredLocales =
                                key.SessionOptions?.PreferredLocales,
                            SessionTimeout =
                                key.SessionOptions?.SessionTimeout,
                            ReconnectStrategy =
                                context.client._options.RetryStrategy
                        }, key.UseReverseConnect, context.ct).ConfigureAwait(false);
                    return new Scoped<Opc.Ua.Client.Sessions.Session>(session);
                }
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<ISessionHandle> ConnectAsync(EndpointDescription endpoint,
            SessionCreateOptions? options = null, bool useReverseConnect = false,
            CancellationToken ct = default)
        {
            var session = await ConnectWithResiliencyAsync(endpoint, options,
                useReverseConnect, ct).ConfigureAwait(false);
            return new Unpooled(session);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _logger.LogInformation("Stopping all {Count} sessions...", _pool.Count);
                foreach (var client in _pool)
                {
                    try
                    {
                        // await client.Value.Dispose(); // TODO
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected exception disposing client {Name}",
                            client.Key);
                    }
                }
                _pool.Clear();
                _logger.LogInformation("Stopped all sessions, current number of clients is 0");
                ReverseConnectManager.Dispose();
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Create session
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="endpoint"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        protected abstract Opc.Ua.Client.Sessions.Session CreateSession(ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint, SessionCreateOptions options, ITelemetryContext telemetry);

        /// <summary>
        /// Connect with resiliency applied
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="options"></param>
        /// <param name="useReverseConnect"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask<Opc.Ua.Client.Sessions.Session> ConnectWithResiliencyAsync(EndpointDescription endpoint,
            SessionCreateOptions? options, bool useReverseConnect, CancellationToken ct)
        {
            if (_options.RetryStrategy != null)
            {
                return await _options.RetryStrategy.ExecuteAsync(
                    (state, ct) => ConnectCoreAsync(
                        state.endpoint, state.options, state.useReverseConnect, ct),
                    (endpoint, options, useReverseConnect), ct).ConfigureAwait(false);
            }
            return await ConnectCoreAsync(endpoint, options, useReverseConnect,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Connect a session without resiliency applied
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="options"></param>
        /// <param name="useReverseConnect"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask<Opc.Ua.Client.Sessions.Session> ConnectCoreAsync(EndpointDescription endpoint,
            SessionCreateOptions? options, bool useReverseConnect, CancellationToken ct)
        {
            ITransportWaitingConnection? connection = null;

            options ??= new SessionCreateOptions { SessionName = Guid.NewGuid().ToString() };
            if (useReverseConnect)
            {
                connection = await ReverseConnectManager.WaitForConnectionAsync(
                    new Uri(endpoint.EndpointUrl!), null, ct).ConfigureAwait(false);
                options = options with { Connection = connection };
            }

            var configuration = await GetConfigurationAsync().ConfigureAwait(false);
            var configuredEndpoint = await SelectEndpointAsync(configuration, endpoint,
                null, connection, ct).ConfigureAwait(false);
            var session = CreateSession(configuration, configuredEndpoint, options, _observability);
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                session.Dispose();
                throw;
            }
            return session;
        }

        /// <summary>
        /// Select the endpoint to use
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="endpoint"></param>
        /// <param name="discoveryUrl"></param>
        /// <param name="connection"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        internal async Task<ConfiguredEndpoint> SelectEndpointAsync(ApplicationConfiguration configuration,
            EndpointDescription endpoint, Uri? discoveryUrl, ITransportWaitingConnection? connection,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout =
                (int)TimeSpan.FromSeconds(15).TotalMilliseconds;

            // needs to add the /discovery onto http urls
            if (connection == null)
            {
                if (discoveryUrl == null)
                {
                    if (!Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute,
                        out var endpointUrl))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadArgumentsMissing,
                            "No discovery url provided and no valid endpoint url.");
                    }
                    discoveryUrl = endpointUrl;
                }
                if (discoveryUrl.Scheme == Utils.UriSchemeHttp &&
                    !discoveryUrl.AbsolutePath.EndsWith("/discovery",
                        StringComparison.OrdinalIgnoreCase))
                {
                    discoveryUrl = new UriBuilder(discoveryUrl)
                    {
                        Path = discoveryUrl.AbsolutePath.TrimEnd('/') + "/discovery"
                    }.Uri;
                }
            }

            using var client = connection != null ?
                await DiscoveryClient.CreateAsync(configuration, connection, endpointConfiguration, ct: ct).ConfigureAwait(false) :
                await DiscoveryClient.CreateAsync(configuration, discoveryUrl!, endpointConfiguration, ct: ct).ConfigureAwait(false);
            var uri = new Uri(client.Endpoint!.EndpointUrl!);
            var endpoints = await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
            var endpointsList = endpoints.ToArray()!;
            discoveryUrl ??= uri;

            _logger.LogInformation("{Client}: Discovery endpoint {DiscoveryUrl} returned " +
                "endpoints. Selecting endpoint {EndpointUri} with SecurityMode " +
                "{SecurityMode} and {SecurityPolicy} SecurityPolicyUri from:\n{Endpoints}",
                this, discoveryUrl, uri, endpoint.SecurityMode,
                    endpoint.SecurityPolicyUri ?? "any", endpointsList.Select(
                        ep => "      " + ToString(ep)).Aggregate((a, b) => $"{a}\n{b}"));

            var filtered = endpointsList
                .Where(ep =>
                    SecurityPolicies.GetDisplayName(ep.SecurityPolicyUri) != null &&
                    ep.SecurityMode == endpoint.SecurityMode &&
                    (endpoint.SecurityPolicyUri == null ||
                     string.Equals(ep.SecurityPolicyUri,
                        endpoint.SecurityPolicyUri,
                        StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ep.SecurityPolicyUri,
                        "http://opcfoundation.org/UA/SecurityPolicy#"
                            + endpoint.SecurityPolicyUri,
                        StringComparison.OrdinalIgnoreCase)))
                //
                // The security level is a relative measure assigned by the server
                // to the endpoints that it returns. Clients should always pick the
                // highest level unless they have a reason not too. Some servers
                // however, mess this up a bit. So group SecurityLevel also by
                // security mode and then pick the highest in that group.
                //
                .OrderByDescending(ep => ((int)ep.SecurityMode << 8) | ep.SecurityLevel)
                .ToList();

            //
            // Try to find endpoint that matches scheme and endpoint url path
            // but fall back to match just the scheme. We need to match only
            // scheme to support the reverse connect (indicated by connection
            // being not null here).
            //
            var selected = filtered.Find(ep => Match(ep, uri, true, true))
                        ?? filtered.Find(ep => Match(ep, uri, true, false));
            if (connection != null)
            {
                //
                // Only allow same uri scheme (which must also be opc.tcp)
                // for when reverse connection is used.
                //
                if (selected != null)
                {
                    _logger.LogInformation(
                        "{Client}: Endpoint {Endpoint} selected via reverse connect!",
                        this, ToString(selected));
                }
                return new ConfiguredEndpoint(null, selected,
                    EndpointConfiguration.Create(configuration));
            }

            if (selected == null)
            {
                //
                // Fall back to first supported endpoint matching absolute path
                // then fall back to first endpoint (backwards compatibilty)
                //
                selected = filtered.Find(ep => Match(ep, uri, false, true))
                        ?? filtered.Find(ep => Match(ep, uri, false, false));

                if (selected == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound,
                        "No endpoint found that matches the desired configuration.");
                }
            }

            //
            // Adjust the host name and port to the host name and port
            // that was use to successfully connect the discovery client
            //
            var selectedUrl = Utils.ParseUri(selected.EndpointUrl);
            if (selectedUrl != null && discoveryUrl != null &&
                selectedUrl.Scheme == discoveryUrl.Scheme)
            {
                selected.EndpointUrl = new UriBuilder(selectedUrl)
                {
                    Host = discoveryUrl.DnsSafeHost,
                    Port = discoveryUrl.Port
                }.ToString();
            }

            _logger.LogInformation("{Client}: Endpoint {Endpoint} selected!", this,
                ToString(selected));
            return new ConfiguredEndpoint(null, selected,
                EndpointConfiguration.Create(configuration));

            static string ToString(EndpointDescription ep) =>
    $"#{ep.SecurityLevel:000}: {ep.EndpointUrl}|{ep.SecurityMode} [{ep.SecurityPolicyUri}]";
            // Match endpoint returned against desired endpoint url
            static bool Match(EndpointDescription endpointDescription,
                Uri endpointUrl, bool includeScheme, bool includePath)
            {
                var url = Utils.ParseUri(endpointDescription.EndpointUrl);
                return url != null &&
                    (!includeScheme || string.Equals(url.Scheme,
                        endpointUrl.Scheme, StringComparison.OrdinalIgnoreCase)) &&
                    (!includePath || string.Equals(url.AbsolutePath,
                        endpointUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Start reverse connect manager service
        /// </summary>
        /// <returns></returns>
        private Exception? StartReverseConnectManager()
        {
            var port = _options.ReverseConnectPort ?? 4840;
            try
            {
                ReverseConnectManager.StartService(new ReverseConnectClientConfiguration
                {
                    HoldTime = 120000,
                    WaitTimeout = 120000,
                    ClientEndpoints =
                    [
                        new ReverseConnectClientEndpoint
                        {
                            EndpointUrl = $"opc.tcp://localhost:{port}"
                        }
                    ]
                });
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Create metrics
        /// </summary>
        private void InitializeMetrics()
        {
        }

        /// <summary>
        /// Compare connectivity options
        /// </summary>
        private class PooledSessionOptionsComparer : IEqualityComparer<PooledSessionOptions>
        {
            /// <inheritdoc/>
            public bool Equals(PooledSessionOptions? x, PooledSessionOptions? y)
            {
                if (x?.Endpoint == null || y?.Endpoint == null)
                {
                    return false;
                }
                if (!Utils.IsEqual(x.Endpoint, y.Endpoint) ||
                    !Utils.IsEqual(x.User, y.User) ||
                    x.SessionOptions != y.SessionOptions ||
                    x.UseReverseConnect != y.UseReverseConnect)
                {
                    return false;
                }
                return true;
            }

            /// <inheritdoc/>
            public int GetHashCode([DisallowNull] PooledSessionOptions obj)
            {
                return HashCode.Combine(
                    obj.Endpoint?.EndpointUrl,
                    obj.Endpoint?.SecurityLevel,
                    obj.Endpoint?.TransportProfileUri,
                    obj.Endpoint?.SecurityMode,
                    obj.Endpoint?.SecurityPolicyUri,
                    obj.SessionOptions,
                    obj.UseReverseConnect);
            }
        }

        /// <inheritdoc/>
        private sealed class Unpooled(Opc.Ua.Client.Sessions.Session session) : ISessionHandle
        {
            /// <inheritdoc/>
            public Sessions.ISession Session { get; } = session;

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return session.DisposeAsync();
            }
        }

        /// <summary>
        /// A pooled session wraps a session from the session pool.
        /// Disposing the client returns the session to the pool.
        /// </summary>
        /// <param name="lifetime"></param>
        private sealed class Pooled(Lifetime<Opc.Ua.Client.Sessions.Session> lifetime) : ISessionHandle
        {
            /// <summary>
            /// Session reference valid until disposed.
            /// </summary>
            public Sessions.ISession Session => lifetime.Value;

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                lifetime.Dispose();
                return default;
            }
        }

        private readonly ILogger _logger;
        private readonly ITelemetryContext _observability;
        private readonly ClientOptions _options;
        private readonly Lazy<Exception?> _reverseConnectStartException;
        private readonly IScopedAsyncCache<PooledSessionOptions, Opc.Ua.Client.Sessions.Session> _pool;
        private bool _disposed;
    }
}
