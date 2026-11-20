/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace IndustrialAutomationSample
{
    /// <summary>
    /// Configures one encrypted server and one borrowed-session typed client through public APIs.
    /// </summary>
    public static class IaSampleHost
    {
        /// <summary>
        /// Creates the Machinery-owned information simulation; anonymous resets require a separate explicit opt-in.
        /// </summary>
        public static IHost CreateServer(string endpoint, string pkiRoot, bool demoTrust, bool permitReset = false)
        {
            Validate(endpoint, pkiRoot);
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddSingleton(_ => new IaSampleModel { AllowReset = permitReset });
            builder.Services.AddSingleton<IServerStartupTask>(services => services.GetRequiredService<IaSampleModel>());
            builder.Services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "IndustrialAutomationSampleServer";
                options.ApplicationUri = "urn:localhost:OPCFoundation:IndustrialAutomationSampleServer";
                options.PkiRoot = Path.Combine(pkiRoot, "server");
                options.AutoAcceptUntrustedCertificates = demoTrust;
                options.IncludeUnsecurePolicyNone = false;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            }).AddMachinery(options =>
            {
                options.Parts = MachineryParts.BuildingBlocks;
                options.InstanceNamespaceUri = IaSampleModel.InstanceUri;
            }).AddIndustrialAutomation()
                .ConfigureIndustrialAutomationFor<MachineryNodeManager>(setup =>
                    setup.GetRequiredService<IaSampleModel>().BuildAsync(setup));
            return builder.Build();
        }

        /// <summary>
        /// Creates a client whose DI container owns the one managed connection.
        /// </summary>
        public static IHost CreateClient(string endpoint, string pkiRoot, bool demoTrust)
        {
            Validate(endpoint, pkiRoot);
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "IndustrialAutomationSampleClient";
                options.ApplicationUri = "urn:localhost:OPCFoundation:IndustrialAutomationSampleClient";
                options.PkiRoot = Path.Combine(pkiRoot, "client");
                options.AutoAcceptUntrustedCertificates = demoTrust;
                options.Session = new ManagedSessionOptions
                {
                    SessionName = "IA sample",
                    CheckDomain = true,
                    SessionTimeout = TimeSpan.FromSeconds(30)
                };
            }).AddDiscoveryAndConnect(options =>
            {
                options.DiscoveryUrl = endpoint;
                options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            }).AddIndustrialAutomationClient();
            return builder.Build();
        }

        /// <summary>
        /// Keeps framework diagnostics available without direct ILogger calls in application code.
        /// </summary>
        private static void ConfigureLogging(HostApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
        }

        /// <summary>
        /// Rejects unsupported endpoint schemes and missing certificate-store locations.
        /// </summary>
        private static void Validate(string endpoint, string pkiRoot)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != "opc.tcp")
            {
                throw new ArgumentException("An absolute opc.tcp endpoint is required.", nameof(endpoint));
            }
            if (string.IsNullOrWhiteSpace(pkiRoot))
            {
                throw new ArgumentException("A certificate-store root is required.", nameof(pkiRoot));
            }
        }
    }
}
