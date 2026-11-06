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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using OnboardingRegistrar;

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.EnableOpcUaRedaction();
    builder.Logging.AddConsole();

    int port = 62560;
    string? configuredPort = builder.Configuration["port"];
    if (configuredPort != null && (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
    {
        throw new ArgumentException("--port must be an integer from 1 to 65535.");
    }
    string pkiRoot = builder.Configuration["pkiRoot"] ??
        Path.Combine(
            Path.GetTempPath(),
            "opcua-onboarding-demo",
            "registrar-pki");
    string endpoint = $"opc.tcp://localhost:{port}/OnboardingRegistrar";
    string userName = GetRequiredEnvironmentVariable("ONBOARDING_DEMO_USER");
    string password = GetRequiredEnvironmentVariable("ONBOARDING_DEMO_PASSWORD");
    var userDatabase = new LinqUserDatabase();
    byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
    try
    {
        if (!userDatabase.CreateUser(
            userName,
            passwordBytes,
            [Role.AuthenticatedUser]))
        {
            throw new InvalidOperationException(
                "Could not create the onboarding demo administrator.");
        }
    }
    finally
    {
        CryptographicOperations.ZeroMemory(passwordBytes);
    }
    bool runPullRegistrar = GetBooleanOption(builder, "pullRegistrar");
    bool runPushRegistrar = GetBooleanOption(builder, "pushRegistrar");
    bool runExternalAuthentication = GetBooleanOption(builder, "externalAuthentication");
    if (runExternalAuthentication && !runPullRegistrar && !runPushRegistrar)
    {
        throw new ArgumentException("External authentication requires --pullRegistrar or --pushRegistrar.");
    }
    if (runPullRegistrar && runPushRegistrar)
    {
        throw new ArgumentException("Select only one of --pullRegistrar and --pushRegistrar.");
    }
    if (runPushRegistrar && (string.IsNullOrWhiteSpace(builder.Configuration["pkiRoot"]) ||
        !Path.IsPathFullyQualified(pkiRoot)))
    {
        throw new ArgumentException("Push registrar mode requires an absolute, operator-controlled --pkiRoot.");
    }
    if (runPullRegistrar || runPushRegistrar)
    {
        builder.Services.AddFileSystemOnboardingTicketStore(Path.Combine(pkiRoot, "tickets.bin"));
    }
    else
    {
        builder.Services.AddSingleton<ITicketStore>(_ => new MemoryTicketStore());
    }
    builder.Services.AddSingleton<ICertificateManager>(services =>
    {
        return new CertificateManager(services.GetRequiredService<ITelemetryContext>());
    });
    builder.Services.AddSingleton<IUserDatabase>(userDatabase);
    builder.Services.AddSingleton<IUserManagement>(services =>
        new UserManagement(services.GetRequiredService<IUserDatabase>()));
    builder.Services.AddSingleton<IServerStartupTask>(
        new OnboardingReadyStartupTask(endpoint));
    if (runPullRegistrar || runPushRegistrar)
    {
        await ConfigureConnectedRegistrarAsync(builder, pkiRoot, endpoint, userName, runPushRegistrar)
            .ConfigureAwait(false);
    }
    else
    {
        builder.Services
            .AddOpcUa()
            .AddServer(options =>
            {
                options.ApplicationName = "OnboardingRegistrar";
                options.SubjectName = Utils.ReplaceDCLocalhost(
                    "CN=OnboardingRegistrar, O=OPC Foundation, DC=localhost");
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:OnboardingRegistrar";
                options.ProductUri =
                    "uri:opcfoundation.org:UA-.NETStandard:OnboardingRegistrar";
                options.PkiRoot = pkiRoot;
                options.AutoAcceptUntrustedCertificates = false;
                options.RejectSHA1Certificates = true;
                options.MinCertificateKeySize = 2048;
                options.IncludeSignAndEncryptPolicies = true;
                options.ConfigureBuilder = server => server.SetAuditingEnabled(true);
                options.IncludeUnsecurePolicyNone = false;
                options.IncludeEccPolicies = false;
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                {
                    TokenType = UserTokenType.Anonymous
                });
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                {
                    TokenType = UserTokenType.UserName
                });
                options.EndpointUrls.Add(endpoint);
            })
            .AddDefaultIdentityAuthenticators(options =>
            {
                options.EnableAnonymous = true;
                options.EnableUserNamePassword = true;
                options.EnableX509 = false;
                options.EnableJwt = false;
            })
            .AddIdentityAugmenter(_ =>
                new OnboardingRegistrarAdminAugmenter(userName))
            .AddOnboardingTickets(
                Path.Combine(pkiRoot, "ticket-authorities", "trusted"),
                Path.Combine(pkiRoot, "ticket-authorities", "issuers"))
            .AddOnboardingIdentities(
                Path.Combine(pkiRoot, "identity-authorities", "trusted"),
                Path.Combine(pkiRoot, "identity-authorities", "issuers"))
            .AddNodeManager<OnboardingRegistrarNodeManagerFactory>();
    }

    IHost host = builder.Build();
    try
    {
        if (runPushRegistrar)
        {
            await host.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                .GetAsync(CancellationToken.None).ConfigureAwait(false);
        }
        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await host.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
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
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static string GetRequiredEnvironmentVariable(string name)
{
    string? value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Set the {name} environment variable before starting the demo.");
    }
    return value;
}

