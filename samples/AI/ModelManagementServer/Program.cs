// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Server.Fluent;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62640;

// 0.0.0.0 so the Server is reachable from outside a container. Override with
// --host for local-only development.
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

builder.Services.AddRestChatCompletionsAIChatClientFactory();

// InferenceBackend:Kind defaults to ChatClient, the Microsoft.Extensions.AI
// path. Set it to RestChatCompletions only for endpoints where the host cannot
// supply an IChatClient and the OpenAI-compatible REST contract is the wire
// contract itself.
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "ModelManagementServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:ModelManagementServer";
        o.ProductUri = "uri:opcfoundation.org:ModelManagementServer";
        // Sample convenience only; never auto-accept untrusted certificates in
        // production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/ModelManagementServer");
    })
    .AddAI(
        ai => builder.Configuration.GetSection(AIOptions.SectionName).Bind(ai),
        backend => builder.Configuration.GetSection(InferenceBackendOptions.SectionName).Bind(backend),
        fallback => builder.Configuration
            .GetSection(InferenceBackendOptions.FallbackSectionName)
            .Bind(fallback));

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
