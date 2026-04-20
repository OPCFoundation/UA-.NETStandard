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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Quickstarts.ConsoleBoilerServer
{
    /// <summary>
    /// Self-contained NativeAOT OPC UA server driven entirely by the OPC
    /// UA model source generator and the fluent NodeManager wiring API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Boiler companion-spec design files (<c>Generated/BoilerDesign.xml</c>
    /// and the matching CSV) are processed at build time by the source
    /// generator; with
    /// <c>ModelSourceGeneratorGenerateNodeManager=true</c> set in the
    /// project, the generator emits a partial
    /// <see cref="global::Boiler.BoilerNodeManager"/> + an
    /// <see cref="INodeManagerFactory"/> alongside the usual
    /// <c>NodeState</c> / <c>DataType</c> code. A sibling partial in
    /// <c>BoilerNodeManager.Configure.cs</c> wires per-node callbacks
    /// using the fluent builder from <c>Opc.Ua.Server.Fluent</c>.
    /// </para>
    /// <para>
    /// All configuration is built in code (no XML) so the application
    /// stays AOT-friendly. Logging uses
    /// <c>Microsoft.Extensions.Logging.Console</c>.
    /// </para>
    /// </remarks>
    public static class Program
    {
        private const string kApplicationName = "ConsoleBoilerServer";
        private const string kApplicationUri = "urn:localhost:OPCFoundation:ConsoleBoilerServer";
        private const string kProductUri = "uri:opcfoundation.org:ConsoleBoilerServer";

        public static async Task<int> Main(string[] args)
        {
            int port = ParsePort(args, defaultValue: 62541);

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole());

            ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
                builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole());

            ILogger logger = loggerFactory.CreateLogger("ConsoleBoilerServer");

            try
            {
                await RunAsync(port, telemetry, logger).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Server terminated with an error.");
                return 1;
            }
        }

        private static async Task RunAsync(
            int port,
            ITelemetryContext telemetry,
            ILogger logger)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OPC Foundation", "ConsoleBoilerServer", "pki");
            string endpointUrl = $"opc.tcp://localhost:{port}/{kApplicationName}";

            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = kApplicationName,
                ApplicationType = ApplicationType.Server
            };

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    $"CN={kApplicationName}, O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    pkiRoot);

            ApplicationConfiguration configuration = await application
                .Build(kApplicationUri, kProductUri)
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .AsServer([endpointUrl])
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                .AddSignAndEncryptPolicies()
                .AddUnsecurePolicyNone()
                .SetDiagnosticsEnabled(true)
                .AddSecurityConfiguration(applicationCerts, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync()
                .ConfigureAwait(false);

            bool haveAppCertificate = await application
                .CheckApplicationInstanceCertificatesAsync(
                    silent: true, CertificateFactory.DefaultLifeTime)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            var server = new BoilerStandardServer(telemetry);
            await application.StartAsync(server).ConfigureAwait(false);

            logger.LogInformation(
                "Server listening at {Endpoint}. Press Ctrl+C to stop.", endpointUrl);

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };

            try
            {
                await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // expected on Ctrl+C
            }

            logger.LogInformation("Stopping server...");
            await server.StopAsync().ConfigureAwait(false);
            server.Dispose();
        }

        private static int ParsePort(string[] args, int defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int parsed))
                {
                    return parsed;
                }
            }
            return defaultValue;
        }
    }

    /// <summary>
    /// Minimal <see cref="StandardServer"/> subclass that registers the
    /// generated <see cref="global::Boiler.BoilerNodeManagerFactory"/>.
    /// </summary>
    /// <remarks>
    /// Registration happens in <see cref="OnServerStarting"/> so the
    /// factory is available before the address space is built.
    /// </remarks>
    internal sealed class BoilerStandardServer : StandardServer
    {
        public BoilerStandardServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);
            AddNodeManager(new global::Boiler.BoilerNodeManagerFactory());
        }
    }
}
