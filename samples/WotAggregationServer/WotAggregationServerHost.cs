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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;
#if NET8_0_OR_GREATER
using Opc.Ua.WotCon.Bindings.OpcUa;
#endif
using Opc.Ua.WotCon.Server;

namespace WotAggregationServer
{
    /// <summary>
    /// Builds and runs the reusable generic aggregation host.
    /// </summary>
    public static class WotAggregationServerHost
    {
        /// <summary>
        /// Builds a host from explicit options.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IHost Build(WotAggregationServerOptions options)
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
        /// Builds and runs a host from explicit options.
        /// </summary>
        public static async Task RunAsync(
            WotAggregationServerOptions options,
            CancellationToken cancellationToken = default)
        {
            using IHost host = Build(options);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds and runs a host from command-line configuration.
        /// </summary>
        public static async Task RunAsync(
            string[] args,
            CancellationToken cancellationToken = default)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            var options = new WotAggregationServerOptions
            {
                EndpointUrl = builder.Configuration["endpoint"],
                Host = builder.Configuration["host"] ?? "localhost",
                Port = ReadPort(builder.Configuration),
                ApplicationName =
                    builder.Configuration["applicationName"] ?? "WotAggregationServer",
                MaximumDocumentBytes = ReadMaximumDocumentBytes(builder.Configuration)
            };
            Configure(builder, options);
            using IHost host = builder.Build();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void Configure(
            HostApplicationBuilder builder,
            WotAggregationServerOptions options)
        {
            Validate(options);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddSingleton(options);

            string endpoint = options.EndpointUrl ??
                $"opc.tcp://{options.Host}:{options.Port}/WotAggregationServer";
            IOpcUaBuilder opcUa = builder.Services.AddOpcUa();
            opcUa.AddOpcTcpTransport();
            opcUa.AddServer(server =>
            {
                server.ApplicationName = options.ApplicationName;
                server.ApplicationUri =
                    $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                server.ProductUri = "uri:opcfoundation.org:WotAggregationServer";
                if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                {
                    server.PkiRoot = options.PkiRoot;
                }
                server.AutoAcceptUntrustedCertificates = true;
                server.IncludeUnsecurePolicyNone = true;
                server.EndpointUrls.Add(endpoint);
            });
            opcUa.AddWotRegistryServer(registry =>
            {
                registry.AutoRefresh = false;
                registry.Bounds.MaxDocumentBytes = options.MaximumDocumentBytes;
                registry.ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
                };
            });

            opcUa.AddClient(client =>
            {
                client.ApplicationName = options.ApplicationName;
                client.ApplicationUri =
                    $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                client.ProductUri = "uri:opcfoundation.org:WotAggregationServer";
                if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                {
                    client.PkiRoot = options.PkiRoot;
                }
                client.AutoAcceptUntrustedCertificates = true;
                client.Session = new ManagedSessionOptions
                {
                    SessionName = $"{options.ApplicationName}.Upstream",
                    SessionTimeout = TimeSpan.FromSeconds(60)
                };
            }).AddManagedClientPool();

#if NET8_0_OR_GREATER
            opcUa.AddHttpWotBinding();
            opcUa.AddModbusWotBinding();
            opcUa.AddWotProtocolBinders();
            builder.Services.AddSingleton<IWotBindingExecutor>(serviceProvider =>
            {
                IManagedSessionPool pool =
                    serviceProvider.GetRequiredService<IManagedSessionPool>();
                return new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    DisposeSession = false,
                    SessionFactory = async (url, ct) =>
                    {
                        var endpointDescription = new EndpointDescription
                        {
                            EndpointUrl = url,
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        };
                        var configuredEndpoint = new ConfiguredEndpoint(
                            null,
                            endpointDescription,
                            null);
                        return await pool.GetOrConnectAsync(url, configuredEndpoint, ct)
                            .ConfigureAwait(false);
                    }
                });
            });
#else
            opcUa.AddWotProtocolBinders();
#endif
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

        private static void Validate(WotAggregationServerOptions options)
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
