/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Samples;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Server;

namespace AggregationServer
{
    /// <summary>
    /// Builds and runs the reusable generic aggregation host.
    /// </summary>
    public static class AggregationServerHost
    {
        /// <summary>
        /// Builds, but does not start, the aggregation server and upstream-client host from explicit options,
        /// warning for enabled certificate-trust, unsecured-channel, or anonymous-management exceptions.
        /// </summary>
        /// <exception cref="ArgumentNullException">The options argument is null.</exception>
        public static IHost Build(AggregationServerOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            Configure(builder, options);
            return builder.Build();
        }

        /// <summary>
        /// Runs the aggregation host with the supplied policies until shutdown or cancellation
        /// and disposes the host afterward.
        /// </summary>
        public static async Task RunAsync(
            AggregationServerOptions options,
            CancellationToken cancellationToken = default)
        {
            using IHost host = Build(options);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Parses sample switches and forwarded host settings before running the aggregation server.
        /// Certificate auto-acceptance, unsecured channels, and anonymous management
        /// are separate default-false opt-ins.
        /// Stores the command result in Environment.ExitCode.
        /// </summary>
        public static async Task RunAsync(
            string[] args,
            CancellationToken cancellationToken = default)
        {
            Environment.ExitCode = await WotSampleCommandLine.InvokeAsync(
                args,
                "WoT aggregation server. Registry changes require authenticated SecurityAdmin on an encrypted channel.",
                ["endpoint", "host", "port", "applicationName", "pkiRoot", "maximumDocumentBytes"],
                management: true,
                async (builder, autoAccept, securityNone, anonymousManagement, ct) =>
                {
                    var options = new AggregationServerOptions
                    {
                        EndpointUrl = builder.Configuration["endpoint"],
                        Host = builder.Configuration["host"] ?? "localhost",
                        Port = ReadPort(builder.Configuration),
                        ApplicationName = builder.Configuration["applicationName"] ?? "AggregationServer",
                        PkiRoot = builder.Configuration["pkiRoot"],
                        MaximumDocumentBytes = ReadMaximumDocumentBytes(builder.Configuration),
                        AutoAcceptUntrustedCertificates = autoAccept,
                        IncludeUnsecurePolicyNone = securityNone,
                        AllowAnonymousManagement = anonymousManagement
                    };
                    Configure(builder, options);
                    using IHost host = builder.Build();
                    await host.RunAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static void Configure(
            HostApplicationBuilder builder,
            AggregationServerOptions options)
        {
            Validate(options);
            SampleCommandLine.WriteSecurityWarnings(
                Console.Error, options.AutoAcceptUntrustedCertificates,
                options.IncludeUnsecurePolicyNone, "client and upstream server", "--security-none");
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddSingleton(options);

            string endpoint = options.EndpointUrl ??
                $"opc.tcp://{options.Host}:{options.Port}/AggregationServer";
            IOpcUaBuilder opcUa = builder.Services.AddOpcUa();
            opcUa.AddOpcTcpTransport();
            IOpcUaServerBuilder serverBuilder = opcUa.AddServer(server =>
            {
                server.ApplicationName = options.ApplicationName;
                server.ApplicationUri =
                    $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                server.ProductUri = "uri:opcfoundation.org:AggregationServer";
                if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                {
                    server.PkiRoot = options.PkiRoot;
                }
                server.AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedCertificates;
                server.IncludeUnsecurePolicyNone = options.IncludeUnsecurePolicyNone;
                server.EndpointUrls.Add(endpoint);
            });
            options.ConfigureAuthentication?.Invoke(serverBuilder);
            opcUa.AddWotRegistryServer(registry =>
            {
                registry.AutoRefresh = false;
                registry.Bounds.MaxDocumentBytes = options.MaximumDocumentBytes;
                if (options.AllowAnonymousManagement)
                {
                    registry.ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = options.IncludeUnsecurePolicyNone
                            ? MessageSecurityMode.None
                            : MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
                    };
                    Console.Error.WriteLine(
                        "WARNING: --allow-anonymous-management permits anonymous registry changes "
                        + "(isolated demo only).");
                }
            });

            opcUa.AddClient(client =>
            {
                client.ApplicationName = options.ApplicationName;
                client.ApplicationUri =
                    $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                client.ProductUri = "uri:opcfoundation.org:AggregationServer";
                if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                {
                    client.PkiRoot = options.PkiRoot;
                }
                client.AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedCertificates;
                client.Session = new ManagedSessionOptions
                {
                    SessionName = $"{options.ApplicationName}.Upstream",
                    SessionTimeout = TimeSpan.FromSeconds(60)
                };
            }).AddDiscovery().AddManagedClientPool();

            opcUa.AddHttpWotBinding();
            opcUa.AddModbusWotBinding();

            // The aggregation topology deliberately federates source servers that run on the
            // same host as this server, so the loopback gate the default policy applies must be
            // opened explicitly. Keep every other check (scheme, blocked hosts, private ranges)
            // at its secure default.
            opcUa.AddWotEndpointPolicy(new WotEndpointPolicy { AllowLoopback = true });

            builder.Services.AddSingleton<OpcUaWotBindingOptions>(serviceProvider =>
            {
                IManagedSessionPool pool =
                    serviceProvider.GetRequiredService<IManagedSessionPool>();
                IOpcUaDiscoveryService discovery =
                    serviceProvider.GetRequiredService<IOpcUaDiscoveryService>();
                async ValueTask<ArrayOf<EndpointDescription>> DiscoverAsync(string url, CancellationToken ct)
                {
                    ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(url, ct: ct)
                        .ConfigureAwait(false);
                    MessageSecurityMode mode = options.IncludeUnsecurePolicyNone
                        ? MessageSecurityMode.None
                        : MessageSecurityMode.SignAndEncrypt;
                    string policy = options.IncludeUnsecurePolicyNone
                        ? SecurityPolicies.None
                        : SecurityPolicies.Basic256Sha256;
                    var eligible = new List<EndpointDescription>();
                    foreach (EndpointDescription endpoint in endpoints)
                    {
                        if (endpoint.SecurityMode == mode && endpoint.SecurityPolicyUri == policy)
                        {
                            eligible.Add(endpoint);
                        }
                    }
                    return eligible.ToArray();
                }

                async ValueTask<ISession> ConnectAsync(EndpointDescription endpoint, CancellationToken ct)
                {
                    string key = $"{endpoint.EndpointUrl}|{endpoint.SecurityMode}|{endpoint.SecurityPolicyUri}";
                    return await pool.GetOrConnectAsync(
                        key, new ConfiguredEndpoint(null, endpoint, null), ct).ConfigureAwait(false);
                }

                return new OpcUaWotBindingOptions
                {
                    DisposeSession = false,
                    EndpointDiscovery = DiscoverAsync,
                    SelectedEndpointSessionFactory = (endpoint, _, ct) => ConnectAsync(endpoint, ct),
                    SessionFactory = async (url, ct) =>
                    {
                        ArrayOf<EndpointDescription> endpoints = await DiscoverAsync(url, ct).ConfigureAwait(false);
                        EndpointDescription endpoint = OpcUaWotEndpointSelector.Select(endpoints, null) ??
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityPolicyRejected,
                                "No upstream endpoint matches the sample's configured security policy and mode.");
                        return await ConnectAsync(endpoint, ct).ConfigureAwait(false);
                    }
                };
            });
            builder.Services.AddSingleton<IWotBindingExecutor>(serviceProvider =>
                new OpcUaWotBindingExecutor(serviceProvider.GetRequiredService<OpcUaWotBindingOptions>()));
        }

        private static int ReadPort(ConfigurationManager configuration)
        {
            return int.TryParse(
                configuration["port"],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int port)
                ? port
                : 62550;
        }

        private static int ReadMaximumDocumentBytes(ConfigurationManager configuration)
        {
            return int.TryParse(
                configuration["maximumDocumentBytes"],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximumDocumentBytes)
                ? maximumDocumentBytes
                : 32 * 1024 * 1024;
        }

        private static void Validate(AggregationServerOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                throw new ArgumentException("ApplicationName is required.", nameof(options));
            }
            if (options.MaximumDocumentBytes <= 0)
            {
                throw new ArgumentException(
                    "MaximumDocumentBytes must be positive.",
                    nameof(options));
            }
            if (options.EndpointUrl is null &&
                (string.IsNullOrWhiteSpace(options.Host) || options.Port is < 1 or > 65535))
            {
                throw new ArgumentException("A valid host and port are required.", nameof(options));
            }
        }
    }
}
