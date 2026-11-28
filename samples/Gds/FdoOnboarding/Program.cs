// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Enrollment;
using Fdo.Transport.Http;
using FdoOnboarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnboardingClientSample;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Fdo;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;
using UaObjectTypeIds = Opc.Ua.ObjectTypeIds;

if ((args.Length != 2 && !(args.Length == 3 && args[2] == "--replace")) ||
    !Path.IsPathFullyQualified(args[0]) ||
    !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
    port is < 1024 or > 65535)
{
    Console.Error.WriteLine("Usage: FdoOnboarding <new absolute state directory> <loopback OPC UA port> [--replace]");
    return 2;
}

try
{
    string root = Path.GetFullPath(args[0]);
    if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
    {
        throw new InvalidOperationException("Use an empty state directory for each new demonstration run.");
    }
    Directory.CreateDirectory(root);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await RunAsync(root, port, args.Length == 3, timeout.Token).ConfigureAwait(false);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static async Task RunAsync(string root, int port, bool replace, CancellationToken ct)
{
    const string product = "urn:opcua:fdo-demo:product";
    const string applicationUri = "urn:opcua:fdo-demo:dca";
    const string deviceSubject = "CN=FDO Demo DCA";
    string devicePki = Path.Combine(root, "device");
    string registrarPki = Path.Combine(root, "registrar");
    var registrarUri = new UriBuilder("opc.tcp", IPAddress.Loopback.ToString(), port, "/FdoOnboarding").Uri;

    LoopbackFdoHost http = await LoopbackFdoHost.StartAsync(FdoBootstrap.MaxMessageSize, ct)
        .ConfigureAwait(false);
    await using var httpLifetime = http.ConfigureAwait(false);
    using var bootstrap = new FdoBootstrap(http.Address);
    BootstrappedDevice initialized = await bootstrap.RunAsync(http, ct).ConfigureAwait(false);
    using ECDsa? owner2 = replace ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
    using var replacement = owner2 == null ? null : new ReplacementDemo(root, initialized.Credential, owner2);
    ByteString guid = ByteString.From(initialized.Credential.DeviceGuid.Span);
    ByteString attestationPin = ByteString.From(SHA256.HashData(bootstrap.Attestation.Span));

    HostApplicationBuilder clientBuilder = CreateHostBuilder();
    clientBuilder.Services.AddOpcUa().AddClient(options =>
    {
        options.ApplicationName = "FDO Demo DCA";
        options.ApplicationUri = applicationUri;
        options.SubjectName = deviceSubject;
        options.PkiRoot = devicePki;
        options.AutoAcceptUntrustedCertificates = false;
        options.RejectSHA1SignedCertificates = true;
    }).AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = registrarUri.AbsoluteUri;
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    }).AddGdsClient().AddPullDcaWorkflow();
    clientBuilder.Services.AddSingleton(services => new OnboardingDeviceCredentials(
        services.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration,
        services.GetRequiredService<ITelemetryContext>()));
    clientBuilder.Services.AddSingleton<IPullDcaIdentityProvider>(services =>
        services.GetRequiredService<OnboardingDeviceCredentials>());
    clientBuilder.Services.AddSingleton<IPullDcaCredentialProvider>(services =>
        services.GetRequiredService<OnboardingDeviceCredentials>());
    IHost client = clientBuilder.Build();
    try
    {
        var application = client.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>();
        ITelemetryContext telemetry = client.Services.GetRequiredService<ITelemetryContext>();
        ApplicationConfiguration config = await application.GetAsync(ct).ConfigureAwait(false);
        ICertificateManager deviceCertificates = config.CertificateManager ??
            throw new InvalidOperationException("The DCA configuration did not initialize a certificate manager.");
        using CertificateEntry key = deviceCertificates.AcquireApplicationCertificateByType(
            UaObjectTypeIds.RsaSha256ApplicationCertificateType) ??
            throw new InvalidOperationException("The DCA configuration did not create its retained RSA key.");
        OnboardingDeviceCredentials credentials = client.Services.GetRequiredService<OnboardingDeviceCredentials>();
        ByteString csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(key.Certificate));

        string externalPki = Path.Combine(root, "external");
        var groupConfig = new CertificateGroupConfiguration
        {
            SubjectName = "CN=FDO Demo Enrollment CA",
            BaseStorePath = externalPki,
            CertificateTypes = [nameof(UaObjectTypeIds.RsaSha256ApplicationCertificateType)]
        };
        using var factory = new CertificateGroup(telemetry);
        using ICertificateGroup group = factory.Create(Path.Combine(externalPki, "authorities"), groupConfig);
        await group.InitAsync(ct).ConfigureAwait(false);
        if (group is not ICertificateGroupWithAuthenticatedSigningRequest issuer)
        {
            throw new InvalidOperationException("The dedicated certificate group cannot sign external requests.");
        }
        var trustReader = new CertificateManager(telemetry);
        await using var trustReaderLifetime = trustReader.ConfigureAwait(false);
        TrustListIdentifier enrollmentCa = ExternalAuthenticationProvider.AuthenticationAuthorities;
        trustReader.RegisterTrustList(enrollmentCa, groupConfig.TrustedListPath, groupConfig.IssuerListPath);
        using TrustListData authority = await trustReader.ReadTrustListAsync(enrollmentCa, TrustListMasks.All, ct)
            .ConfigureAwait(false);
        await ProvisionChannelTrustAsync(registrarPki, authority, telemetry, ct).ConfigureAwait(false);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pullPolicy = new PullRegistrarPolicy(product, applicationUri);
        HostApplicationBuilder serverBuilder = CreateHostBuilder();
        serverBuilder.Services.AddSingleton<ICertificateManager>(services =>
            new CertificateManager(services.GetRequiredService<ITelemetryContext>()));
        serverBuilder.Services.AddSingleton<IDeviceRegistrarOnboardingPolicy>(pullPolicy);
        serverBuilder.Services.AddSingleton<IDeviceRegistrarApplicationAuthorization>(pullPolicy);
        serverBuilder.Services.AddSingleton<IDeviceRegistrarPushScheduler>(pullPolicy);
        serverBuilder.Services.AddSingleton<IDeviceRegistrarManagerProvider>(pullPolicy);
        serverBuilder.Services.AddSingleton<IFdoEnrollmentReceiptStore>(_ =>
            replace
                ? new FdoSampleReceiptStore(Path.Combine(root, "receipts"), product, applicationUri)
                : new InMemoryFdoEnrollmentReceiptStore());
        serverBuilder.Services.AddSingleton<ITicketStore>(_ => new MemoryTicketStore());
        serverBuilder.Services.AddHostedFileSystemDeviceRegistrarLifecycle(Path.Combine(root, "device-lifecycle.json"));
        serverBuilder.Services.AddHostedDeviceRegistrarAuditing();
        serverBuilder.Services.AddOpcUa().AddGdsServer(options =>
        {
            options.ApplicationName = "FDO Demo Registrar";
            options.ApplicationUri = "urn:opcua:fdo-demo:registrar";
            options.SubjectName = "CN=FDO Demo Registrar, DC=localhost";
            options.PkiRoot = registrarPki;
            options.IncludeSignAndEncryptPolicies = true;
            options.IncludeUnsecurePolicyNone = false;
            options.AutoAcceptUntrustedCertificates = false;
            options.EndpointUrls.Add(registrarUri.AbsoluteUri);
        }).AddInMemoryStores()
            .AddDefaultIdentityAuthenticators(options =>
            {
                options.EnableAnonymous = true;
                options.EnableUserNamePassword = false;
                options.EnableX509 = false;
                options.EnableJwt = false;
                options.EnableGdsApplicationSelfAdminProvider = false;
            })
            .AddOnboardingTickets(Path.Combine(root, "ticket-trusted"), Path.Combine(root, "ticket-issuers"))
            .AddOnboardingIdentities(Path.Combine(root, "identity-trusted"), Path.Combine(root, "identity-issuers"))
            .AddDeviceRegistrarExternalAuthentication(groupConfig.TrustedListPath, groupConfig.IssuerListPath,
                services => new FdoRegistrarEnrollmentPolicy(
                    services.GetRequiredService<IFdoEnrollmentReceiptStore>(), pullPolicy,
                    product, applicationUri, deviceSubject, guid, attestationPin, issuer))
            .AddStartupTask((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                ready.TrySetResult();
                return default;
            })
            .AddDeviceRegistrar(_ => { });
        IHost server = serverBuilder.Build();
        try
        {
            var policy = (FdoRegistrarEnrollmentPolicy)server.Services
                .GetRequiredService<IDeviceRegistrarExternalAuthenticationPolicy>();
            FdoDeviceEnrollmentHandler deviceHandler = FdoDeviceEnrollmentSetup.CreateHandler(
                credentials, key.Certificate, authority, guid, csr, () => replacement?.RequireActivated());
            using TrustListData previousPeers = await deviceCertificates
                .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All, ct).ConfigureAwait(false);
            FdoDeviceEnrollment enrolled = await RunEnrollmentAsync(
                http, bootstrap, initialized, policy, deviceHandler, csr, replacement, ct).ConfigureAwait(false);
            using TrustListData currentPeers = await deviceCertificates
                .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All, ct).ConfigureAwait(false);
            RequireSamePeerTrust(previousPeers, currentPeers);
            using CertificateEntry? installed = deviceCertificates.AcquireApplicationCertificateByType(
                UaObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (installed?.Certificate is not { HasPrivateKey: true } installedLeaf ||
                !installedLeaf.RawData.AsSpan().SequenceEqual(enrolled.Certificate.Span))
            {
                throw new InvalidOperationException("The enrolled DCA leaf is not active with its private key.");
            }

            await server.StartAsync(ct).ConfigureAwait(false);
            await ready.Task.WaitAsync(ct).ConfigureAwait(false);
            await TrustRegistrarAsync(registrarPki, deviceCertificates, telemetry, ct).ConfigureAwait(false);
            await client.StartAsync(ct).ConfigureAwait(false);
            Func<CancellationToken, Task<ManagedSession>> connect =
                client.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession session = await connect(ct).ConfigureAwait(false);
            NodeId registrarNode = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar, session.NamespaceUris);
            NodeId directoryNode = ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, session.NamespaceUris);
            var registrar = new PullDcaRegistrar(registrarUri, registrarNode, directoryNode);
            PullDcaExternalAuthenticationResult result = await client.Services.GetRequiredService<PullDcaWorkflow>()
                .RunExternalAuthenticationAsync(registrar, [], cancellationToken: ct).ConfigureAwait(false);
            IDeviceRegistrarIdentityLifecycle lifecycle =
                server.Services.GetRequiredService<IDeviceRegistrarIdentityLifecycle>();
            DeviceRegistrarIdentityCandidate? admitted = await lifecycle.FindCandidateByCertificateAsync(
                ByteString.From(enrolled.Certificate.Span), ct).ConfigureAwait(false);
            if (admitted?.ProductInstanceUri != product)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "The authenticated registrar did not admit the enrolled Device.");
            }
            Console.WriteLine($"FDO_ONBOARDING_COMPLETE managers={result.Managers.Count}");
        }
        finally
        {
            await DisposeHostAsync(server).ConfigureAwait(false);
        }
    }
    finally
    {
        await DisposeHostAsync(client).ConfigureAwait(false);
    }
}