static async Task ConfigureConnectedRegistrarAsync(
    HostApplicationBuilder builder,
    string pkiRoot,
    string endpoint,
    string userName,
    bool runPushRegistrar)
{
    string product = builder.Configuration["allowedProductInstanceUri"] ??
        throw new ArgumentException("Connected registrar mode requires --allowedProductInstanceUri.");
    string application = builder.Configuration["dcaApplicationUri"] ??
        throw new ArgumentException("Connected registrar mode requires --dcaApplicationUri.");
    bool autoApprove = GetBooleanOption(builder, "autoApprove");
    Directory.CreateDirectory(pkiRoot);
    JsonApplicationsDatabase database = JsonApplicationsDatabase.Load(Path.Combine(pkiRoot, "applications.json"));
    builder.Services.AddSingleton<IApplicationsDatabase>(database);
    builder.Services.AddSingleton<ICertificateRequest>(database);
    builder.Services.AddHostedFileSystemDeviceRegistrarLifecycle(Path.Combine(pkiRoot, "device-lifecycle.json"));
    builder.Services.AddHostedDeviceRegistrarAuditing();
    if (!runPushRegistrar)
    {
        var policy = new PullRegistrarPolicy(product, application);
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton<IDeviceRegistrarOnboardingPolicy>(policy);
        builder.Services.AddSingleton<IDeviceRegistrarApplicationAuthorization>(policy);
        builder.Services.AddSingleton<IDeviceRegistrarPushScheduler>(policy);
        builder.Services.AddSingleton<IDeviceRegistrarManagerProvider>(policy);
    }
    var gds = builder.Services.AddOpcUa()
        .AddGdsServer(options =>
        {
            options.ApplicationName = "OnboardingRegistrar";
            options.ApplicationUri = "urn:localhost:OPCFoundation:OnboardingRegistrar";
            options.ProductUri = "uri:opcfoundation.org:UA-.NETStandard:OnboardingRegistrar";
            options.SubjectName = Utils.ReplaceDCLocalhost("CN=OnboardingRegistrar, O=OPC Foundation, DC=localhost");
            options.PkiRoot = pkiRoot;
            options.AutoAcceptUntrustedCertificates = false;
            options.IncludeSignAndEncryptPolicies = true;
            options.IncludeUnsecurePolicyNone = false;
            options.AutoApprove = autoApprove;
            options.EndpointUrls.Add(endpoint);
        })
        .AddInMemoryStores()
        .AddDefaultIdentityAuthenticators(options =>
        {
            options.EnableAnonymous = true;
            options.EnableUserNamePassword = true;
            options.EnableX509 = false;
            options.EnableJwt = false;
            options.EnableGdsApplicationSelfAdminProvider = false;
        })
        .AddIdentityAugmenter(_ => new OnboardingRegistrarAdminAugmenter(
            userName, true, runPushRegistrar && builder.Configuration["softwareManagerEndpoint"] != null))
        .AddOnboardingTickets(
            Path.Combine(pkiRoot, "ticket-authorities", "trusted"),
            Path.Combine(pkiRoot, "ticket-authorities", "issuers"))
        .AddOnboardingIdentities(
            Path.Combine(pkiRoot, "identity-authorities", "trusted"),
            Path.Combine(pkiRoot, "identity-authorities", "issuers"));
    if (runPushRegistrar)
    {
        await PushRegistrarSample.ConfigureAsync(builder, gds, pkiRoot, product, application).ConfigureAwait(false);
    }
    else
    {
        gds.AddSecretStoreCertificateRequestIssuanceStore(new InMemorySecretStore(), "pull-registrar");
    }
    if (GetBooleanOption(builder, "externalAuthentication"))
    {
        gds.AddDeviceRegistrarExternalAuthentication(
            Path.Combine(pkiRoot, "external-authentication", "trusted"),
            Path.Combine(pkiRoot, "external-authentication", "issuers"),
            services => runPushRegistrar
                ? services.GetRequiredService<PushRegistrarSample>()
                : services.GetRequiredService<PullRegistrarPolicy>());
    }
    gds.AddDeviceRegistrar(_ => { });
}

static bool GetBooleanOption(HostApplicationBuilder builder, string name)
{
    string? value = builder.Configuration[name];
    if (value == null)
    {
        return false;
    }
    if (!bool.TryParse(value, out bool result))
    {
        throw new ArgumentException($"--{name} must be true or false.");
    }
    return result;
}
