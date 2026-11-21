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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using OnboardingDevice;
using OnboardingRegistrar;
using OnboardingClientSample;
using DeviceRegistrarTypeClient = Opc.Ua.Onboarding.DeviceRegistrarTypeClient;

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.EnableOpcUaRedaction();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Error);

    int timeoutSeconds = 90;
    string? configuredTimeout = builder.Configuration["operationTimeoutSeconds"];
    if (configuredTimeout != null &&
        (!int.TryParse(configuredTimeout, out timeoutSeconds) || timeoutSeconds is < 1 or > 3600))
    {
        throw new ArgumentException("--operationTimeoutSeconds must be an integer from 1 to 3600.");
    }
    using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    CancellationToken ct = operation.Token;
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
    bool runPull = bool.TryParse(builder.Configuration["pull"], out bool pull) && pull;
    OnboardingSoftwareStatus? softwareStatus =
        OnboardingSoftwareStatus.FromConfiguration(builder.Configuration);
    string? externalAuthentication = builder.Configuration["externalAuthentication"];
    if (externalAuthentication != null && !bool.TryParse(externalAuthentication, out _))
    {
        throw new ArgumentException("--externalAuthentication must be true or false.");
    }
    bool runExternalAuthentication = bool.TryParse(externalAuthentication, out bool external) && external;
    if (runExternalAuthentication && (!runPull ||
        builder.Configuration["signingJournal"] != null ||
        builder.Configuration["resumeApplicationId"] != null ||
        builder.Configuration["resumeRequestId"] != null ||
        builder.Configuration["resumeSigningKeyCertificate"] != null ||
        builder.Configuration["exportSigningKeyCertificate"] != null ||
        builder.Configuration["deviceTicket"] != null ||
        builder.Configuration["compositeTicket"] != null ||
        (bool.TryParse(builder.Configuration["prepareTickets"], out bool prepare) && prepare)))
    {
        throw new ArgumentException(
            "External authentication requires --pull and installed DCA credentials; " +
            "omit ticket and recovery options.");
    }
    string? signingJournal = builder.Configuration["signingJournal"];
    string? signingKeyStore = builder.Configuration["signingKeyStore"];
    bool discardSigningRequest =
        bool.TryParse(builder.Configuration["discardSigningRequest"], out bool discard) && discard;
    if ((signingJournal == null) != (signingKeyStore == null) || (signingJournal != null && !runPull))
    {
        throw new ArgumentException("--signingJournal and --signingKeyStore are used together with --pull.");
    }
    if (discardSigningRequest && signingJournal == null)
    {
        throw new ArgumentException("--discardSigningRequest requires --signingJournal and --signingKeyStore.");
    }
    if (builder.Configuration["resumeApplicationId"] != null ||
        builder.Configuration["resumeRequestId"] != null ||
        builder.Configuration["resumeSigningKeyCertificate"] != null)
    {
        throw new ArgumentException(
            "The --resume* options do not retain the original channel binding. " +
            "Use --signingJournal and --signingKeyStore from the initial request for restart recovery.");
    }
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
                    ct)
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
            options.SubjectName = Utils.ReplaceDCLocalhost("CN=OnboardingClient, O=OPC Foundation, DC=localhost");
            options.ApplicationUri =
                "urn:localhost:OPCFoundation:OnboardingClient";
            options.ProductUri =
                "uri:opcfoundation.org:UA-.NETStandard:OnboardingClient";
            options.PkiRoot = pkiRoot;
            options.AutoAcceptUntrustedCertificates = false;
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
    IGdsClientBuilder gds = clientBuilder.AddGdsClient().AddOnboardingClient();
    ManagedSession? selectedSession = null;
    if (runPull)
    {
        builder.Services.AddSingleton(services => new OnboardingDeviceCredentials(
            services.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration,
            services.GetRequiredService<ITelemetryContext>(),
            services.GetServices<ICertificateStoreProvider>().ToArray().ToArrayOf()));
        builder.Services.AddSingleton<IPullDcaIdentityProvider>(services =>
            services.GetRequiredService<OnboardingDeviceCredentials>());
        builder.Services.AddSingleton<IPullDcaCredentialProvider>(services =>
            services.GetRequiredService<OnboardingDeviceCredentials>());
        builder.Services.AddSingleton<IPullDcaNewKeyPairCredentialProvider>(services =>
            services.GetRequiredService<OnboardingDeviceCredentials>());
        builder.Services.AddSingleton<IPullDcaBoundCredentialProvider>(services =>
            services.GetRequiredService<OnboardingDeviceCredentials>());
        builder.Services.AddSingleton<IPullDcaSigningKeyEvidenceProvider>(services =>
            services.GetRequiredService<OnboardingDeviceCredentials>());
        if (signingJournal != null)
        {
            builder.Services.AddSingleton<IPullDcaSigningRecoveryProvider>(services =>
                services.GetRequiredService<OnboardingDeviceCredentials>());
            builder.Services.AddSingleton(_ => new PushRegistrarCheckpointFiles(signingJournal + ".pull"));
            builder.Services.AddSingleton(services => new DevicePullJournal(
                services.GetRequiredService<PushRegistrarCheckpointFiles>(),
                services.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration.ApplicationUri ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The DCA application URI is missing."),
                builder.Configuration["registrarApplicationUri"],
                services.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<IPullDcaSigningJournal>(services =>
                services.GetRequiredService<DevicePullJournal>());
            builder.Services.AddSingleton<IPullDcaCertificateReceiptStore>(services =>
                services.GetRequiredService<DevicePullJournal>());
        }
        builder.Services.AddSingleton<Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ManagedSession session = selectedSession ??
                    throw new InvalidOperationException("The sample has not connected to its selected registrar.");
                if (session.Disposed)
                {
                    throw new ObjectDisposedException(nameof(ManagedSession));
                }
                return new ValueTask<ManagedSession>(session);
            });
        gds.AddPullDcaWorkflow();
    }

    using IHost host = builder.Build();
    if (runExternalAuthentication)
    {
        host.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
            .Application.DisableCertificateAutoCreation = true;
    }
    if (bool.TryParse(builder.Configuration["prepareTickets"], out bool prepareTickets) && prepareTickets)
    {
        string registrarPkiRoot = builder.Configuration["registrarPkiRoot"] ??
            throw new InvalidOperationException("Set --registrarPkiRoot when preparing demo certificates.");
        string trustPath = builder.Configuration["ticketAuthorityTrustPath"] ??
            throw new InvalidOperationException("Set --ticketAuthorityTrustPath when preparing demo tickets.");
        await OnboardingDemoTickets.PrepareAsync(
            pkiRoot, registrarPkiRoot, trustPath, builder.Configuration["untrustedClientPkiRoot"],
            host.Services.GetRequiredService<ITelemetryContext>(),
            ct)
            .ConfigureAwait(false);
        Console.WriteLine("ONBOARDING_TICKETS_READY");
        return 0;
    }
    await host.StartAsync(ct).ConfigureAwait(false);
    try
    {
        DevicePullJournalCheckpoint? retainedPull = null;
        bool appliedPullRecovered = false;
        if (signingJournal != null)
        {
            DevicePullJournal publicJournal = host.Services.GetRequiredService<DevicePullJournal>();
            OnboardingDeviceCredentials credentials = host.Services.GetRequiredService<OnboardingDeviceCredentials>();
            credentials.UsePullJournal(publicJournal);
            retainedPull = await publicJournal.ReadAsync(ct).ConfigureAwait(false);
            if (retainedPull != null)
            {
                if (discardSigningRequest)
                {
                    throw new InvalidOperationException(
                        "A retained pull attempt cannot be discarded by deleting its signing-key journal.");
                }
                if (retainedPull.Stage == DevicePullJournalStage.DispatchUnknown)
                {
                    throw new InvalidOperationException(
                        "The retained StartSigningRequest outcome is unknown; no new request was dispatched.");
                }
                appliedPullRecovered = await credentials.RecoverAppliedPullReceiptAsync(retainedPull, ct)
                    .ConfigureAwait(false);
                if (!appliedPullRecovered)
                {
                    await credentials.ActivateAsync(retainedPull.Binding.ClientChannelCertificate, ct)
                        .ConfigureAwait(false);
                }
            }
        }
        Func<CancellationToken, Task<ManagedSession>> connect =
            host.Services.GetRequiredService<
                Func<CancellationToken, Task<ManagedSession>>>();
        ManagedSession session = await connect(ct)
            .ConfigureAwait(false);
        await using var sessionLifetime = session.ConfigureAwait(false);
        selectedSession = session;
        if (appliedPullRecovered)
        {
            DevicePullJournalCheckpoint installed = await host.Services.GetRequiredService<DevicePullJournal>()
                .ReadForSessionAsync(session.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
            PullDcaCredentials installedCredentials = installed.Credentials ??
                throw new InvalidOperationException("The installed pull receipt has no retained credentials.");
            SessionRequestContext recoveredContext = await SessionRequestContext.CaptureConfidentialAsync(session, ct)
                .ConfigureAwait(false);
            await using var recoveredContextLifetime = recoveredContext.ConfigureAwait(false);
            ITransportChannel channel = recoveredContext.Session.TransportChannel;
            if (!installedCredentials.Certificate.Span.SequenceEqual(channel.ClientChannelCertificate) ||
                !installed.Binding.RegistrarChannelCertificate.Span.SequenceEqual(channel.ServerChannelCertificate) ||
                !string.Equals(installed.Binding.RegistrarApplicationUri,
                    channel.EndpointDescription.Server?.ApplicationUri, StringComparison.Ordinal) ||
                !Uri.TryCreate(channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? recoveredEndpoint) ||
                !installed.Binding.Registrar.EndpointUrl.Equals(recoveredEndpoint) ||
                recoveredContext.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The recovered installation connected to a different channel.");
            }
            Console.WriteLine($"PULL_DCA_REQUEST_INSTALLED {installed.Binding.ApplicationId}");
            return 0;
        }
        if (softwareStatus != null)
        {
            NodeId deviceRegistrarId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar, session.NamespaceUris);
            if (deviceRegistrarId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The server did not publish the OPC 10000-21 namespace.");
            }
            var registrar = new DeviceRegistrarTypeClient(
                session, deviceRegistrarId, host.Services.GetRequiredService<ITelemetryContext>());
            await softwareStatus.ReportAsync(registrar, ct).ConfigureAwait(false);
            Console.WriteLine("ONBOARDING_SOFTWARE_STATUS_REPORTED");
            return 0;
        }
        if (runPull)
        {
            ApplicationConfiguration configuration = await host.Services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>().GetAsync(ct)
                .ConfigureAwait(false);
            ICertificateManager certificates = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            using CertificateEntry identity = certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            var issuerBytes = new ByteString[identity.IssuerChain.Count];
            for (int i = 0; i < issuerBytes.Length; i++)
            {
                issuerBytes[i] = ByteString.From(identity.IssuerChain[i].RawData.AsSpan());
            }
            NodeId deviceRegistrar = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar, session.NamespaceUris);
            NodeId directory = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, session.NamespaceUris);
            NodeId group = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, session.NamespaceUris);
            var registrar = new PullDcaRegistrar(new Uri(endpoint), deviceRegistrar, directory);
            var profile = new PullDcaCertificateProfile(group, ObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (runExternalAuthentication)
            {
                PullDcaExternalAuthenticationResult externalResult =
                    await host.Services.GetRequiredService<PullDcaWorkflow>()
                        .RunExternalAuthenticationAsync(registrar, [], cancellationToken: ct).ConfigureAwait(false);
                Console.WriteLine($"PULL_DCA_EXTERNAL_AUTHENTICATED {externalResult.Managers.Count}");
                return 0;
            }
            if (signingJournal != null)
            {
                OnboardingDeviceCredentials credentials =
                    host.Services.GetRequiredService<OnboardingDeviceCredentials>();
                credentials.UseSigningJournal(
                    signingJournal,
                    new CertificateStoreIdentifier(
                        signingKeyStore ?? throw new InvalidOperationException("Set --signingKeyStore."),
                        CertificateStoreType.Directory,
                        false),
                    session.NamespaceUris);
                OnboardingSigningRequestState state = await credentials.GetSigningRequestStateAsync(ct)
                    .ConfigureAwait(false);
                DevicePullJournalCheckpoint? sessionPull = retainedPull == null
                    ? null
                    : await host.Services.GetRequiredService<DevicePullJournal>()
                        .ReadForSessionAsync(session.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
                if (sessionPull != null && state == OnboardingSigningRequestState.Prepared)
                {
                    PullDcaSigningBinding binding = sessionPull.Binding;
                    await credentials.OnSigningRequestStartedAsync(
                        binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile,
                        binding.SigningRequest, ct).ConfigureAwait(false);
                    state = await credentials.GetSigningRequestStateAsync(ct).ConfigureAwait(false);
                }
                if (discardSigningRequest)
                {
                    await credentials.DiscardSigningRequestAsync(ct).ConfigureAwait(false);
                    Console.WriteLine($"PULL_DCA_REQUEST_DISCARDED {state}");
                    return 0;
                }
                switch (state)
                {
                    case OnboardingSigningRequestState.Started:
                        OnboardingJournaledSigningRequest journaled = await credentials
                            .RestoreJournaledSigningRequestAsync(registrar, ct).ConfigureAwait(false);
                        PullDcaSigningBinding binding = sessionPull?.Binding ??
                            throw new InvalidOperationException(
                                "The signing request has no durable original channel binding; " +
                                "Finish was not dispatched.");
                        ByteString retainedKey = await credentials.GetSigningKeyCertificateAsync(
                            journaled.ApplicationId, journaled.Profile, binding.SigningRequest, ct)
                            .ConfigureAwait(false);
                        if (journaled.Registrar != binding.Registrar ||
                            journaled.ApplicationId != binding.ApplicationId ||
                            journaled.RequestId != binding.RequestId ||
                            journaled.Profile != binding.Profile ||
                            retainedKey != binding.SigningKeyCertificate)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityChecksFailed,
                                "The signing journals identify different attempts.");
                        }
                        await host.Services.GetRequiredService<PullDcaWorkflow>()
                            .CompleteCertificateRequestAsync(binding, cancellationToken: ct)
                            .ConfigureAwait(false);
                        await session.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
                        await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
                        Console.WriteLine($"PULL_DCA_REQUEST_INSTALLED {journaled.ApplicationId}");
                        return 0;
                    case OnboardingSigningRequestState.Prepared:
                        throw new InvalidOperationException(
                            "The StartSigningRequest outcome is unknown; recovery requires retained request evidence.");
                    case OnboardingSigningRequestState.Installed:
                        throw new InvalidOperationException(
                            "The signing-key journal is installed but its public receipt was not verified; " +
                            "the retained attempt was not discarded.");
                    case OnboardingSigningRequestState.None:
                        if (sessionPull != null)
                        {
                            throw new InvalidOperationException(
                                "A retained pull attempt has no matching signing-key journal; " +
                                "no new Start was dispatched.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown signing request state {state}.");
                }
            }
            string ticketPath = builder.Configuration["deviceTicket"] ??
                throw new InvalidOperationException("Set --deviceTicket to this device's signed ticket.");
            string? signingKeySnapshot = builder.Configuration["exportSigningKeyCertificate"];
            if (signingKeySnapshot != null)
            {
                await File.WriteAllBytesAsync(signingKeySnapshot, identity.Certificate.RawData, ct)
                    .ConfigureAwait(false);
            }
            var offer = new PullDcaIdentityOffer(
                [ByteString.From(identity.Certificate.RawData.AsSpan())], issuerBytes,
                [await File.ReadAllTextAsync(ticketPath, ct).ConfigureAwait(false)]);
            var request = new PullDcaRequest(offer, profile, []);
            PullDcaResult result;
            try
            {
                result = await host.Services.GetRequiredService<PullDcaWorkflow>()
                    .RunAsync(registrar, request, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (PullDcaCertificatePendingException pending)
            {
                Console.WriteLine("PULL_DCA_PENDING_APPLICATION " +
                    NodeId.ToExpandedNodeId(pending.ApplicationId, session.NamespaceUris));
                Console.WriteLine("PULL_DCA_PENDING_REQUEST " +
                    NodeId.ToExpandedNodeId(pending.RequestId, session.NamespaceUris));
                throw;
            }
            Console.WriteLine($"PULL_DCA_INSTALLED {result.Identity.ApplicationId}");
            return 0;
        }
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

        var onboarding = new OnboardingClient(
            session, registrarId, host.Services.GetRequiredService<ITelemetryContext>());
        ArrayOf<string> tickets =
        [
            await File.ReadAllTextAsync(Path.Combine(pkiRoot, "ticket-1.json"), ct).ConfigureAwait(false),
            await File.ReadAllTextAsync(Path.Combine(pkiRoot, "ticket-2.json"), ct).ConfigureAwait(false)
        ];

        ArrayOf<StatusCode> registered = await onboarding
            .RegisterTicketsAsync(tickets, ct)
            .ConfigureAwait(false);
        RequireStatus(registered, 0, StatusCodes.Good, "register ticket one");
        RequireStatus(registered, 1, StatusCodes.Good, "register ticket two");
        Console.WriteLine(
            $"REGISTER {registered[0]} {registered[1]}");
        ArrayOf<StatusCode> invalid = await onboarding.RegisterTicketsAsync(["{}"], ct).ConfigureAwait(false);
        RequireStatus(invalid, 0, StatusCodes.BadTicketInvalid, "reject malformed ticket");
        Console.WriteLine($"INVALID_TICKET_REJECTED {invalid[0]}");

        ArrayOf<StatusCode> removed = await onboarding
            .UnregisterTicketsAsync([tickets[0]], ct)
            .ConfigureAwait(false);
        RequireStatus(removed, 0, StatusCodes.Good, "unregister ticket one");
        Console.WriteLine($"UNREGISTER {removed[0]}");

        ArrayOf<StatusCode> removedAgain = await onboarding
            .UnregisterTicketsAsync([tickets[0]], ct)
            .ConfigureAwait(false);
        RequireStatus(
            removedAgain,
            0,
            StatusCodes.BadNotFound,
            "unregister ticket one again");
        Console.WriteLine($"UNREGISTER_AGAIN {removedAgain[0]}");
        Console.WriteLine("ONBOARDING_DEMO_OK");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(cleanup.Token).ConfigureAwait(false);
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
