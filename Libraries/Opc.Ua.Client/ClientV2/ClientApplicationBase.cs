#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The basis of a client or server application providing services like managing
    /// the application's private key infrastructure and certificate stores.
    /// </summary>
    internal abstract class ClientApplicationBase : ApplicationBase, IDiscovery,
        ICertificatePasswordProvider, IDisposable
    {
        /// <summary>
        /// Create application
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="applicationUri"></param>
        /// <param name="productUri"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        /// <exception cref="ArgumentException"></exception>
        protected ClientApplicationBase(ApplicationInstance instance, string applicationUri,
            string productUri, ClientOptions options, IV2TelemetryContext telemetry)
            : base(telemetry)
        {
            _options = options;
            _logger = telemetry.LoggerFactory.CreateLogger<ClientApplicationBase>();
            _timeProvider = telemetry.TimeProvider;

            // Install a logger
            var stackLogger = telemetry.LoggerFactory.CreateLogger(
                instance.ApplicationName);
            if (_options.StackLoggingLevel != null)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                Utils.SetLogger(new MaxLevel(stackLogger, _options.StackLoggingLevel.Value));
#pragma warning restore CS0618 // Type or member is obsolete
            }
            _application = BuildAsync(instance, applicationUri, productUri);
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlySet<FoundEndpoint>> FindEndpointsAsync(Uri discoveryUrl,
            IReadOnlyList<string>? locales, CancellationToken ct)
        {
            var configuration = await GetConfigurationAsync().ConfigureAwait(false);
            var results = new HashSet<FoundEndpoint>(EqualityComparer<FoundEndpoint>.Create(
                (e1, e2) =>
                {
                    return e1 != null &&
                        e1.AccessibleEndpointUrl == e2?.AccessibleEndpointUrl &&
                        e1.Capabilities.SetEquals(e2.Capabilities) &&
                        Utils.IsEqual(e1.Description, e2.Description);
                }));
            var visitedUris = new HashSet<string>
            {
                CreateDiscoveryUri(discoveryUrl.ToString(), 4840)
            };
            var queue = new Queue<Tuple<Uri, List<string>>>();
            var localeIds = locales != null ? new ArrayOf<string>(locales.ToArray()) : default;
            queue.Enqueue(Tuple.Create(discoveryUrl, new List<string>()));
            ct.ThrowIfCancellationRequested();
            while (queue.Count > 0)
            {
                var nextServer = queue.Dequeue();
                discoveryUrl = nextServer.Item1;
                var sw = Stopwatch.StartNew();
                _logger.LogDebug("Try finding endpoints at {DiscoveryUrl}...", discoveryUrl);
                try
                {
                    await DiscoverAsync(discoveryUrl, localeIds, nextServer.Item2, 20000,
                        visitedUris, queue, results, configuration, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Exception occurred during FindEndpoints at {DiscoveryUrl}.",
                        discoveryUrl);
                    _logger.LogError("Could not find endpoints at {DiscoveryUrl} " +
                        "due to {Error} (after {Elapsed}).",
                        discoveryUrl, ex.Message, sw.Elapsed);
                    return new HashSet<FoundEndpoint>();
                }
                ct.ThrowIfCancellationRequested();
                _logger.LogDebug("Finding endpoints at {DiscoveryUrl} completed in {Elapsed}.",
                    discoveryUrl, sw.Elapsed);
            }
            return results;
        }

        /// <inheritdoc/>
        public abstract char[] GetPassword(CertificateIdentifier certificateIdentifier);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _application.GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Get application configuration
        /// </summary>
        /// <returns></returns>
        protected override Task<ApplicationConfiguration> GetConfigurationAsync()
        {
            return _application;
        }

        /// <summary>
        /// Discover endpoints
        /// </summary>
        /// <param name="discoveryUrl"></param>
        /// <param name="localeIds"></param>
        /// <param name="caps"></param>
        /// <param name="timeout"></param>
        /// <param name="visitedUris"></param>
        /// <param name="queue"></param>
        /// <param name="result"></param>
        /// <param name="configuration"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal ValueTask DiscoverAsync(Uri discoveryUrl, ArrayOf<string> localeIds,
            List<string> caps, int timeout, HashSet<string> visitedUris,
            Queue<Tuple<Uri, List<string>>> queue, HashSet<FoundEndpoint> result,
            ApplicationConfiguration configuration, CancellationToken ct)
        {
            if (_options.RetryStrategy != null)
            {
                return _options.RetryStrategy.ExecuteAsync(
                    (ct) => DiscoverAsyncCore(discoveryUrl, localeIds, caps,
                        timeout, visitedUris, queue, result,
                        configuration, ct), ct);
            }
            return DiscoverAsyncCore(discoveryUrl, localeIds, caps, timeout, visitedUris,
                queue, result, configuration, ct);

            async ValueTask DiscoverAsyncCore(Uri discoveryUrl, ArrayOf<string> localeIds,
                List<string> caps, int timeout, HashSet<string> visitedUris,
                Queue<Tuple<Uri, List<string>>> queue, HashSet<FoundEndpoint> result,
                ApplicationConfiguration configuration, CancellationToken ct)
            {
                var endpointConfiguration = EndpointConfiguration.Create(configuration);
                endpointConfiguration.OperationTimeout = timeout;
                using var client = await DiscoveryClient.CreateAsync(
                    configuration, discoveryUrl, endpointConfiguration, ct: ct).ConfigureAwait(false);
                //
                // Get endpoints from current discovery server
                //
                var endpoints = await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
                if (endpoints.IsNull || endpoints.Count == 0)
                {
                    _logger.LogDebug("No endpoints at {DiscoveryUrl}...", discoveryUrl);
                    return;
                }
                _logger.LogDebug("Found endpoints at {DiscoveryUrl}...", discoveryUrl);

                foreach (var ep in endpoints.ToArray()!.Where(ep =>
                    ep.Server.ApplicationType != Opc.Ua.ApplicationType.DiscoveryServer))
                {
                    result.Add(new FoundEndpoint(ep, new UriBuilder(ep.EndpointUrl!)
                    {
                        Host = discoveryUrl.DnsSafeHost
                    }.ToString(), new HashSet<string>(caps)));
                }

                //
                // Now Find servers on network.  This might fail for old lds
                // as well as reference servers, then we call FindServers...
                //
                try
                {
                    var response = await client.FindServersOnNetworkAsync(new RequestHeader(),
                        0, 1000, [], ct).ConfigureAwait(false);
                    foreach (var server in response?.Servers ?? [])
                    {
                        var url = CreateDiscoveryUri(server.DiscoveryUrl!, discoveryUrl.Port);
                        if (!visitedUris.Contains(url))
                        {
                            queue.Enqueue(Tuple.Create(discoveryUrl,
                                server.ServerCapabilities.ToList()));
                            visitedUris.Add(url);
                        }
                    }
                }
                catch
                {
                    // Old lds, just continue...
                    _logger.LogDebug("{DiscoveryUrl} does not support ME extension...",
                        discoveryUrl);
                }

                //
                // Call FindServers first to push more unique discovery urls
                // into the discovery queue
                //
                var found = await client.FindServersAsync(null,
                    client.Endpoint!.EndpointUrl, localeIds, default, ct).ConfigureAwait(false);
                if (found?.Servers != null)
                {
                    foreach (var server in found.Servers.ToArray()!.SelectMany(
                        s => s.DiscoveryUrls.ToArray()!))
                    {
                        var url = CreateDiscoveryUri(server, discoveryUrl.Port);
                        if (!visitedUris.Contains(url))
                        {
                            queue.Enqueue(Tuple.Create(discoveryUrl, new List<string>()));
                            visitedUris.Add(url);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create discovery url from string
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="defaultPort"></param>
        private static string CreateDiscoveryUri(string uri, int defaultPort)
        {
            var url = new UriBuilder(uri);
            if (url.Port is 0 or (-1))
            {
                url.Port = defaultPort;
            }
            url.Host = url.Host.Trim('.');
            url.Path = url.Path.Trim('/');
            return url.Uri.ToString();
        }

        /// <summary>
        /// Build application instance
        /// </summary>
        /// <param name="appInstance"></param>
        /// <param name="applicationUri"></param>
        /// <param name="productUri"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        private async Task<ApplicationConfiguration> BuildAsync(ApplicationInstance appInstance,
            string applicationUri, string productUri)
        {
            // Set transport quotas
            var appBuilder = appInstance.Build(applicationUri, productUri)
                .SetTransportQuotas(new TransportQuotas
                {
                    OperationTimeout = (int)_options.Quotas.OperationTimeout.TotalMilliseconds,
                    MaxStringLength = _options.Quotas.MaxStringLength,
                    MaxByteStringLength = _options.Quotas.MaxByteStringLength,
                    MaxArrayLength = _options.Quotas.MaxArrayLength,
                    MaxMessageSize = _options.Quotas.MaxMessageSize,
                    MaxBufferSize = _options.Quotas.MaxBufferSize,
                    ChannelLifetime = (int)_options.Quotas.ChannelLifetime.TotalMilliseconds,
                    SecurityTokenLifetime = (int)_options.Quotas.SecurityTokenLifetime.TotalMilliseconds
                })
                .AsClient()
                .AddSecurityConfiguration(default(ArrayOf<CertificateIdentifier>),
                    _options.Security.PkiRootPath)
                .SetAutoAcceptUntrustedCertificates(
                    _options.Security.AutoAcceptUntrustedCertificates)
                .SetRejectSHA1SignedCertificates(
                    _options.Security.RejectSha1SignedCertificates)
                .SetMinimumCertificateKeySize(
                    _options.Security.MinimumCertificateKeySize)
                .SetAddAppCertToTrustedStore(
                    _options.Security.AddAppCertToTrustedStore)
                .SetRejectUnknownRevocationStatus(
                    _options.Security.RejectUnknownRevocationStatus)
                .AddCertificatePasswordProvider(this);

            var applicationConfiguration = appInstance.ApplicationConfiguration;
            //    applicationConfiguration.SecurityConfiguration.ApplicationCertificate
            //        .ApplyLocalConfig(securityOptions.ApplicationCertificate);
            //    applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates
            //        .ApplyLocalConfig(securityOptions.TrustedPeerCertificates);
            //    applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates
            //        .ApplyLocalConfig(securityOptions.TrustedIssuerCertificates);
            //    applicationConfiguration.SecurityConfiguration.RejectedCertificateStore
            //        .ApplyLocalConfig(securityOptions.RejectedCertificateStore);
            //    applicationConfiguration.SecurityConfiguration.TrustedUserCertificates
            //        .ApplyLocalConfig(securityOptions.TrustedUserCertificates);
            //    applicationConfiguration.SecurityConfiguration.TrustedHttpsCertificates
            //        .ApplyLocalConfig(securityOptions.TrustedHttpsCertificates);
            //    applicationConfiguration.SecurityConfiguration.HttpsIssuerCertificates
            //        .ApplyLocalConfig(securityOptions.HttpsIssuerCertificates);
            //    applicationConfiguration.SecurityConfiguration.UserIssuerCertificates
            //        .ApplyLocalConfig(securityOptions.UserIssuerCertificates);
            //

            // Fix up and build security configuration
            Exception innerException = ServiceResultException.Create(StatusCodes.BadNotConnected,
                "Missing network.");
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var hostname = _options.Security.HostName;
                if (hostname != null && Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
                {
                    hostname = new IPAddress(SHA256.HashData(Encoding.UTF8.GetBytes(hostname))
                        .AsSpan()[..16], 0).ToString();
                }
                var subjectName = _options.Security.ApplicationCertificateSubjectName;
                if (subjectName == null)
                {
                    hostname ??= await GetHostNameAsync().ConfigureAwait(false);
                    subjectName = $"CN={hostname}";
                }
                if (_options.Security.UpdateApplicationFromExistingCert)
                {
                    (applicationUri, appInstance.ApplicationName, hostname, subjectName) =
                        await UpdateFromExistingCertificateAsync(
                            applicationUri, appInstance.ApplicationName, hostname, subjectName,
                            applicationConfiguration.SecurityConfiguration).ConfigureAwait(false);
                }
                hostname ??= await GetHostNameAsync().ConfigureAwait(false);

                // Fixup hostname in application uri and subject names
                applicationUri = applicationUri.Replace("urn:localhost", $"urn:{hostname}",
                    StringComparison.Ordinal);

                applicationConfiguration.ApplicationUri = applicationUri;
                applicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName =
                    Utils.ReplaceDCLocalhost(subjectName, hostname);

                // Allow private keys in this store so user identities can be side loaded
                applicationConfiguration.SecurityConfiguration.TrustedUserCertificates =
                    new TrustedUserCertificateStore();
                try
                {
                    var appConfig = await appBuilder.CreateAsync().ConfigureAwait(false);
                    var ownCertificate =
                        appConfig.SecurityConfiguration.ApplicationCertificate.Certificate;
                    if (ownCertificate == null)
                    {
                        _logger.LogInformation(
                            "No application own certificate found. Creating a self-signed " +
                            "own certificate valid since yesterday for {DefaultLifeTime} months, " +
                            "with a {DefaultKeySize} bit key and {DefaultHashSize} bit hash.",
                            CertificateFactory.DefaultLifeTime,
                            CertificateFactory.DefaultKeySize,
                            CertificateFactory.DefaultHashSize);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Own certificate Subject '{Subject}' (Thumbprint: {Tthumbprint}) loaded.",
                            ownCertificate.Subject, ownCertificate.Thumbprint);
                    }

                    var hasAppCertificate = await appInstance.CheckApplicationInstanceCertificatesAsync(true,
                        CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
                    if (!hasAppCertificate || appInstance.ApplicationConfiguration.SecurityConfiguration
                        .ApplicationCertificate.Certificate == null)
                    {
                        _logger.LogError("Failed to load or create application own certificate.");
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                            "OPC UA application own certificate invalid");
                    }

                    if (ownCertificate == null)
                    {
                        ownCertificate = appInstance.ApplicationConfiguration.SecurityConfiguration
                            .ApplicationCertificate.Certificate;
                        _logger.LogInformation(
                            "Own certificate Subject '{Subject}' (Thumbprint: {Thumbprint}) created.",
                            ownCertificate?.Subject, ownCertificate?.Thumbprint);
                    }
                    await ShowCertificateStoreInformationAsync(appConfig).ConfigureAwait(false);
                    return appConfig;
                }
                catch (Exception e)
                {
                    _logger.LogInformation(
                        "Error {Message} while configuring OPC UA stack - retry...", e.Message);
                    _logger.LogDebug(e, "Detailed error while configuring OPC UA stack.");
                    innerException = e;
                    await Task.Delay(3000).ConfigureAwait(false);
                }
            }

            _logger.LogCritical("Failed to configure OPC UA stack - exit.");
            throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                "OPC UA stack configuration not possible.", innerException);

            async ValueTask<(string, string, string?, string)> UpdateFromExistingCertificateAsync(
                string applicationUri, string appName, string? hostName, string subjectName,
                SecurityConfiguration options)
            {
                try
                {
                    var now = _timeProvider.GetUtcNow();
                    if (options.ApplicationCertificate?.StorePath != null &&
                        options.ApplicationCertificate.StoreType != null)
                    {
                        using var certStore = CertificateStoreIdentifier.CreateStore(
                            options.ApplicationCertificate.StoreType, (Opc.Ua.ITelemetryContext?)null);
                        certStore.Open(options.ApplicationCertificate.StorePath, false);
                        var certs = await certStore.EnumerateAsync().ConfigureAwait(false);
                        var subjects = new List<string>();
                        foreach (var cert in certs.Where(c => c != null).OrderBy(c => c.NotAfter))
                        {
                            // Select first certificate that has valid information
                            var name = cert.SubjectName.EnumerateRelativeDistinguishedNames()
                                .Where(dn => dn.GetSingleElementType().FriendlyName == "CN")
                                .Select(dn => dn.GetSingleElementValue())
                                .FirstOrDefault(dn => dn != null);
                            if (name != null)
                            {
                                appName = name;
                            }
                            var san = cert.FindExtension<X509SubjectAltNameExtension>();
                            var uris = san?.Uris;
                            var hostNames = san?.DomainNames;
                            if (uris != null && hostNames != null &&
                                uris.Count > 0 && hostNames.Count > 0)
                            {
                                return (uris[0], appName, hostNames[0], cert.Subject);
                            }
                            _logger.LogDebug(
                                "Found invalid certificate for {Subject} [{Thumbprint}].",
                                cert.Subject, cert.Thumbprint);
                        }
                    }
                    _logger.LogDebug("Could not find a certificate to take information from.");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to find a certificate to take information from.");
                }
                return (applicationUri, appName, hostName, subjectName);
            }

            async ValueTask<string?> GetHostNameAsync()
            {
                string? hostname = null;
                while (string.IsNullOrWhiteSpace(hostname))
                {
                    // wait with the configuration until network is up
                    if (!NetworkInterface.GetIsNetworkAvailable())
                    {
                        _logger.LogWarning("Network not available...");
                        await Task.Delay(3000).ConfigureAwait(false);
                        continue;
                    }
                    hostname = Utils.GetHostName();
                }
                return hostname;
            }
        }

        /// <summary>
        /// Just log at max level
        /// </summary>
        private sealed class MaxLevel : ILogger
        {
            /// <inheritdoc/>
            public MaxLevel(ILogger logger, LogLevel maxLevel = LogLevel.Error)
            {
                _logger = logger;
                _maxLevel = maxLevel;
            }

            /// <inheritdoc/>
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= _maxLevel)
                {
                    _logger.Log(logLevel, eventId, state, exception, formatter);
                }
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= _maxLevel;
            }

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return _logger.BeginScope(state);
            }

            private readonly ILogger _logger;
            private readonly LogLevel _maxLevel;
        }

        /// <summary>
        /// Override to support private keys
        /// </summary>
        private class TrustedUserCertificateStore : CertificateTrustList
        {
            /// <inheritdoc/>
            public override ICertificateStore OpenStore(Opc.Ua.ITelemetryContext telemetry)
            {
                var store = CreateStore(StoreType, telemetry);
                store.Open(StorePath, false); // Allow private keys
                return store;
            }
        }

        private readonly Task<ApplicationConfiguration> _application;
        private readonly ILogger<ClientApplicationBase> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ClientOptions _options;
    }
}
#endif
