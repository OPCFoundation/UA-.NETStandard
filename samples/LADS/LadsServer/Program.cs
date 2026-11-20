/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LadsSample.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.LADS.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = 62565;
if (builder.Configuration["port"] is { } portOption &&
    (!int.TryParse(portOption, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
{
    await Console.Error.WriteLineAsync("--port must be an integer from 1 through 65535.").ConfigureAwait(false);
    return 2;
}
string bindHost = builder.Configuration["host"] ?? "localhost";
if (Uri.CheckHostName(bindHost) == UriHostNameType.Unknown)
{
    await Console.Error.WriteLineAsync("--host must be a host name or IP address.").ConfigureAwait(false);
    return 2;
}
string? pkiRoot = builder.Configuration["pki-root"];
if (pkiRoot is not null && !Path.IsPathFullyQualified(pkiRoot))
{
    await Console.Error.WriteLineAsync("--pki-root must be an absolute path.").ConfigureAwait(false);
    return 2;
}
string? dataRoot = builder.Configuration["data-root"];
if (dataRoot is not null && !Path.IsPathFullyQualified(dataRoot))
{
    await Console.Error.WriteLineAsync("--data-root must be an absolute path.").ConfigureAwait(false);
    return 2;
}
bool autoAccept = true;
if (builder.Configuration["autoaccept"] is { } acceptOption && !bool.TryParse(acceptOption, out autoAccept))
{
    await Console.Error.WriteLineAsync("--autoaccept must be true or false.").ConfigureAwait(false);
    return 2;
}
int runSeconds = 0;
if (builder.Configuration["run-seconds"] is { } runOption &&
    (!int.TryParse(runOption, NumberStyles.None, CultureInfo.InvariantCulture, out runSeconds) ||
        runSeconds is < 1 or > 3600))
{
    await Console.Error.WriteLineAsync("--run-seconds must be an integer from 1 through 3600.").ConfigureAwait(false);
    return 2;
}
string endpointUrl = new UriBuilder("opc.tcp", bindHost, port, "/LadsServer").Uri.AbsoluteUri;
using var dataStore = dataRoot is null ? null : new FileSystemLadsStore(LocalFileSystem.Instance, dataRoot);

// The hardware accepts setpoints and the simulation moves the measured values towards them.
builder.Services.AddSingleton<ILadsFunctionProvider, IncubatorHardware>();
builder.Services.AddHostedService<IncubatorSimulation>();

builder.Services
    .AddOpcUa()
    .AddServer(
        options =>
        {
            options.ApplicationName = "LadsServer";
            options.ApplicationUri = "urn:localhost:OPCFoundation:LadsServer";
            options.ProductUri = "uri:opcfoundation.org:LadsServer";
            // Sample convenience only; never auto-accept untrusted certificates in production.
            options.AutoAcceptUntrustedCertificates = autoAccept;
            if (pkiRoot is not null)
            {
                options.PkiRoot = pkiRoot;
            }
            options.EndpointUrls.Add(endpointUrl);
        })
    .AddAssetManagement(options =>
    {
        if (dataRoot is not null)
        {
            options.UseFileSystemStores(Path.Combine(dataRoot, "assets"));
        }
    })
    .AddLads(options =>
    {
        options.Store = dataStore;
        options.ConfirmInterruptedDeviceReadinessAsync = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            // This simulator owns no physical equipment that needs inspection after interruption.
            return new ValueTask<bool>(true);
        };
    })
    .ConfigureLads(ShakingIncubator.Configure);

using IHost host = builder.Build();
using var runLifetime = new CancellationTokenSource();
if (runSeconds > 0)
{
    runLifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
await host.RunAsync(runLifetime.Token).ConfigureAwait(false);
return 0;