static async Task<FdoDeviceEnrollment> RunEnrollmentAsync(
    LoopbackFdoHost http,
    FdoBootstrap bootstrap,
    BootstrappedDevice initialized,
    IFdoOwnerEnrollmentHandler ownerHandler,
    IFdoDeviceEnrollmentHandler deviceHandler,
    ByteString csr,
    ReplacementDemo? replacement,
    CancellationToken ct)
{
    const ushort max = FdoBootstrap.MaxMessageSize;
    var voucher = initialized.Voucher;
    var credential = initialized.Credential;
    using var owner = new FdoOwnerSession(new FdoOwnerSessionOptions
    {
        EncodedVoucherHeaderTag = voucher.EncodedVoucherHeaderTag,
        EncodedVoucherHeaderHmac = voucher.EncodedVoucherHeaderHmac,
        EncodedVoucherEntries = voucher.EncodedVoucherEntries,
        EncodedDeviceCertificateChain = voucher.EncodedDeviceCertificateChain,
        TrustedManufacturerKey = bootstrap.ManufacturerTrust,
        DeviceTrustAnchor = bootstrap.AttestationRoot,
        DeviceRevocationMode = X509RevocationMode.NoCheck,
        OwnerSigningKey = bootstrap.OwnerKey,
        EnrollmentHandler = ownerHandler,
        KeyExchangeSuite = FdoPairedKeyExchangeSuite.Ecdh256,
        CipherSuite = FdoPairedCipherSuite.A128Gcm,
        AllowRendezvous = true,
        AllowRendezvousBypass = false,
        AllowCredentialReuse = replacement == null,
        Replacement = replacement?.OwnerOptions,
        UseProvisionalPairedProfile = true,
        MaxMessageSize = max,
        MaxDeviceServiceInfoSize = 4096
    });
    using var device = new FdoDeviceSession(new FdoDeviceSessionOptions
    {
        DeviceGuid = credential.DeviceGuid,
        EncodedManufacturerKeyHash = credential.EncodedManufacturerKeyHash,
        VoucherHmacAlgorithm = credential.VoucherHmacAlgorithm,
        VoucherHmacSecretHandle = credential.VoucherHmacSecret,
        AttestationKey = bootstrap.DeviceKey,
        CertificateSigningRequest = csr.ToArray(),
        KeyExchangeSuite = FdoPairedKeyExchangeSuite.Ecdh256,
        CipherSuite = FdoPairedCipherSuite.A128Gcm,
        RendezvousResult = initialized.Redirect,
        AllowRendezvousBypass = false,
        AllowCredentialReuse = replacement == null,
        ReplacementHandler = replacement,
        UseProvisionalPairedProfile = true,
        MaxMessageSize = max,
        MaxOwnerServiceInfoSize = 4096,
        Metadata = new FdoDeviceMetadata("demo", "demo", "1", "opcua-dca", ":", "demo"),
        EnrollmentHandler = deviceHandler
    });
    replacement?.Bind(device);
    using var binding = FdoHttpServerSession.ForOwner(owner, max);
    FdoDeviceEnrollment result = await http.RunAsync(binding,
        token => FdoHttp.RunDeviceAsync(device, http.Address, max, token), ct).ConfigureAwait(false);
    if (!device.DoneAckAuthenticated || !owner.DoneAckHandoffConfirmed)
    {
        throw new InvalidOperationException("The protected TO2 exchange did not complete.");
    }
    if (replacement != null)
    {
        await replacement.VerifyAsync(owner.CommittedReplacement, device.ActivatedReplacement, ct)
            .ConfigureAwait(false);
        Console.WriteLine("FDO_REPLACEMENT_ACTIVATED after=DoneAck20 ownerVoucher=verified dca=installed");
    }
    return result;
}

