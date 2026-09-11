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
                if (settings.Command is XRegistryConnectorCommand.Conflicts or XRegistryConnectorCommand.Resolve)
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
                await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
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
                    await XRegistryConnectorOutput.ReadyAsync("opcua-gateway",
                        host.Services.GetRequiredService<XRegistryBridgeNativeOptions>().RootAddress.ToString())
                        .ConfigureAwait(false);
                    await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                    return 0;
                }
                var state = new FileXRegistrySyncStateStore(
                    LocalFileSystem.Instance, Path.Combine(settings.StateDirectory!, settings.JobId));
                await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
                var options = new XRegistrySyncOptions(settings.JobId,
                    settings.OpcUaEndpoint!.AbsoluteUri + "#" + settings.RegistryNodeId,
                    settings.HttpRoot!.AbsoluteUri)
                {
                    ConflictPolicy = ParsePolicy(settings.ConflictPolicy),
                    PropagateDeletes = settings.PropagateDeletes,
                    OpcUaContext = context,
                    HttpContext = context
                };
                var synchronizer = new XRegistrySynchronizer(
                    native!, http!, state, options, host.Services.GetRequiredService<ITelemetryContext>());
                while (true)
                {
                    XRegistrySyncReport report = await synchronizer.RunOnceAsync(settings.DryRun, cancellationToken)
                        .ConfigureAwait(false);
                    await XRegistryConnectorOutput.ReportAsync(report).ConfigureAwait(false);
                    if (settings.Once || settings.DryRun || report.Status == XRegistrySyncStatus.Failed)
                    {
                        return report.ExitCode;
                    }
                    await Task.Delay(settings.PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (session is not null)
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
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
                        https.ServerCertificateSelector = (_, _) => selectedCertificate ??
                            throw new InvalidOperationException("The managed HTTPS certificate is not available."));
                }
            });
            WebApplication app = builder.Build();
            await using ConfiguredAsyncDisposable appLifetime = app.ConfigureAwait(false);
            using CertificateEntry? entry = settings.ListenAddress.Scheme == Uri.UriSchemeHttps
                ? await AcquireCertificateAsync(app.Services, cancellationToken).ConfigureAwait(false) : null;
            using X509Certificate2? tls = entry?.Certificate?.AsX509Certificate2();
            selectedCertificate = tls;
            ManagedSession session = await app.Services
                .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()(cancellationToken)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
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
            app.MapXRegistry(settings.PublicHttpRoot!.AbsolutePath.TrimEnd('/'), endpoint,
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
                try
                {
                    XRegistryEndpointDescription description = await endpoint.InspectAsync(
                        OperatorContext(settings.CredentialProfile), context.RequestAborted).ConfigureAwait(false);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(XRegistryConnectorOutput.Readiness(true,
                        description.SupportsAtomicMutations &&
                        description.SupportsConditionalMutations &&
                        description.SupportsWriteTouch &&
                        description.SupportsPreparedMutations),
                        context.RequestAborted).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is ServiceResultException or IOException or HttpRequestException)
                {
                    context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync(XRegistryConnectorOutput.Readiness(false), context.RequestAborted)
                        .ConfigureAwait(false);
                }
            });
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            await XRegistryConnectorOutput.ReadyAsync("http-gateway", settings.PublicHttpRoot.AbsoluteUri)
                .ConfigureAwait(false);
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return 0;
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
                SecurityPolicies.Basic256Sha256) ??
                throw new InvalidOperationException(
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
            else
            {
                await XRegistryConnectorOutput.ConflictsAsync(
                    await manager.ListConflictsAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
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
