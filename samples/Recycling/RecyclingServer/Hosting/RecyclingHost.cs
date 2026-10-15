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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Direct, injectable Generic Host construction for the sole coordinator or one process view.
    /// </summary>
    public static class RecyclingHost
    {
        /// <summary>
        /// Builds an unstarted host with a secure OPC UA endpoint and one DI/Machinery address-space owner.
        /// Services and additional companion-model contributions can be supplied without changing the causal core.
        /// </summary>
        public static IHost Build(
            RecyclingHostOptions options,
            Action<IServiceCollection>? configureServices = null,
            Action<IOpcUaServerBuilder>? configureServer = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddSingleton(options);
            configureServices?.Invoke(builder.Services);
            builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.TryAddSingleton<ICertificatePasswordProvider>(
                static _ => new CertificatePasswordProvider());
            builder.Services.TryAddSingleton<ICertificateProvider>(static services =>
                services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                    .Configuration.CertificateManager?.CertificateProvider ??
                    throw new InvalidOperationException(
                        "Initialize the application certificate manager before connecting."));
            builder.Services.TryAddSingleton<IFileSystemProvider>(_ =>
                new PhysicalFileSystemProvider(options.StateRoot, "RecyclingState"));
            builder.Services.TryAddSingleton<RecyclingSignalDefaults>();
            builder.Services.TryAddSingleton<RecyclingNativeProcessOptions>();
            builder.Services.TryAddSingleton<IRecyclingProcessViewAdapter, CompanionProcessViewAdapter>();
            builder.Services.TryAddSingleton<IRecyclingUserCommandForwarder, UnconfiguredUserCommandForwarder>();
            builder.Services.AddSingleton(new RecyclingAuthorization(options.Identities));
            builder.Services.AddSingleton<RecyclingRuntime>();
            builder.Services.AddHostedService<RecyclingStartupService>();

            IOpcUaBuilder opc = builder.Services.AddOpcUa().ConfigureApplication(application =>
            {
                application.ApplicationName = options.ApplicationName;
                application.ApplicationUri = options.ApplicationUri;
                application.ProductUri = "urn:opcfoundation.org:sample:recycling";
                application.PkiRoot = Path.GetFullPath(options.PkiRoot);
                application.AutoAcceptUntrustedCertificates = false;
                application.RejectSHA1SignedCertificates = true;
                application.MinimumCertificateKeySize = 2048;
                // Match the RSA-only endpoints instead of provisioning unadvertised platform-dependent ECC keys.
                application.ConfigureSecurity = security => security.SetApplicationCertificates(
                [
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.GetFullPath(options.PkiRoot),
                        SubjectName = $"CN={options.ApplicationName}, O=OPC Foundation, DC=localhost",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ]);
            });
            IOpcUaServerBuilder server = opc.AddServer(configuration =>
            {
                configuration.ApplicationName = options.ApplicationName;
                configuration.ApplicationUri = options.ApplicationUri;
                configuration.ProductUri = "urn:opcfoundation.org:sample:recycling";
                configuration.PkiRoot = Path.GetFullPath(options.PkiRoot);
                configuration.EndpointUrls.Add(options.EndpointUrl);
                configuration.AutoAcceptUntrustedCertificates = false;
                configuration.IncludeUnsecurePolicyNone = false;
                configuration.IncludeSignAndEncryptPolicies = true;
                configuration.IncludeEccPolicies = false;
                configuration.MaxArrayLength = 100_000;
                configuration.MaxStringLength = 1024;
                configuration.MaxByteStringLength = 16 * 1024 * 1024;
                configuration.MaxMessageSize = 16 * 1024 * 1024;
                configuration.OperationTimeoutMs = 15000;
                configuration.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                {
                    TokenType = UserTokenType.Certificate
                });
            })
                .AddDefaultIdentityAuthenticators(authentication =>
                {
                    authentication.EnableAnonymous = false;
                    authentication.EnableUserNamePassword = false;
                    authentication.EnableX509 = false;
                    authentication.EnableJwt = false;
                })
                .AddIdentityAuthenticator((_, validator) =>
                    new X509Authenticator(validator ??
                        throw new InvalidOperationException("Certificate validation is required.")))
                .AddMachinery(machinery =>
                {
                    machinery.Parts = options.MachineryParts;
                    machinery.InstanceNamespaceUri = "urn:recycling-sample:instances:" +
                        Uri.EscapeDataString(options.LineId) + ":" + options.Role + ":" + options.Process;
                })
                .AddMachineryModel<RecyclingModelProvider>()
                .ConfigureMachinery((context, cancellationToken) => context.GetRequiredService<RecyclingRuntime>()
                    .ConfigureAsync(context, cancellationToken));
            if (options.Role == RecyclingHostRole.ProcessView)
            {
                server.AddAssetManagement().AddStartupTask<RecyclingProjectionCapabilities>();
                if (options.Process is RecyclingProcess.Collection or RecyclingProcess.Sorting or
                    RecyclingProcess.Bagging)
                {
                    server.AddScalesToMachinery();
                }
                if (options.Process is RecyclingProcess.Washing or RecyclingProcess.DensitySeparation)
                {
                    server.AddPumpsToMachinery();
                }
                if (options.Process == RecyclingProcess.Sorting)
                {
                    server.AddVisionToMachinery()
                        .ConfigureVisionFor<MachineryNodeManager>((context, cancellationToken) =>
                            context.GetRequiredService<IRecyclingProcessViewAdapter>() is
                                CompanionProcessViewAdapter adapter
                                ? adapter.InitializeVisionAsync(context, cancellationToken)
                                : ValueTask.CompletedTask);
                }
                if (options.Process == RecyclingProcess.Drying)
                {
                    server.AddGranulateDryingToMachinery();
                }
                if (options.Process is RecyclingProcess.Extrusion or RecyclingProcess.MeltFiltration or
                    RecyclingProcess.Pelletizing)
                {
                    server.AddExtrusionToMachinery();
                }
            }
            server.AddStartupTask<RecyclingServerReady>();
            configureServer?.Invoke(server);

            if (options.Role == RecyclingHostRole.ProcessView)
            {
                opc.AddClient(client =>
                {
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = options.ApplicationName,
                        SessionTimeout = TimeSpan.FromSeconds(options.Simulation.LeaseSeconds),
                        CheckDomain = true
                    };
                    client.Identity.EnableAnonymous = false;
                    client.Identity.X509 = new X509ClientIdentityOptions
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = options.IdentityStorePath,
                        SubjectName = options.IdentitySubject
                    };
                }).AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.CoordinatorUrl;
                    discovery.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    discovery.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                });
            }
            builder.Services.AddHostedService<RecyclingRuntimeService>();
            return builder.Build();
        }
    }

    /// <summary>
    /// Opens durable state before the OPC UA endpoint starts.
    /// </summary>
    internal sealed class RecyclingStartupService(RecyclingRuntime runtime) : IHostedService
    {
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return runtime.StartAsync(cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return runtime.StopAsync().AsTask();
        }
    }

    /// <summary>
    /// Signals real address-space/listener startup, not merely construction of a host object.
    /// </summary>
    internal sealed class RecyclingServerReady(RecyclingRuntime runtime) : IServerStartupTask
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            runtime.SignalAddressSpaceReady();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Runs pacing/projection only after the server startup signal.
    /// </summary>
    internal sealed class RecyclingRuntimeService(RecyclingRuntime runtime) : BackgroundService
    {
        /// <inheritdoc/>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return runtime.RunAsync(stoppingToken);
        }
    }
}
