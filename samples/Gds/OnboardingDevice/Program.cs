/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnboardingClientSample;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Network;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace OnboardingDevice
{
    /// <summary>
    /// Hosts one connected DCA/operational server with generated Part 21 methods and standard push management.
    /// Preinstall factory keys, peer trust and ticket authority trust; no insecure bootstrap callback is provided.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Starts the sample with source-configured APIs; failures are surfaced and return a nonzero exit code.
        /// Restart the executable with the same inputs and PKI root to recover the existing security state.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
                builder.Logging.ClearProviders();
                builder.Logging.EnableOpcUaRedaction();
                builder.Logging.AddConsole();
                var options = new DeviceSampleOptions(builder.Configuration);
                ArrayOf<DeviceBootstrap.TicketInput> tickets = await DeviceBootstrap.ReadTicketsAsync(
                    options, CancellationToken.None).ConfigureAwait(false);
                Directory.CreateDirectory(options.PkiRoot);
                using var files = new PushRegistrarCheckpointFiles(Path.Combine(options.PkiRoot, "device-state"));
                var checkpoints = new DeviceSecurityCheckpointStore(files, options.ApplicationUri);
                Configure(builder, options, tickets, checkpoints);
                IHost host = builder.Build();
                try
                {
                    if (options.PullRegistrarEndpoint != null)
                    {
                        await PreparePullPasswordAsync(host.Services, CancellationToken.None).ConfigureAwait(false);
                    }
                    await DeviceBootstrap.PrepareAsync(
                        host.Services, options, tickets, CancellationToken.None).ConfigureAwait(false);
                    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    await host.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                    foreach (BackgroundService service in host.Services.GetServices<IHostedService>()
                        .OfType<BackgroundService>())
                    {
                        if (service.ExecuteTask is { IsFaulted: true } failed)
                        {
                            await failed.ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    if (host is IAsyncDisposable asyncHost)
                    {
                        await asyncHost.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        host.Dispose();
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        /// <summary>
        /// Composes existing host, file-backed tickets/candidates, certificate stores and device security services.
        /// Only the two device-security producers receive public checkpoints; unrelated secret consumers retain
        /// the normal in-memory store and must never be redirected to the public adapter.
        /// </summary>
        private static void Configure(
            HostApplicationBuilder builder,
            DeviceSampleOptions options,
            ArrayOf<DeviceBootstrap.TicketInput> tickets,
            DeviceSecurityCheckpointStore checkpoints)
        {
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(checkpoints);
            builder.Services.AddSingleton<ICertificateManager>(services =>
                new CertificateManager(services.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<ISecretStore>(_ => new InMemorySecretStore());
            builder.Services.AddFileSystemOnboardingTicketStore(Path.Combine(options.PkiRoot, "tickets.bin"));
            builder.Services.AddFileSystemProvisionableDeviceRegistrarStore(
                Path.Combine(options.PkiRoot, "registrar-candidates.bin"));
            ConfigureAccount(builder.Services);
            var applicationCertificate = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = options.ApplicationStorePath,
                SubjectName = options.SubjectName,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            IOpcUaServerBuilder server = builder.Services.AddOpcUa()
                .ConfigureApplication(application =>
                {
                    application.ApplicationName = "OnboardingDevice";
                    application.ApplicationUri = options.ApplicationUri;
                    application.ProductUri = options.ProductUri;
                    application.SubjectName = options.SubjectName;
                    application.PkiRoot = options.PkiRoot;
                    application.AutoAcceptUntrustedCertificates = false;
                    application.RejectSHA1SignedCertificates = true;
                    application.MinimumCertificateKeySize = 2048;
                    application.ConfigureSecurity = security => security.SetApplicationCertificates(
                        [applicationCertificate]);
                })
                .AddServer(configuration =>
                {
                    configuration.ApplicationName = "OnboardingDevice";
                    configuration.ApplicationUri = options.ApplicationUri;
                    configuration.ProductUri = options.ProductUri;
                    configuration.SubjectName = options.SubjectName;
                    configuration.PkiRoot = options.PkiRoot;
                    configuration.AutoAcceptUntrustedCertificates = false;
                    configuration.IncludeSignAndEncryptPolicies = true;
                    configuration.IncludeUnsecurePolicyNone = false;
                    configuration.IncludeEccPolicies = false;
                    configuration.RejectSHA1Certificates = true;
                    configuration.MinCertificateKeySize = 2048;
                    configuration.EndpointUrls.Add(options.EndpointUrl);
                    configuration.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName
                    });
                })
                .AddDefaultIdentityAuthenticators(authentication =>
                {
                    authentication.EnableAnonymous = false;
                    authentication.EnableUserNamePassword = true;
                    authentication.EnableX509 = false;
                    authentication.EnableJwt = false;
                })
                .AddIdentityAugmenter(_ => new DeviceAdministratorAugmenter(
                    Environment.GetEnvironmentVariable("ONBOARDING_DEVICE_ADMIN_USER")))
                .AddOnboardingTickets(
                    Path.Combine(options.PkiRoot, "ticket-authorities", "trusted"),
                    Path.Combine(options.PkiRoot, "ticket-authorities", "issuers"))
                .AddOnboardingIdentities(
                    Path.Combine(options.PkiRoot, "identity-authorities", "trusted"),
                    Path.Combine(options.PkiRoot, "identity-authorities", "issuers"));

            var installerOptions = new ProvisionableDeviceSecurityInstallerOptions
            {
                ApplicationCertificate = applicationCertificate,
                DeviceIdentityStorePath = options.IdentityStorePath,
                DeviceIdentityStoreType = CertificateStoreType.Directory,
                CertificateGroupId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup
            };
            server.AddProvisionableDeviceSecurity(options.ApplicationUri, options.ApplicationUri, installerOptions);
            // The public-only adapter is scoped to these producers, not registered as general-purpose secret storage.
            builder.Services.Replace(ServiceDescriptor.Singleton<IProvisionableDeviceSecurityInstaller>(services =>
                new ProvisionableDeviceSecurityInstaller(
                    checkpoints, options.ApplicationUri, options.ApplicationUri,
                    services.GetRequiredService<ICertificateManager>(),
                    services.GetRequiredService<ICertificateStoreResolver>(),
                    services.GetRequiredService<IPendingCertificateKeyStore>(),
                    services.GetRequiredService<IServerApplicationCertificateActivator>(),
                    installerOptions, services.GetRequiredService<ITelemetryContext>(),
                    services.GetService<IProvisionableDeviceSoftwareUpdatePolicy>())));
            builder.Services.Replace(ServiceDescriptor.Singleton<ProvisionableDeviceSecurity>(services =>
                new ProvisionableDeviceSecurity(
                    checkpoints, options.ApplicationUri, options.ApplicationUri,
                    services.GetRequiredService<ICertificateManager>(),
                    services.GetRequiredService<IProvisionableDeviceSecurityInstaller>(),
                    services.GetRequiredService<ITelemetryContext>())));
            bool isSingleton = options.ManagedApplicationUris.Count == 0;
            if (!isSingleton)
            {
                server.AddProvisionableDeviceApplications(services =>
                    new SampleManagedClientApplicationsProvider(
                        options,
                        services.GetRequiredService<ITelemetryContext>()));
            }
            server.AddNetworkModel(
                new NetworkModelBuilder().Build(DateTime.UnixEpoch, StatusCodes.Good));
            server.AddProvisionableDeviceRuntime(
                isSingleton, tickets.ToList().Select(static ticket => ticket.Id).ToArrayOf());
            if (options.PhysicalReset)
            {
                builder.Services.AddSingleton<IServerPreStartupTask>(static services =>
                    new DevicePhysicalResetTask(services.GetRequiredService<IProvisionableDeviceSecurity>()));
            }
            if (options.PullRegistrarEndpoint != null)
            {
                ConfigurePull(builder.Services, options);
            }
            builder.Services.AddSingleton<IServerStartupTask>(static services =>
                new DeviceReadyTask(services.GetRequiredService<DeviceSampleOptions>()));
        }

        /// <summary>
        /// Shares the server's application identity and local key with the typed outbound pull workflow.
        /// Received credentials pass through the device lifecycle instead of the standalone client's installer.
        /// </summary>
        private static void ConfigurePull(IServiceCollection services, DeviceSampleOptions options)
        {
            string user = RequiredEnvironment("ONBOARDING_PULL_USER");
            services.AddSingleton<DevicePullSession>(static provider =>
                new DevicePullSession(provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()));
            services.AddSingleton<Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(
                static provider =>
                {
                    DevicePullSession session = provider.GetRequiredService<DevicePullSession>();
                    return (_, ct) => new ValueTask<ManagedSession>(session.GetCurrentAsync(ct));
                });
            services.AddSingleton<IClientIdentityProvider>(provider =>
            {
                ISecretStore secrets = provider.GetRequiredService<ISecretStore>();
                return new UserNamePasswordIdentityProvider(
                    user, new SecretRegistry(secrets), new SecretIdentifier("DevicePullPassword", secrets.StoreType));
            });
            services.AddOpcUa().AddClient(client =>
            {
                client.ApplicationName = "OnboardingDevice";
                client.ApplicationUri = options.ApplicationUri;
                client.ProductUri = options.ProductUri;
                client.SubjectName = options.SubjectName;
                client.PkiRoot = options.PkiRoot;
                client.AutoAcceptUntrustedCertificates = false;
                client.RejectSHA1SignedCertificates = true;
                client.MinimumCertificateKeySize = 2048;
                client.Session = new ManagedSessionOptions
                {
                    SessionName = "OnboardingDevicePull"
                };
            }).AddDiscoveryAndConnect(connection =>
            {
                connection.DiscoveryUrl = options.PullRegistrarEndpoint!.AbsoluteUri;
                connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                connection.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            }).AddGdsClient().AddOnboardingClient().AddPullDcaWorkflow();
            services.AddSingleton(provider => new OnboardingDeviceCredentials(
                provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration,
                provider.GetRequiredService<ITelemetryContext>(),
                provider.GetServices<ICertificateStoreProvider>().ToArray().ToArrayOf()));
            services.AddSingleton(provider => new DevicePullCredentials(
                provider.GetRequiredService<OnboardingDeviceCredentials>(),
                provider.GetRequiredService<OnboardingDeviceCredentials>(),
                provider.GetRequiredService<IProvisionableDeviceSecurity>(),
                provider.GetRequiredService<DevicePullSession>().GetCurrentAsync,
                options.PullRegistrarApplicationUri!));
            services.AddSingleton<IPullDcaIdentityProvider>(static provider =>
                provider.GetRequiredService<DevicePullCredentials>());
            services.AddSingleton<IPullDcaCredentialProvider>(static provider =>
                provider.GetRequiredService<DevicePullCredentials>());
            services.AddSingleton<IServerStartupTask>(static provider => new DevicePullTask(
                provider.GetRequiredService<DeviceSampleOptions>(),
                provider.GetRequiredService<IProvisionableDeviceSecurity>(),
                provider.GetRequiredService<IProvisionableDeviceRegistrarStore>(),
                provider.GetRequiredService<IProvisionableDeviceRuntime>(),
                provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>(),
                provider.GetRequiredService<PullDcaWorkflow>(),
                provider.GetRequiredService<DevicePullSession>()));
        }

        /// <summary>
        /// Places the outbound password in the ordinary host secret store, separate from public device checkpoints.
        /// </summary>
        private static async ValueTask PreparePullPasswordAsync(IServiceProvider services, CancellationToken ct)
        {
            ISecretStore secrets = services.GetRequiredService<ISecretStore>();
            byte[] password = Encoding.UTF8.GetBytes(RequiredEnvironment("ONBOARDING_PULL_PASSWORD"));
            try
            {
                await secrets.SetAsync(new SecretIdentifier("DevicePullPassword", secrets.StoreType), password, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(password);
            }
        }

        /// <summary>
        /// Creates one environment-configured login without granting implicit administrative rights.
        /// ONBOARDING_DEVICE_ADMIN_USER optionally maps that exact authenticated account to SecurityAdmin.
        /// </summary>
        private static void ConfigureAccount(IServiceCollection services)
        {
            string user = RequiredEnvironment("ONBOARDING_DEVICE_USER");
            string password = RequiredEnvironment("ONBOARDING_DEVICE_PASSWORD");
            string? administrator = Environment.GetEnvironmentVariable("ONBOARDING_DEVICE_ADMIN_USER");
            if (administrator != null && !string.Equals(administrator, user, StringComparison.Ordinal))
            {
                throw new ArgumentException("ONBOARDING_DEVICE_ADMIN_USER must name the configured device account.");
            }
            var database = new LinqUserDatabase();
            byte[] bytes = Encoding.UTF8.GetBytes(password);
            try
            {
                if (!database.CreateUser(user, bytes, [Role.AuthenticatedUser]))
                {
                    throw new InvalidOperationException("The configured device login could not be created.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
            services.AddSingleton<IUserDatabase>(database);
            services.AddSingleton<IUserManagement>(provider =>
                new UserManagement(provider.GetRequiredService<IUserDatabase>()));
        }

        /// <summary>
        /// Reads a login secret only from the process environment.
        /// </summary>
        private static string RequiredEnvironment(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"Set {name} before starting the device.");
        }
    }
}
