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
using AiModelManagement.Bridge;
using AiModelManagement.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Server.Fluent;

const string fallbackKey = AiModelManagementNodeManagerFactory.FallbackOptionsName;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62640;

// 0.0.0.0 so the Server is reachable from outside a container. Override with
// --host for local-only development.
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

builder.Services.Configure<AiModelManagementOptions>(
    builder.Configuration.GetSection(AiModelManagementOptions.SectionName));

builder.Services.Configure<InferenceBackendOptions>(
    builder.Configuration.GetSection(InferenceBackendOptions.SectionName));

builder.Services.Configure<InferenceBackendOptions>(
    fallbackKey,
    builder.Configuration.GetSection(InferenceBackendOptions.FallbackSectionName));

// Two deployments mean two backends. A fallback reached through the same client,
// connection and credentials as the primary is a retry, not a fallback.
builder.Services.AddSingleton(sp =>
{
    IOptionsMonitor<InferenceBackendOptions> monitor =
        sp.GetRequiredService<IOptionsMonitor<InferenceBackendOptions>>();
    ILoggerFactory loggers = sp.GetRequiredService<ILoggerFactory>();

    InferenceBackendOptions primaryOptions = monitor.CurrentValue;

    var primary = new RestChatCompletionsBackend(
        primaryOptions,
        CredentialResolverFor(primaryOptions),
        loggers.CreateLogger<RestChatCompletionsBackend>());

    InferenceBackendOptions fallbackOptions = monitor.Get(fallbackKey);

    if (!fallbackOptions.Enabled)
    {
        return new InferenceBackends(primary);
    }

    var fallback = new RestChatCompletionsBackend(
        fallbackOptions,
        CredentialResolverFor(fallbackOptions),
        loggers.CreateLogger<RestChatCompletionsBackend>());

    return new InferenceBackends(primary, fallback);
});

builder.Services.AddSingleton<AiModelManagementNodeManagerFactory>();

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "AiModelManagementServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:AiModelManagementServer";
        o.ProductUri = "uri:opcfoundation.org:AiModelManagementServer";
        // Sample convenience only; never auto-accept untrusted certificates in
        // production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/AiModelManagementServer");
    })
    .AddNodeManager<AiModelManagementNodeManagerFactory>();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;

// Which resolver a backend gets follows from how it says it authenticates, so a
// misconfigured pair cannot silently fall back to reading a secret from disk.
static ICredentialResolver CredentialResolverFor(InferenceBackendOptions options)
{
    return options.Authentication switch
    {
        BackendAuthentication.Anonymous => new NullCredentialResolver(),
        BackendAuthentication.WorkloadIdentity =>
            new WorkloadIdentityCredentialResolver(options.TokenAudience),
        _ => new FileCredentialResolver(options.CredentialDirectory)
    };
}
