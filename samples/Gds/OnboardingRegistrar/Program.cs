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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
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
    builder.Logging.AddConsole();

    int port = int.TryParse(builder.Configuration["port"], out int configuredPort)
        ? configuredPort
        : 62560;
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
    builder.Services.AddSingleton<ITicketStore>(_ => new MemoryTicketStore());
    builder.Services.AddSingleton<IUserDatabase>(userDatabase);
    builder.Services.AddSingleton<IUserManagement>(services =>
        new UserManagement(services.GetRequiredService<IUserDatabase>()));
    builder.Services.AddSingleton<IServerStartupTask>(
        new OnboardingReadyStartupTask(endpoint));
    builder.Services
        .AddOpcUa()
        .AddServer(options =>
        {
            options.ApplicationName = "OnboardingRegistrar";
            options.ApplicationUri =
                "urn:localhost:OPCFoundation:OnboardingRegistrar";
            options.ProductUri =
                "uri:opcfoundation.org:UA-.NETStandard:OnboardingRegistrar";
            options.PkiRoot = pkiRoot;
            options.AutoAcceptUntrustedCertificates = true;
            options.RejectSHA1Certificates = true;
            options.MinCertificateKeySize = 2048;
            options.IncludeSignAndEncryptPolicies = true;
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
        .AddNodeManager<OnboardingRegistrarNodeManagerFactory>();

    using IHost host = builder.Build();
    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
    await host.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
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
