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
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.XRegistry.Bridge;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Connector
{
    /// <summary>
    /// Thin executable lifecycle over the injectable bridge libraries. Configuration
    /// and trust are validated before listeners or reconciliation writes start.
    /// </summary>
    public static partial class XRegistryConnectorHost
    {
        /// <summary>
        /// Runs one selected mode until completion or cancellation.
        /// </summary>
        public static async Task<int> RunAsync(
            XRegistryConnectorSettings settings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            try
            {
                settings.Validate();
                if (settings.Command is XRegistryConnectorCommand.Conflicts or XRegistryConnectorCommand.Resolve
                    or XRegistryConnectorCommand.StateStatus or XRegistryConnectorCommand.StateBackup
                    or XRegistryConnectorCommand.StateRestore or XRegistryConnectorCommand.StateCompact)
                {
                    return await RunStateCommandAsync(settings, cancellationToken).ConfigureAwait(false);
                }
                return settings.Command == XRegistryConnectorCommand.HttpGateway
                    ? await RunHttpGatewayAsync(settings, cancellationToken).ConfigureAwait(false)
                    : await RunHostAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception exception) when (exception is
                IOException or HttpRequestException or ServiceResultException or UnauthorizedAccessException
                    or ArgumentException or InvalidOperationException or KeyNotFoundException or InvalidDataException)
            {
                string message = exception is XRegistryHttpException { Response: { } response }
                    ? $"{exception.Message} HTTP status {response.StatusCode}." : exception.Message;
                await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
                return 1;
            }
        }

        private static async Task<int> RunHostAsync(
            XRegistryConnectorSettings settings, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
            Configure(builder.Services, builder.Configuration, builder.Logging, settings);
            IHost host = builder.Build();
            ManagedSession? session = null;
            FileXRegistrySyncStateStore? state = null;
            XRegistryBridgeRunner? runner = null;
            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                IXRegistryEndpoint? native = null;
                if (settings.OpcUaEndpoint is not null)
                {
                    session = await host.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()
                        (cancellationToken).ConfigureAwait(false);
                    native = await CreateNativeAsync(session, host.Services, settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                XRegistryHttpEndpoint? http = settings.HttpRoot is null
                    ? null : host.Services.GetRequiredService<XRegistryHttpEndpoint>();
                XRegistryCallContext context = OperatorContext(settings.CredentialProfile);
                if (settings.Command == XRegistryConnectorCommand.Inspect)
                {
                    if (native is not null)
                    {
                        await XRegistryConnectorOutput.DescriptionAsync("opcua",
                            await native.InspectAsync(context, cancellationToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                    }
                    if (http is not null)
                    {
                        await XRegistryConnectorOutput.DescriptionAsync("http",
                            await http.InspectAsync(context, cancellationToken).ConfigureAwait(false))
                            .ConfigureAwait(false);
                    }
                    return 0;
                }
                if (settings.Command == XRegistryConnectorCommand.OpcUaGateway)
                {
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                    startup.CancelAfter(host.Services.GetRequiredService<XRegistryBridgeNativeOptions>()
                        .PreparedOperationTimeout);
                    try
                    {
                        await host.Services.GetRequiredService<NativeReadiness>().Started.WaitAsync(startup.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new InvalidOperationException(
                            "The OPC UA server failed startup or exceeded its startup deadline.", exception);
                    }
                    runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions
                    {
                        Mode = XRegistryBridgeMode.OpcUaGateway,
                        PollInterval = settings.PollInterval
                    }, [new XRegistryBridgeUpstream("http", http!, context)],
                        host.Services.GetRequiredService<ITelemetryContext>(),
                        projection: host.Services.GetRequiredService<IXRegistryBridgeProjection>());
                    await RequireHealthyStartupAsync(runner, cancellationToken).ConfigureAwait(false);
                    await XRegistryConnectorOutput.ReadyAsync("opcua-gateway",
                        host.Services.GetRequiredService<XRegistryBridgeNativeOptions>().RootAddress.ToString())
                        .ConfigureAwait(false);
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                    return await runner.RunAsync(cancellationToken: stop.Token).ConfigureAwait(false);
                }
                // The finally callback owns this store until delayed reconciliation has really stopped.
                // TODO: Remove when CA2000 recognizes deferred asynchronous disposal ownership.
#pragma warning disable CA2000
                state = new FileXRegistrySyncStateStore(
                    LocalFileSystem.Instance, Path.Combine(settings.StateDirectory!, settings.JobId));
#pragma warning restore CA2000
                var options = new XRegistrySyncOptions(settings.JobId,
                    settings.OpcUaEndpoint!.AbsoluteUri + "#" + settings.RegistryNodeId,
                    settings.HttpRoot!.AbsoluteUri)
                {
                    ConflictPolicy = ParsePolicy(settings.ConflictPolicy),
                    PropagateDeletes = settings.PropagateDeletes,
                    VersionCorrespondences =
                        ReadVersionCorrespondences(host.Services.GetRequiredService<IConfiguration>()),
                    OpcUaContext = context,
                    HttpContext = context
                };
                var synchronizer = new XRegistrySynchronizer(
                    native!, http!, state, options, host.Services.GetRequiredService<ITelemetryContext>());
                runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions
                {
                    Mode = XRegistryBridgeMode.Synchronization,
                    PollInterval = settings.PollInterval,
                    Once = settings.Once,
                    DryRun = settings.DryRun
                },
                    [new XRegistryBridgeUpstream("opcua", native!, context),
                        new XRegistryBridgeUpstream("http", http!, context)],
                    host.Services.GetRequiredService<ITelemetryContext>(), synchronizer);
                using var syncStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                return await runner.RunAsync(async (status, _) =>
                {
                    if (status.Synchronization is { } report)
                    {
                        await XRegistryConnectorOutput.ReportAsync(report).ConfigureAwait(false);
                    }
                }, syncStop.Token).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseWhenIdleAsync(runner, async () =>
                {
                    try
                    {
                        if (session is not null)
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (state is not null)
                            {
                                await state.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                                await host.StopAsync(stop.Token).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (host is IAsyncDisposable disposable)
                                {
                                    await disposable.DisposeAsync().ConfigureAwait(false);
                                }
                                else
                                {
                                    host.Dispose();
                                }
                            }
                        }
                    }
                }).ConfigureAwait(false);
            }
        }

        internal static async Task ReleaseWhenIdleAsync(
            XRegistryBridgeRunner? runner, Func<Task> release, TimeSpan? timeout = null)
        {
            Task cleanup = CompleteReleaseAsync();
            try
            {
                await cleanup.WaitAsync(timeout ?? TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (TimeoutException) when (!cleanup.IsCompleted)
            {
                await Console.Error.WriteLineAsync(
                    "xRegistry shutdown is deferred until in-flight operations finish; their resources remain owned.")
                    .ConfigureAwait(false);
                _ = ObserveReleaseAsync(cleanup);
            }

            async Task CompleteReleaseAsync()
            {
                if (runner is not null)
                {
                    await runner.WaitForPendingOperationsAsync().ConfigureAwait(false);
                }
                await release().ConfigureAwait(false);
            }
        }

        private static async Task ObserveReleaseAsync(Task cleanup)
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or
                ServiceResultException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                or OperationCanceledException)
            {
                await Console.Error.WriteLineAsync("Deferred xRegistry shutdown failed: " + exception.Message)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<int> RunHttpGatewayAsync(
            XRegistryConnectorSettings settings, CancellationToken cancellationToken)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            Configure(builder.Services, builder.Configuration, builder.Logging, settings);
            X509Certificate2? selectedCertificate = null;
            builder.WebHost.UseUrls(settings.ListenAddress!.GetLeftPart(UriPartial.Authority));
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 33_554_432;
                if (settings.ListenAddress.Scheme == Uri.UriSchemeHttps)
                {
                    options.ConfigureHttpsDefaults(https =>
                        https.ServerCertificateSelector = (_, _) => selectedCertificate
                            ?? throw new InvalidOperationException("The managed HTTPS certificate is not available."));
                }
            });
            WebApplication app = builder.Build();
            var lifetime = new HttpGatewayLifetime(app);
            await using ConfiguredAsyncDisposable appLifetime = lifetime.ConfigureAwait(false);
            CertificateEntry? entry = lifetime.Entry = settings.ListenAddress.Scheme == Uri.UriSchemeHttps
                ? await AcquireCertificateAsync(app.Services, cancellationToken).ConfigureAwait(false) : null;
            selectedCertificate = lifetime.Certificate = entry?.Certificate?.AsX509Certificate2();
            ManagedSession session = await app.Services
                .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()(cancellationToken)
                .ConfigureAwait(false);
            lifetime.Session = session;
            IXRegistryEndpoint endpoint = await CreateNativeAsync(session, app.Services, settings, cancellationToken)
                .ConfigureAwait(false);
            IConfiguration configuration = app.Services.GetRequiredService<IConfiguration>();
            string? secretName = configuration["HttpServer:BearerSecret"];
            var authentication = new XRegistryConnectorAuthentication(
                app.Services.GetRequiredService<ISecretRegistry>(),
                secretName is null ? null : new SecretIdentifier(secretName,
                    configuration["HttpServer:SecretStoreType"] ?? "Environment"),
                ReadBoolean(configuration, "HttpServer:AllowAnonymousReads", true));
            app.Use(authentication.InvokeAsync);
            var runner = lifetime.Runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions
            {
                Mode = XRegistryBridgeMode.HttpGateway,
                PollInterval = settings.PollInterval
            }, [new XRegistryBridgeUpstream("opcua", endpoint, OperatorContext(settings.CredentialProfile))],
                app.Services.GetRequiredService<ITelemetryContext>());
            app.MapXRegistry(settings.PublicHttpRoot!.AbsolutePath.TrimEnd('/'),
                app.Services.GetService<IXRegistryEndpointResolver>() ?? XRegistryEndpointResolver.Borrow(endpoint),
                new XRegistryHttpRouteOptions(settings.PublicHttpRoot)
                {
                    RequireAuthenticatedUser = false,
                    Transport = new XRegistryHttpOptions
                    {
                        AllowLoopbackHttp = settings.AllowLoopbackHttp,
                        Telemetry = app.Services.GetRequiredService<ITelemetryContext>()
                    },
                    AuthorizeAsync = static (context, request, _) => new ValueTask<bool>(
                        !request.IsMutation || context.User.IsInRole("xregistry.write"))
                });
            app.MapGet("/_bridge/ready", async context =>
            {
                XRegistryBridgeStatus status = runner.Status;
                context.Response.StatusCode = status.Ready
                    ? Microsoft.AspNetCore.Http.StatusCodes.Status200OK
                    : Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(XRegistryConnectorOutput.Readiness(status.Ready, status.CanWrite),
                    context.RequestAborted).ConfigureAwait(false);
            });
            await RequireHealthyStartupAsync(runner, cancellationToken).ConfigureAwait(false);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            await XRegistryConnectorOutput.ReadyAsync("http-gateway", settings.PublicHttpRoot.AbsoluteUri)
                .ConfigureAwait(false);
            using var runStop =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, app.Lifetime.ApplicationStopping);
            return await runner.RunAsync(cancellationToken: runStop.Token).ConfigureAwait(false);
        }

        private static async Task RequireHealthyStartupAsync(
            XRegistryBridgeRunner runner, CancellationToken cancellationToken)
        {
            XRegistryBridgeStatus status = await runner.RunOnceAsync(cancellationToken).ConfigureAwait(false);
            if (!status.Ready)
            {
                throw new InvalidOperationException("The bridge cannot become ready: " + status.Failure);
            }
        }

        private sealed class NativeReadiness
        {
            public Task Started => m_started.Task;

            public void MarkStarted()
            {
                m_started.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class HttpGatewayLifetime(WebApplication app) : IAsyncDisposable
        {
            public ManagedSession? Session { get; set; }

            public CertificateEntry? Entry { get; set; }

            public X509Certificate2? Certificate { get; set; }

            public XRegistryBridgeRunner? Runner { get; set; }

            public ValueTask DisposeAsync()
            {
                return new ValueTask(ReleaseWhenIdleAsync(Runner, ReleaseAsync));
            }

            private async Task ReleaseAsync()
            {
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await app.StopAsync(stop.Token).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (Session is not null)
                        {
                            await Session.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            await app.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            Certificate?.Dispose();
                            Entry?.Dispose();
                        }
                    }
                }
            }
        }

        private static async Task<IXRegistryEndpoint> CreateNativeAsync(
            ManagedSession session, IServiceProvider services, XRegistryConnectorSettings settings,
            CancellationToken cancellationToken)
        {
            await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
            var root = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(settings.RegistryNodeId!), session.NamespaceUris);
            if (root.IsNull)
            {
                throw new ArgumentException("The registry root namespace is not present on the selected server.");
            }
            XRegistryBridgeNativeOptions options = services.GetRequiredService<XRegistryBridgeNativeOptions>();
            if (settings.ModelFile is not null)
            {
                using var stream = new FileStream(settings.ModelFile, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.Asynchronous);
                if (stream.Length > options.MaxMessageBytes)
                {
                    throw new InvalidDataException("The supplied model exceeds the configured message limit.");
                }
                using System.Text.Json.JsonDocument model = await System.Text.Json.JsonDocument
                    .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                options = options with { BaseModel = model.RootElement };
            }
            return new XRegistryOpcUaEndpoint(session, root, options, services.GetRequiredService<ITelemetryContext>());
        }

        private static async Task<CertificateEntry> AcquireCertificateAsync(
            IServiceProvider services, CancellationToken cancellationToken)
        {
            ApplicationConfiguration configuration = await services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>().GetAsync(cancellationToken)
                .ConfigureAwait(false);
            return configuration.CertificateManager?.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256)
                ?? throw new InvalidOperationException(
                    "Provision a managed application certificate for the HTTPS listener.");
        }

        private static async Task<int> RunStateCommandAsync(
            XRegistryConnectorSettings settings, CancellationToken cancellationToken)
        {
            var store = new FileXRegistrySyncStateStore(
                LocalFileSystem.Instance, Path.Combine(settings.StateDirectory!, settings.JobId));
            await using ConfiguredAsyncDisposable storeLifetime = store.ConfigureAwait(false);
            var manager = new XRegistrySyncStateManager(store, settings.JobId);
            if (settings.Command == XRegistryConnectorCommand.Resolve)
            {
                XRegistrySyncConflict conflict = await manager.ResolveConflictAsync(
                    settings.ConflictId!, ParsePolicy(settings.Resolution!), cancellationToken).ConfigureAwait(false);
                await XRegistryConnectorOutput.ConflictsAsync([conflict]).ConfigureAwait(false);
            }
            else if (settings.Command == XRegistryConnectorCommand.Conflicts)
            {
                await XRegistryConnectorOutput.ConflictsAsync(
                    await manager.ListConflictsAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (settings.Command is XRegistryConnectorCommand.StateBackup
                or XRegistryConnectorCommand.StateRestore)
            {
                var recovery = new FileXRegistrySyncStateStore(
                    LocalFileSystem.Instance, Path.Combine(settings.SnapshotDirectory!, settings.JobId));
                await using ConfiguredAsyncDisposable recoveryLifetime = recovery.ConfigureAwait(false);
                var recoveryManager = new XRegistrySyncStateManager(recovery, settings.JobId);
                XRegistrySyncStateManager source = settings.Command == XRegistryConnectorCommand.StateBackup
                    ? manager : recoveryManager;
                XRegistrySyncStateManager destination = settings.Command == XRegistryConnectorCommand.StateBackup
                    ? recoveryManager : manager;
                ByteString snapshot = await source.ExportSnapshotAsync(cancellationToken).ConfigureAwait(false);
                _ = await destination.RestoreIntoPristineAsync(snapshot, cancellationToken).ConfigureAwait(false);
                await XRegistryConnectorOutput.StateAsync(settings.Command,
                    await destination.ReadStatusAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else
            {
                int retired = settings.Command == XRegistryConnectorCommand.StateCompact
                    ? await manager.CompactAsync(settings.ExpectedGeneration, settings.AcknowledgedOperationIds,
                        cancellationToken).ConfigureAwait(false) : 0;
                await XRegistryConnectorOutput.StateAsync(settings.Command,
                    await manager.ReadStatusAsync(cancellationToken).ConfigureAwait(false), retired).ConfigureAwait(
                        false);
            }
            return 0;
        }

        private static XRegistrySyncConflictPolicy ParsePolicy(string value)
        {
            return value switch
            {
                "manual" => XRegistrySyncConflictPolicy.Manual,
                "prefer-opcua" => XRegistrySyncConflictPolicy.PreferOpcUa,
                "prefer-http" => XRegistrySyncConflictPolicy.PreferHttp,
                _ => throw new ArgumentException("Unknown conflict policy.", nameof(value))
            };
        }
    }
}