static void RequireSamePeerTrust(TrustListData before, TrustListData after)
{
    if (!Same(before.TrustedCertificates, after.TrustedCertificates,
            cert => Fingerprint(cert.RawData.AsSpan())) ||
        !Same(before.IssuerCertificates, after.IssuerCertificates,
            cert => Fingerprint(cert.RawData.AsSpan())) ||
        !Same(before.TrustedCrls, after.TrustedCrls, crl => Fingerprint(crl.RawData.AsSpan())) ||
        !Same(before.IssuerCrls, after.IssuerCrls, crl => Fingerprint(crl.RawData.AsSpan())))
    {
        throw new InvalidOperationException("Enrollment unexpectedly changed OPC UA peer trust.");
    }

    static string Fingerprint(ReadOnlySpan<byte> der) => Convert.ToHexString(SHA256.HashData(der));

    static bool Same<T>(IEnumerable<T> oldValues, IEnumerable<T> newValues, Func<T, string> fingerprint) =>
        oldValues.Select(fingerprint).Order(StringComparer.Ordinal)
            .SequenceEqual(newValues.Select(fingerprint).Order(StringComparer.Ordinal), StringComparer.Ordinal);
}

static HostApplicationBuilder CreateHostBuilder()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.EnableOpcUaRedaction();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    return builder;
}

