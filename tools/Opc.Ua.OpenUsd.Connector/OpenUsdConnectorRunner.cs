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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Connector
{
    /// <summary>
    /// Runs the generic <see cref="OpenUsdConnector"/>: connects to a running OPC UA
    /// server (e.g. PumpDeviceIntegrationServer), discovers the OpenUSD representation
    /// and bindings via <c>Server/OpenUSD/Representations</c>, and streams live values
    /// into a <see cref="UsdFileSink"/> (an override <c>live.usda</c>). Invoked as
    /// <c>Opc.Ua.OpenUsd.Connector [--server &lt;url&gt;] [--out &lt;live.usda&gt;] [--seconds N]</c>.
    /// </summary>
    public static class OpenUsdConnectorRunner
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string server = GetOption(args, "--server")
                ?? "opc.tcp://localhost:62542/PumpDeviceIntegrationServer";
            string outPath = GetOption(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "live.usda");
            int seconds = int.TryParse(GetOption(args, "--seconds"), out int s) ? s : 0;

            // §5.15 asset content delivery (OU-AssetDelivery): when set, the connector
            // downloads the server's served USD layer closure into this cache directory
            // (verifying each digest) and writes a self-contained stage.usda there, so a
            // viewer renders the twin with no external asset resolver. live.usda is
            // written into the same directory.
            string? cacheDir = GetOption(args, "--fetch-assets");
            if (!string.IsNullOrEmpty(cacheDir))
            {
                Directory.CreateDirectory(cacheDir!);
                outPath = Path.Combine(cacheDir!, "live.usda");
            }

            // Secure by default (spec §9: an authenticated, integrity-protected endpoint
            // with server-certificate trust is required). The --insecure flag opts into
            // an unsecured endpoint and blanket certificate acceptance, which is only
            // appropriate for a localhost demo with self-signed certificates.
            bool insecure = HasFlag(args, "--insecure");

            // Command bindings (UsdToUaCommand) are opt-in and disabled by default
            // (fail-closed). --enable-commands lets the connector actuate the single
            // controllable command binding; --command-value <double> supplies the
            // setpoint to write once at start (demo).
            bool enableCommands = HasFlag(args, "--enable-commands");
            string? commandValueOpt = GetOption(args, "--command-value");

            ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            string pkiRoot = Path.Combine(Path.GetTempPath(), "Opc.Ua.OpenUsd.Connector", Path.GetRandomFileName());
            var config = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Opc.Ua.OpenUsd.Connector",
                ApplicationUri = "urn:localhost:OPCFoundation:Opc.Ua.OpenUsd.Connector",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=Opc.Ua.OpenUsd.Connector, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = insecure
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

            var appInstance = new Opc.Ua.Configuration.ApplicationInstance(config, telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            await appInstance.DisposeAsync().ConfigureAwait(false);
            config.CertificateManager ??= CertificateManagerFactory.Create(config.SecurityConfiguration, telemetry);
            if (insecure)
            {
                // Demo-only: accept any server certificate.
                config.CertificateManager.AcceptError = static (cert, err) => true;
                Console.WriteLine("WARNING: --insecure: using an unsecured endpoint and accepting any server certificate.");
            }

            Console.WriteLine($"Connecting to {server} ...");
            EndpointDescription? endpointDescription = null;
            for (int attempt = 0; attempt < 40 && endpointDescription == null; attempt++)
            {
                try
                {
                    endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                        config, server, useSecurity: !insecure, telemetry, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
            if (endpointDescription == null)
            {
                Console.Error.WriteLine("ERROR: could not reach the server endpoint. Is the server running?");
                return 2;
            }

            var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(config));
            var sessionFactory = new DefaultSessionFactory(telemetry);
            ISession session = await sessionFactory.CreateAsync(
                config, endpoint, updateBeforeConnect: false,
                sessionName: "Opc.Ua.OpenUsd.Connector", sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default, ct: CancellationToken.None).ConfigureAwait(false);

            var sink = new UsdFileSink(outPath);
            var connector = new OpenUsdConnector(session, sink, enableCommands);
            try
            {
                if (!string.IsNullOrEmpty(cacheDir))
                {
                    List<OpenUsdConnector.FetchedAsset> fetched =
                        await connector.FetchServedAssetsAsync(cacheDir!, CancellationToken.None).ConfigureAwait(false);
                    if (fetched.Count > 0)
                    {
                        WriteStageUsda(cacheDir!, fetched);
                        Console.WriteLine(
                            $"Fetched {fetched.Count} server-delivered USD layer(s) into {cacheDir}; " +
                            "wrote a self-contained stage.usda (open it in usdview).");
                    }
                    else
                    {
                        Console.WriteLine(
                            "Server does not advertise served assets (OU-AssetDelivery); using the external base asset.");
                    }
                }

                await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"Streaming live OPC UA values into {outPath}. Press Ctrl+C to stop.");

                if (enableCommands && commandValueOpt != null
                    && double.TryParse(commandValueOpt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double commandValue))
                {
                    bool ok = await connector.IssueCommandAsync(commandValue, CancellationToken.None)
                        .ConfigureAwait(false);
                    Console.WriteLine(ok
                        ? $"Command issued: setpoint <- {commandValue}."
                        : "Command binding not found or write rejected.");
                }

                using var stop = new SemaphoreSlim(0, 1);
                ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Release(); };
                Console.CancelKeyPress += handler;
                try
                {
                    if (seconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                    }
                    else
                    {
                        await stop.WaitAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }

                await connector.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            (config.CertificateManager as IDisposable)?.Dispose();
            Console.WriteLine($"Stopped. Final override layer: {outPath}");
            return 0;
        }

        // Writes a self-contained stage.usda that composes the connector's live override
        // layer over the server-delivered root layer (both now local in the cache dir).
        private static void WriteStageUsda(string cacheDir, List<OpenUsdConnector.FetchedAsset> fetched)
        {
            OpenUsdConnector.FetchedAsset? root = fetched.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
            string rootName = root != null ? Path.GetFileName(root.LocalPath) : "base.usda";
            var sb = new StringBuilder();
            sb.Append("#usda 1.0\n(\n");
            sb.Append("    doc = \"Self-contained OpenUSD stage: server-delivered base layers + the live OPC UA override.\"\n");
            sb.Append("    subLayers = [\n        @./live.usda@,\n        @./").Append(rootName).Append("@\n    ]\n");
            sb.Append(")\n");
            File.WriteAllText(Path.Combine(cacheDir, "stage.usda"), sb.ToString());
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
