/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Robotics.IntentEnabledRobot;
using Robotics.IntentEnabledRobot.Simulation;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = 62840;
if (builder.Configuration["port"] is { } portOption &&
    (!int.TryParse(portOption, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
{
    await Console.Error.WriteLineAsync("--port must be an integer from 1 through 65535.").ConfigureAwait(false);
    return 2;
}
string host = builder.Configuration["host"] is { Length: > 0 } configuredHost
    ? configuredHost
    : "localhost";
if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
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
bool autoAccept = true;
if (builder.Configuration["autoaccept"] is { } trustOption && !bool.TryParse(trustOption, out autoAccept))
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

builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IRobotIntentModelProvider, OpenUsdIntentModelProvider>());
builder.Services.AddSingleton<SimulatedArmExecutor>();
builder.Services.AddSingleton<SampleSafetySource>();
builder.Services.AddSingleton<IntentRobotCell>();
if (runSeconds == 0)
{
    builder.Services.AddHostedService<SafetyConsoleService>();
}

builder.Services
    .AddOpcUa()
    .AddServer(
        options =>
        {
            options.ApplicationName = "IntentEnabledRobot";
            options.ApplicationUri = "urn:localhost:OPCFoundation:IntentEnabledRobot";
            options.ProductUri = "uri:opcfoundation.org:IntentEnabledRobot";
            options.AutoAcceptUntrustedCertificates = autoAccept;
            if (pkiRoot is not null)
            {
                options.PkiRoot = pkiRoot;
            }
            options.EndpointUrls.Add(new UriBuilder("opc.tcp", host, port, "/IntentEnabledRobot").Uri.AbsoluteUri);
        })
    .ConfigureRoles(
        options => options.Roles.Add(
            new RoleDefinitionOptions
            {
                Name = BrowseNames.WellKnownRole_Operator,
                Identities =
                {
                    new RoleIdentityMappingOptions
                    {
                        CriteriaType = IdentityCriteriaType.Anonymous
                    }
                }
            }))
    .AddRobotIntent()
    .AddRobotIntentExecutor<SafetyAwareArmExecutor>()
    .ConfigureRobotIntent(
        async (context, cancellationToken) =>
            await context.GetRequiredService<IntentRobotCell>()
                .ConfigureAsync(context, cancellationToken).ConfigureAwait(false));

using IHost app = builder.Build();
using var runLifetime = new CancellationTokenSource();
if (runSeconds > 0)
{
    runLifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
await app.RunAsync(runLifetime.Token).ConfigureAwait(false);
return 0;