static async Task ProvisionChannelTrustAsync(
    string registrarPki, TrustListData authority, ITelemetryContext telemetry, CancellationToken ct)
{
    using ICertificateStore trusted = OpenDirectoryStore(Path.Combine(registrarPki, "trusted"), telemetry);
    foreach (Certificate anchor in authority.TrustedCertificates)
    {
        await trusted.AddAsync(anchor, ct: ct).ConfigureAwait(false);
    }
    foreach (X509CRL crl in authority.TrustedCrls)
    {
        await trusted.AddCRLAsync(crl, ct).ConfigureAwait(false);
    }
}

static async Task TrustRegistrarAsync(
    string registrarPki, ICertificateManager device, ITelemetryContext telemetry, CancellationToken ct)
{
    using ICertificateStore own = OpenDirectoryStore(registrarPki, telemetry);
    using CertificateCollection certificates = await own.EnumerateAsync(ct).ConfigureAwait(false);
    Certificate registrar = certificates.Single(cert => CertificateIdentifier.ValidateCertificateType(
        cert, UaObjectTypeIds.RsaSha256ApplicationCertificateType));
    using TrustListData peers = await device.ReadTrustListAsync(TrustListIdentifier.Peers,
        TrustListMasks.TrustedCertificates, ct).ConfigureAwait(false);
    using Certificate publicCertificate = Certificate.FromRawData(registrar.RawData);
    peers.TrustedCertificates.Add(publicCertificate);
    await device.WriteTrustListAsync(TrustListIdentifier.Peers, peers, TrustListMasks.TrustedCertificates, ct)
        .ConfigureAwait(false);
}

static ICertificateStore OpenDirectoryStore(string path, ITelemetryContext telemetry)
{
    return new CertificateStoreIdentifier(path, true).OpenStore(telemetry);
}

static async ValueTask DisposeHostAsync(IHost host)
{
    try
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(stop.Token).ConfigureAwait(false);
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
}
