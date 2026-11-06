/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
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
