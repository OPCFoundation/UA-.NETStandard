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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;

#nullable enable

namespace Opc.Ua.Lds.Server.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that hosts an OPC UA
    /// <see cref="LdsServer"/> within a .NET Generic Host. Owns the
    /// <see cref="IApplicationInstance"/> lifetime, builds the configuration
    /// from <see cref="LdsServerOptions"/>, and starts an LDS that derives
    /// from <c>DiscoveryServerBase</c>. When
    /// <see cref="LdsServerOptions.EnableMulticast"/> is set, the LDS-ME
    /// multicast layer is attached via <see cref="LdsServer.MulticastFactory"/>
    /// so it starts and stops with the server.
    /// </summary>
    internal sealed class LdsServerHostedService : BackgroundService
    {
        private readonly LdsServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly IApplicationInstanceFactory m_applicationFactory;
        private readonly ILogger<LdsServerHostedService> m_logger;
        // CA2213: ApplicationInstance is IAsyncDisposable; the lifecycle here is
        // managed via the async StopAsync override which calls m_application.StopAsync.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private LdsServer? m_server;

        public LdsServerHostedService(
            IOptions<LdsServerOptions> options,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            ILogger<LdsServerHostedService> logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_applicationFactory = applicationFactory ?? throw new ArgumentNullException(nameof(applicationFactory));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string appName = string.IsNullOrEmpty(m_options.ApplicationName)
                ? "LdsServer"
                : m_options.ApplicationName;

            string pkiRoot = string.IsNullOrEmpty(m_options.PkiRoot)
                ? Path.Combine(Path.GetTempPath(), "OPC Foundation", appName, "pki")
                : m_options.PkiRoot;

            string subject = string.IsNullOrEmpty(m_options.SubjectName)
                ? $"CN={appName}, O=OPC Foundation, DC=localhost"
                : m_options.SubjectName;

            m_application = m_applicationFactory.Create(m_telemetry);
            m_application.ApplicationName = appName;
            // Build the configuration as a regular Server (the
            // ApplicationConfigurationBuilder rejects DiscoveryServer in
            // AsServer); we promote to DiscoveryServer after CreateAsync
            // so the LDS is correctly advertised per OPC 10000-12.
            m_application.ApplicationType = ApplicationType.Server;

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subject, CertificateStoreType.Directory, pkiRoot);

            string[] urls = new string[m_options.EndpointUrls.Count];
            m_options.EndpointUrls.CopyTo(urls, 0);

            IApplicationConfigurationBuilderServerSelected serverBuilder = m_application
                .Build(m_options.ApplicationUri, m_options.ProductUri)
                .AsServer(urls);

            m_options.ConfigureBuilder?.Invoke(serverBuilder);

            ApplicationConfiguration configuration = await serverBuilder
                .AddSecurityConfiguration(certs, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates)
                .CreateAsync(stoppingToken)
                .ConfigureAwait(false);

            // Promote to DiscoveryServer per OPC 10000-12 Part 12 so the
            // LDS self-describes correctly to peers (FindServers, mDNS).
            configuration.ApplicationType = ApplicationType.DiscoveryServer;
            m_application.ApplicationType = ApplicationType.DiscoveryServer;

            bool haveCert = await m_application
                .CheckApplicationInstanceCertificatesAsync(
                    silent: true, CertificateFactory.DefaultLifeTime, stoppingToken)
                .ConfigureAwait(false);
            if (!haveCert)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            m_server = new LdsServer(m_telemetry);

            if (m_options.EnableMulticast)
            {
                ILogger multicastLogger = m_telemetry.CreateLogger<MulticastDiscovery>();
                m_server.MulticastFactory = lds => new MulticastDiscovery(
                    lds.Store,
                    loopbackOnly: false,
                    logger: multicastLogger);
            }

            m_server.Store.StartPruneTimer();

            await m_application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);

            foreach (string url in urls)
            {
                m_logger.LogInformation("OPC UA LDS listening at {Endpoint}.", url);
            }

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on host shutdown.
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);

            if (m_application != null)
            {
                m_logger.LogInformation("Stopping OPC UA LDS...");
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Error while stopping OPC UA LDS.");
                }
            }
        }

        public override void Dispose()
        {
            m_server?.Dispose();
            base.Dispose();
        }
    }
}
