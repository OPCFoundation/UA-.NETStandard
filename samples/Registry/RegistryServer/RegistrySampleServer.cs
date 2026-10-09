using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.SchemaRegistry.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Registry.Samples
{
    /// <summary>Explicit standalone sample hosting options. No production credentials are built in.</summary>
    public sealed class RegistrySampleServerOptions
    {
        /// <summary>Gets or sets the TCP listen URL.</summary>
        public string EndpointUrl { get; set; } = "opc.tcp://localhost:62555/Registry";
        /// <summary>Gets or sets the parent directory of the three exclusive state stores.</summary>
        public string StateDirectory { get; set; } = Path.Combine("registry-data", "state");
        /// <summary>Gets or sets the server's PKI directory.</summary>
        public string PkiDirectory { get; set; } = Path.Combine("registry-data", "server-pki");
        /// <summary>Gets or sets the sample administrator's username.</summary>
        public string UserName { get; set; } = string.Empty;
        /// <summary>Gets or sets an application-supplied sample password.</summary>
        public string Password { get; set; } = string.Empty;
        /// <summary>Gets or sets whether the separately owned Schema Registry is hosted.</summary>
        public bool EnableSchema { get; set; }
        /// <summary>Gets or sets development-only trust of untrusted client certificates.</summary>
        public bool AutoAccept { get; set; }
    }

    /// <summary>Owns a hosted registry server and its durable single-writer directories.</summary>
    public sealed partial class RegistrySampleServer : IAsyncDisposable
    {
        private RegistrySampleServer(IHost host, List<FileRegistryStateStore> stores)
        {
            m_host = host;
            m_stores = stores;
        }

        /// <summary>Starts the real TCP listener with generic, Media and optional Schema roots.</summary>
        public static async Task<RegistrySampleServer> StartAsync(
            RegistrySampleServerOptions options, CancellationToken cancellationToken = default)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrEmpty(options.Password))
            {
                throw new ArgumentException("Supply sample credentials explicitly.", nameof(options));
            }
            var stores = new List<FileRegistryStateStore>();
            IHost? host = null;
            try
            {
                FileRegistryStateStore generic = OpenStore("endpoints");
                FileRegistryStateStore media = OpenStore("media");
                var endpointOptions = new EndpointRegistryServerOptions
                {
                    Generic = new EndpointRegistryCatalogOptions { RegistryId = "sample-endpoints", Store = generic },
                    Media = new EndpointRegistryCatalogOptions
                    {
                        RegistryId = "sample-media",
                        Store = media,
                        Collections = ["endpoints"]
                    },
                    LoadDependencyModels = !options.EnableSchema,
                    SnapshotLimits = new XRegistry.RegistrySnapshotLimitsDataType
                    {
                        MaxSnapshotsPerSession = 16,
                        MaxContinuationPointsPerSession = 64,
                        MaxSnapshotBytes = 16 * 1024 * 1024,
                        MaxDepth = 128,
                        MaxReadItems = 256,
                        MaxReadBytes = 8192,
                        SnapshotTimeout = 60000
                    }
                };
                var schemaOptions = new SchemaRegistryServerOptions { Enabled = options.EnableSchema };
                if (options.EnableSchema)
                {
                    schemaOptions.Store = OpenStore("schemas");
                    schemaOptions.OriginUri = RegistryDemo.SchemaOrigin;
                }
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                builder.Logging.ClearProviders();
                builder.Logging.AddConsole();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                IOpcUaServerBuilder server = builder.Services.AddOpcUa().AddServer(o =>
                {
                    o.ApplicationName = "RegistryServer";
                    o.ApplicationUri = "urn:localhost:OPCFoundation:RegistryServer";
                    o.ProductUri = "uri:opcfoundation.org:RegistryServer";
                    o.SubjectName = "CN=RegistryServer, O=OPC Foundation, DC=localhost";
                    o.PkiRoot = options.PkiDirectory;
                    o.AutoAcceptUntrustedCertificates = options.AutoAccept;
                    o.IncludeSignAndEncryptPolicies = true;
                    o.IncludeUnsecurePolicyNone = false;
                    o.IncludeEccPolicies = false;
                    o.RejectSHA1Certificates = true;
                    o.MinCertificateKeySize = 2048;
                    o.EndpointUrls.Add(options.EndpointUrl);
                    o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName
                    });
                }).AddIdentityAuthenticator(new UserNamePasswordAuthenticator((token, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (token.UserName != options.UserName || token.DecryptedPassword is null ||
                        !token.DecryptedPassword.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(options.Password)))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    return new ValueTask<IUserIdentity>(new UserIdentity(token));
                })).ConfigureRoles(roles =>
                {
                    roles.Roles.Add(new RoleDefinitionOptions
                    {
                        Name = BrowseNames.WellKnownRole_ConfigureAdmin,
                        Identities =
                        {
                            new RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.UserName, Criteria = options.UserName
                            }
                        }
                    });
                });
                if (options.EnableSchema)
                {
                    server.AddNodeManager(new SchemaRegistryNodeManagerFactory(schemaOptions));
                }
                server.AddNodeManager(new EndpointRegistryNodeManagerFactory(endpointOptions));
                server.AddStartupTask((_, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    ready.TrySetResult(true);
                    return default;
                });
                host = builder.Build();
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                using (cancellationToken.Register(() => ready.TrySetCanceled()))
                {
                    Task? execution = null;
                    foreach (IHostedService hosted in host.Services.GetServices<IHostedService>())
                    {
                        if (hosted is BackgroundService background)
                        {
                            execution = background.ExecuteTask;
                        }
                    }
                    if (execution is null)
                    {
                        throw new InvalidOperationException("The OPC UA hosted service did not start.");
                    }
                    Task completed = await Task.WhenAny(ready.Task, execution).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                    if (!ready.Task.IsCompleted)
                    {
                        throw new InvalidOperationException("The OPC UA hosted service stopped before readiness.");
                    }
                }
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>()
                    .CreateLogger<RegistrySampleServer>();
                ServerStarted(logger, options.EndpointUrl, options.EnableSchema);
                return new RegistrySampleServer(host, stores);

                FileRegistryStateStore OpenStore(string name)
                {
                    string directory = Path.Combine(options.StateDirectory, name);
                    Directory.CreateDirectory(directory);
                    var store = new FileRegistryStateStore(directory);
                    stores.Add(store);
                    return store;
                }
            }
            catch
            {
                try
                {
                    if (host is not null)
                    {
                        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        host?.Dispose();
                    }
                    finally
                    {
                        foreach (FileRegistryStateStore store in stores)
                        {
                            await store.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                await m_host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    m_host.Dispose();
                }
                finally
                {
                    foreach (FileRegistryStateStore store in m_stores)
                    {
                        await store.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Registry TCP listener ready at {EndpointUrl}; Schema host enabled: {SchemaEnabled}")]
        private static partial void ServerStarted(ILogger logger, string endpointUrl, bool schemaEnabled);

        private readonly IHost m_host;
        private readonly List<FileRegistryStateStore> m_stores;
        private int m_disposed;
    }
}
