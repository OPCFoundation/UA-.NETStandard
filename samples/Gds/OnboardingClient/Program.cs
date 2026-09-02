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
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Error);

    string endpoint = builder.Configuration["endpoint"] ??
        "opc.tcp://localhost:62560/OnboardingRegistrar";
    string pkiRoot = builder.Configuration["pkiRoot"] ??
        Path.Combine(
            Path.GetTempPath(),
            "opcua-onboarding-demo",
            "client-pki");
    bool useAnonymous = bool.TryParse(
        builder.Configuration["anonymous"],
        out bool anonymous) && anonymous;
    IClientIdentityProvider? identityProvider = null;
    if (!useAnonymous)
    {
        string userName = GetRequiredEnvironmentVariable("ONBOARDING_DEMO_USER");
        string password = GetRequiredEnvironmentVariable("ONBOARDING_DEMO_PASSWORD");
        var passwordStore = new InMemorySecretStore();
        var passwordId = new SecretIdentifier(
            "onboarding-demo-password",
            passwordStore.StoreType);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            await passwordStore.SetAsync(
                    passwordId,
                    passwordBytes,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
        identityProvider = new UserNamePasswordIdentityProvider(
            userName,
            new SecretRegistry(passwordStore),
            passwordId);
    }

    IOpcUaClientBuilder clientBuilder = builder.Services
        .AddOpcUa()
        .AddClient(options =>
        {
            options.ApplicationName = "OnboardingClient";
            options.ApplicationUri =
                "urn:localhost:OPCFoundation:OnboardingClient";
            options.ProductUri =
                "uri:opcfoundation.org:UA-.NETStandard:OnboardingClient";
            options.PkiRoot = pkiRoot;
            options.AutoAcceptUntrustedCertificates = true;
            options.RejectSHA1SignedCertificates = true;
            options.MinimumCertificateKeySize = 2048;
            options.Session = new ManagedSessionOptions
            {
                SessionName = "OnboardingClient",
                SessionTimeout = TimeSpan.FromSeconds(60)
            };
        })
        .AddDiscoveryAndConnect(options =>
        {
            options.DiscoveryUrl = endpoint;
            options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
        });
    if (identityProvider != null)
    {
        clientBuilder.AddIdentityProvider(identity => identity.Add(identityProvider));
    }
    clientBuilder
        .AddGdsClient()
        .AddOnboardingClient();

    using IHost host = builder.Build();
    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
        Func<CancellationToken, Task<ManagedSession>> connect =
            host.Services.GetRequiredService<
                Func<CancellationToken, Task<ManagedSession>>>();
        ManagedSession session = await connect(CancellationToken.None)
            .ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            Console.WriteLine("Server namespaces:");
            for (int i = 0; i < session.NamespaceUris.Count; i++)
            {
                Console.WriteLine($"  ns={i}: {session.NamespaceUris.GetString((uint)i)}");
            }
            NodeId registrarId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar_Administration,
                session.NamespaceUris);
            if (registrarId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The server did not publish the OPC 10000-21 namespace.");
            }

            Func<NodeId, CancellationToken, ValueTask<OnboardingClient>>
                createOnboardingClient = host.Services.GetRequiredService<
                    Func<NodeId, CancellationToken, ValueTask<OnboardingClient>>>();
            OnboardingClient onboarding = await createOnboardingClient(
                    registrarId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<ByteString> tickets =
            [
                new ByteString(Encoding.UTF8.GetBytes("demo-ticket-one")),
                new ByteString(Encoding.UTF8.GetBytes("demo-ticket-two"))
            ];

            ArrayOf<StatusCode> registered = await onboarding
                .RegisterTicketsAsync(tickets)
                .ConfigureAwait(false);
            RequireStatus(registered, 0, StatusCodes.Good, "register ticket one");
            RequireStatus(registered, 1, StatusCodes.Good, "register ticket two");
            Console.WriteLine(
                $"REGISTER {registered[0]} {registered[1]}");

            ArrayOf<StatusCode> removed = await onboarding
                .UnregisterTicketsAsync([tickets[0]])
                .ConfigureAwait(false);
            RequireStatus(removed, 0, StatusCodes.Good, "unregister ticket one");
            Console.WriteLine($"UNREGISTER {removed[0]}");

            ArrayOf<StatusCode> removedAgain = await onboarding
                .UnregisterTicketsAsync([tickets[0]])
                .ConfigureAwait(false);
            RequireStatus(
                removedAgain,
                0,
                StatusCodes.BadNotFound,
                "unregister ticket one again");
            Console.WriteLine($"UNREGISTER_AGAIN {removedAgain[0]}");
            Console.WriteLine("ONBOARDING_DEMO_OK");
        }
    }
    finally
    {
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
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

static void RequireStatus(
    ArrayOf<StatusCode> statuses,
    int index,
    StatusCode expected,
    string operation)
{
    if (statuses.Count <= index || statuses[index] != expected)
    {
        StatusCode actual = statuses.Count > index
            ? statuses[index]
            : StatusCodes.BadNoData;
        throw new ServiceResultException(
            StatusCodes.BadUnexpectedError,
            $"{operation} returned {actual}; expected {expected}.");
    }
}
