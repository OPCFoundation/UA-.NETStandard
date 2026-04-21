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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;

#nullable enable

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that hosts an OPC UA
    /// <see cref="StandardServer"/> within a .NET Generic Host. Owns the
    /// <see cref="ApplicationInstance"/> lifetime, builds the configuration
    /// from <see cref="OpcUaServerOptions"/>, and registers every
    /// <see cref="IAsyncNodeManagerFactory"/>/<see cref="INodeManagerFactory"/>
    /// resolved from DI before starting the server.
    /// </summary>
    internal sealed class OpcUaServerHostedService : BackgroundService
    {
        private readonly OpcUaServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly IEnumerable<IAsyncNodeManagerFactory> m_asyncFactories;
        private readonly IEnumerable<INodeManagerFactory> m_syncFactories;
        private readonly ILogger<OpcUaServerHostedService> m_logger;
        private ApplicationInstance? m_application;
        private StandardServer? m_server;

        public OpcUaServerHostedService(
            OpcUaServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<IAsyncNodeManagerFactory> asyncFactories,
            IEnumerable<INodeManagerFactory> syncFactories,
            ILogger<OpcUaServerHostedService> logger)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_asyncFactories = asyncFactories ?? throw new ArgumentNullException(nameof(asyncFactories));
            m_syncFactories = syncFactories ?? throw new ArgumentNullException(nameof(syncFactories));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string appName = string.IsNullOrEmpty(m_options.ApplicationName)
                ? "OpcUaServer"
                : m_options.ApplicationName;

            string pkiRoot = string.IsNullOrEmpty(m_options.PkiRoot)
                ? Path.Combine(Path.GetTempPath(), "OPC Foundation", appName, "pki")
                : m_options.PkiRoot;

            string subject = string.IsNullOrEmpty(m_options.SubjectName)
                ? $"CN={appName}, O=OPC Foundation, DC=localhost"
                : m_options.SubjectName;

            m_application = new ApplicationInstance(m_telemetry)
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Server
            };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subject, CertificateStoreType.Directory, pkiRoot);

            string[] urls = new string[m_options.EndpointUrls.Count];
            m_options.EndpointUrls.CopyTo(urls, 0);

            IApplicationConfigurationBuilderServerSelected serverBuilder = m_application
                .Build(m_options.ApplicationUri, m_options.ProductUri)
                .SetMaxByteStringLength((int)m_options.MaxByteStringLength)
                .SetMaxArrayLength((int)m_options.MaxArrayLength)
                .AsServer(urls);

            if (m_options.IncludeSignAndEncryptPolicies)
            {
                serverBuilder = serverBuilder.AddSignAndEncryptPolicies();
            }

            if (m_options.IncludeUnsecurePolicyNone)
            {
                serverBuilder = serverBuilder.AddUnsecurePolicyNone();
            }

            IApplicationConfigurationBuilderServerOptions optionsBuilder =
                serverBuilder.SetDiagnosticsEnabled(m_options.DiagnosticsEnabled);

            m_options.ConfigureBuilder?.Invoke(serverBuilder);

            await optionsBuilder
                .AddSecurityConfiguration(certs, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates)
                .CreateAsync()
                .ConfigureAwait(false);

            bool haveCert = await m_application
                .CheckApplicationInstanceCertificatesAsync(
                    silent: true, CertificateFactory.DefaultLifeTime, stoppingToken)
                .ConfigureAwait(false);
            if (!haveCert)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            m_server = new StandardServer(m_telemetry);
            foreach (IAsyncNodeManagerFactory factory in m_asyncFactories)
            {
                m_server.AddNodeManager(factory);
            }
            foreach (INodeManagerFactory factory in m_syncFactories)
            {
                m_server.AddNodeManager(factory);
            }

            await m_application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);

            foreach (string url in urls)
            {
                m_logger.LogInformation("OPC UA server listening at {Endpoint}.", url);
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
                m_logger.LogInformation("Stopping OPC UA server...");
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Error while stopping OPC UA server.");
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
