/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Admits one explicitly configured device and an optional managed application after signed-ticket validation.
    /// The sample uses preconfigured certificate-store trust; it never autoaccepts discovery certificates.
    /// </summary>
    internal sealed class PushRegistrarSample :
        IDeviceRegistrarOnboardingPolicy,
        IDeviceRegistrarExternalAuthenticationPolicy,
        IDeviceRegistrarApplicationAuthorization,
        IDeviceRegistrarManagerProvider,
        IServerStartupTask
    {
        /// <summary>
        /// Captures the operator's product, DCA, endpoint and namespace-stable model addresses.
        /// </summary>
        private PushRegistrarSample(IConfiguration configuration, string product, string application)
        {
            RequireAbsoluteUri(product, "allowedProductInstanceUri");
            RequireAbsoluteUri(application, "dcaApplicationUri");
            m_product = product;
            m_application = application;
            m_endpoint = Required(configuration, "pushEndpoint");
            Uri endpoint = RequireAbsoluteUri(m_endpoint, "pushEndpoint");
            if (endpoint.Scheme != "opc.tcp")
            {
                throw new ArgumentException("--pushEndpoint must use opc.tcp.");
            }
            m_deviceId = ReadAddress(configuration, "pushDeviceId");
            m_singletonId = ReadAddress(configuration, "pushIsSingletonId");
            m_configurationId = ReadAddress(configuration, "pushServerConfigurationId");
            m_groupId = ReadAddress(configuration, "pushCertificateGroupId");
            m_typeId = ReadAddress(configuration, "pushCertificateTypeId");
            m_trustId = ReadAddress(configuration, "pushTrustListId");
            string? managedApplicationUri = configuration["managedApplicationUri"];
            if (!string.IsNullOrWhiteSpace(managedApplicationUri))
            {
                RequireAbsoluteUri(managedApplicationUri, "managedApplicationUri");
                m_managedApplicationUri = managedApplicationUri;
                m_managedApplicationEndpoint = Required(configuration, "managedApplicationEndpoint");
                RequireAbsoluteUri(m_managedApplicationEndpoint, "managedApplicationEndpoint");
                m_managedApplicationSecurityPolicyUri =
                    Required(configuration, "managedApplicationSecurityPolicyUri");
                RequireAbsoluteUri(
                    m_managedApplicationSecurityPolicyUri, "managedApplicationSecurityPolicyUri");
                m_managedApplicationCertificatePath =
                    Required(configuration, "managedApplicationCertificatePath");
                m_managedApplicationHttpsCertificatePath = configuration["managedApplicationHttpsCertificatePath"];
                if (m_managedApplicationHttpsCertificatePath != null &&
                    !Path.IsPathFullyQualified(m_managedApplicationHttpsCertificatePath))
                {
                    throw new ArgumentException(
                        "--managedApplicationHttpsCertificatePath must be an absolute public certificate path.");
                }
                string capabilities = Required(configuration, "managedApplicationServerCapabilities");
                string[] identifiers = capabilities.Split(',');
                for (int index = 0; index < identifiers.Length; index++)
                {
                    identifiers[index] = identifiers[index].Trim();
                    ArgumentException.ThrowIfNullOrWhiteSpace(identifiers[index]);
                }
                m_managedApplicationServerCapabilities = identifiers.ToArrayOf();
                if (!string.IsNullOrWhiteSpace(configuration["managedApplicationCertificateTypeId"]))
                {
                    m_managedApplicationCertificateTypeId =
                        ReadAddress(configuration, "managedApplicationCertificateTypeId");
                }
                if (!Path.IsPathFullyQualified(m_managedApplicationCertificatePath))
                {
                    throw new ArgumentException(
                        "--managedApplicationCertificatePath must be an absolute path.");
                }
            }
            else if (configuration["managedApplicationEndpoint"] != null ||
                configuration["managedApplicationSecurityPolicyUri"] != null ||
                configuration["managedApplicationCertificatePath"] != null ||
                configuration["managedApplicationHttpsCertificatePath"] != null ||
                configuration["managedApplicationServerCapabilities"] != null ||
                configuration["managedApplicationCertificateTypeId"] != null)
            {
                throw new ArgumentException(
                    "Managed application endpoint, security policy and certificate settings " +
                    "require --managedApplicationUri.");
            }
            if (m_typeId != new ExpandedNodeId(Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType) &&
                m_typeId != new ExpandedNodeId(
                    Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, Opc.Ua.Namespaces.OpcUa))
            {
                throw new ArgumentException("This sample requires the standard RSA SHA256 application type.");
            }
            string? softwareEndpoint = configuration["softwareManagerEndpoint"];
            if (softwareEndpoint != null)
            {
                RequireAbsoluteUri(softwareEndpoint, "softwareManagerEndpoint");
                string purpose = Required(configuration, "softwareManagerPurposeUri");
                string protocol = Required(configuration, "softwareManagerProtocolUri");
                RequireAbsoluteUri(purpose, "softwareManagerPurposeUri");
                RequireAbsoluteUri(protocol, "softwareManagerProtocolUri");
                m_softwareManager = new ManagerDescription
                {
                    Name = new LocalizedText("Externally operated sample software manager"),
                    IsRequired = true,
                    PurposeUri = purpose,
                    ProtocolUri = protocol,
                    EndpointUrls = [softwareEndpoint]
                };
            }
        }

        /// <summary>
        /// Registers GDS issuance, durable public-only checkpoints, the supervised processor and an optional
        /// host-configured managed-server target. Private keys remain in certificate stores.
        /// </summary>
        public static async Task ConfigureAsync(
            HostApplicationBuilder builder,
            IGdsServerBuilder gds,
            string pkiRoot,
            string product,
            string application)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(gds);
            var sample = new PushRegistrarSample(builder.Configuration, product, application);
            string pushUser = RequiredEnvironment("ONBOARDING_PUSH_USER");
            string pushPassword = RequiredEnvironment("ONBOARDING_PUSH_PASSWORD");
            byte[] password = Encoding.UTF8.GetBytes(pushPassword);
            IUserIdentity identity;
            try
            {
                identity = new UserIdentity(pushUser, password.AsSpan());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(password);
            }
            builder.Services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "OnboardingRegistrar";
                options.ApplicationUri = "urn:localhost:OPCFoundation:OnboardingRegistrar";
                options.ProductUri = "uri:opcfoundation.org:UA-.NETStandard:OnboardingRegistrar";
                options.SubjectName = Utils.ReplaceDCLocalhost(
                    "CN=OnboardingRegistrar, O=OPC Foundation, DC=localhost");
                options.PkiRoot = pkiRoot;
                options.AutoAcceptUntrustedCertificates = false;
                options.RejectSHA1SignedCertificates = true;
                options.MinimumCertificateKeySize = 2048;
            });
            if (sample.m_managedApplicationUri is { } managedApplicationUri &&
                sample.m_managedApplicationEndpoint is { } managedApplicationEndpoint &&
                sample.m_managedApplicationSecurityPolicyUri is { } managedApplicationSecurityPolicyUri &&
                sample.m_managedApplicationCertificatePath is { } managedApplicationCertificatePath)
            {
                UserNamePasswordIdentityProvider managedIdentity =
                    await CreateManagedApplicationIdentityAsync().ConfigureAwait(false);
                builder.Services.AddDeviceRegistrarManagedApplicationProvider(provider =>
                    new PushManagedApplicationTargetProvider(
                        managedApplicationUri,
                        managedApplicationEndpoint,
                        managedApplicationSecurityPolicyUri,
                        managedApplicationCertificatePath,
                        sample.m_managedApplicationServerCapabilities,
                        sample.m_managedApplicationCertificateTypeId,
                        managedIdentity,
                        () => provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration,
                        sample.m_managedApplicationHttpsCertificatePath));
                builder.Services.AddDeviceRegistrarManagedApplicationChannelVerifier(
                    provider => provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration);
            }
            builder.Services.AddSingleton(sample);
            builder.Services.AddSingleton<IDeviceRegistrarOnboardingPolicy>(sample);
            builder.Services.AddSingleton<IDeviceRegistrarApplicationAuthorization>(sample);
            builder.Services.AddSingleton<IDeviceRegistrarManagerProvider>(sample);
            builder.Services.AddSingleton<IServerStartupTask>(provider =>
            {
                sample.SetScheduler(provider.GetRequiredService<IDeviceRegistrarPushScheduler>());
                return sample;
            });
            builder.Services.AddSingleton(provider => new PushRegistrarCheckpointFiles(
                Path.Combine(pkiRoot, "push-checkpoints"), provider.GetService<IFileSystem>()));
            builder.Services.AddSingleton<ISecretStore>(provider => new PushEnrollmentCheckpointStore(
                provider.GetRequiredService<PushRegistrarCheckpointFiles>()));
            builder.Services.AddSingleton(provider => new PushRegistrarIssuanceStore(
                provider.GetRequiredService<PushRegistrarCheckpointFiles>()));
            builder.Services.AddSingleton<Opc.Ua.Gds.Server.ICertificateRequestIssuanceStore>(provider =>
                provider.GetRequiredService<PushRegistrarIssuanceStore>());
            ApplicationConfiguration? pushConfiguration = null;
            builder.Services.AddDeviceRegistrarPushConnections(
                provider => pushConfiguration =
                    provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration,
                (description, ct) => sample.SelectTargetAsync(
                    pushConfiguration ?? throw new InvalidOperationException("Push configuration is not initialized."),
                    identity, description, ct));
            gds.AddPreStartupTask<PushRegistrarIssuanceStore>()
                .AddDeviceRegistrarPushEnrollments(
                    "sample-push-registrar",
                    Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            builder.Services.AddFileSystemDeviceRegistrarPushWorkStore(Path.Combine(pkiRoot, "push-work.bin"));
            builder.Services.AddDeviceRegistrarPush();
            if (sample.m_softwareManager != null)
            {
                builder.Services.AddDeviceRegistrarSoftwareUpdateRole(
                    new ExpandedNodeId(Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin, Opc.Ua.Namespaces.OpcUa));
            }
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarOnboardingDecision> EvaluateAsync(
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(selection);
            RequireDevice(selection.ProductInstanceUri, cancellationToken);
            return new ValueTask<DeviceRegistrarOnboardingDecision>(new DeviceRegistrarOnboardingDecision(
                CreateRecord(), m_softwareManager != null, CoreUtils.Clone(m_softwareManager)));
        }

        /// <summary>
        /// Applies the configured device and application approval after external CA validation.
        /// </summary>
        public ValueTask<DeviceRegistrarExternalAuthenticationResult> EvaluateAsync(
            ByteString certificate,
            ArrayOf<string> certificateUris,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (certificate.IsEmpty || !certificateUris.Contains(m_product) ||
                !certificateUris.Contains(m_application))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return new ValueTask<DeviceRegistrarExternalAuthenticationResult>(
                new DeviceRegistrarExternalAuthenticationResult(m_product,
                    new DeviceRegistrarOnboardingDecision(
                        CreateRecord(), m_softwareManager != null, CoreUtils.Clone(m_softwareManager))));
        }

        /// <inheritdoc/>
        public ValueTask<bool> CanManageAsync(
            string productInstanceUri,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(application);
            cancellationToken.ThrowIfCancellationRequested();
            bool allowed = string.Equals(productInstanceUri, m_product, StringComparison.Ordinal) &&
                m_managedApplicationUri != null &&
                string.Equals(application.ApplicationUri, m_managedApplicationUri, StringComparison.Ordinal) &&
                application.ApplicationType is ApplicationType.Server or ApplicationType.ClientAndServer;
            return new ValueTask<bool>(allowed);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            RequireDevice(productInstanceUri, cancellationToken);
            ArrayOf<ManagerDescription> managers = m_softwareManager == null
                ? ArrayOf<ManagerDescription>.Empty
                : [CoreUtils.Clone(m_softwareManager)!];
            return new ValueTask<ArrayOf<ManagerDescription>>(managers);
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            IDeviceRegistrarPushScheduler scheduler = m_scheduler ??
                throw new InvalidOperationException("The sample push scheduler has not been initialized.");
            await scheduler.EnqueueAsync(new ApplicationDescription
            {
                ApplicationUri = m_application,
                ApplicationName = new LocalizedText("ProvisionableDevice"),
                ProductUri = "uri:opcfoundation.org:UA-.NETStandard:ProvisionableDevice",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = [m_endpoint]
            }, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("ONBOARDING_PUSH_QUEUED");
        }

        /// <summary>
        /// Injects the supervised scheduler after the provider set has been registered.
        /// </summary>
        private void SetScheduler(IDeviceRegistrarPushScheduler scheduler)
        {
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        /// <summary>
        /// Imports the managed password into a dedicated secret store and resolves identities per activation.
        /// </summary>
        private static async Task<UserNamePasswordIdentityProvider> CreateManagedApplicationIdentityAsync()
        {
            string userName = RequiredEnvironment("ONBOARDING_MANAGED_APPLICATION_USER");
            string password = RequiredEnvironment("ONBOARDING_MANAGED_APPLICATION_PASSWORD");
            var passwordStore = new InMemorySecretStore("OnboardingManagedApplicationCredentials");
            var passwordId = new SecretIdentifier("managed-application-password", passwordStore.StoreType);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                await passwordStore.SetAsync(passwordId, passwordBytes).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
            return new UserNamePasswordIdentityProvider(userName, new SecretRegistry(passwordStore), passwordId);
        }

        /// <summary>
        /// Selects only the operator's encrypted endpoint; discovery metadata never grants certificate trust.
        /// </summary>
        private async ValueTask<DeviceRegistrarPushTarget> SelectTargetAsync(
            ApplicationConfiguration configuration,
            IUserIdentity identity,
            ApplicationDescription application,
            CancellationToken ct)
        {
            if (!string.Equals(application.ApplicationUri, m_application, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            var targetUri = new Uri(m_endpoint);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = 15000;
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                configuration, targetUri, endpointConfiguration, ct: ct).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);
            foreach (EndpointDescription offered in endpoints)
            {
                if (offered.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    offered.SecurityPolicyUri != SecurityPolicies.Basic256Sha256 ||
                    offered.Server?.ApplicationUri != m_application ||
                    !Uri.TryCreate(offered.EndpointUrl, UriKind.Absolute, out Uri? offeredUri) ||
                    offeredUri.Scheme != targetUri.Scheme || offeredUri.Port != targetUri.Port ||
                    offeredUri.PathAndQuery != targetUri.PathAndQuery)
                {
                    continue;
                }
                EndpointDescription endpoint = CoreUtils.Clone(offered)!;
                endpoint.EndpointUrl = m_endpoint;
                return new DeviceRegistrarPushTarget(
                    new ConfiguredEndpoint(null, endpoint, endpointConfiguration),
                    identity, m_deviceId, m_singletonId, m_configurationId, m_groupId, m_typeId, m_trustId);
            }
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "No approved encrypted RSA endpoint identifies the target.");
        }

        /// <summary>
        /// Describes the device's server application, not this registrar's client application.
        /// </summary>
        private ApplicationRecordDataType CreateRecord()
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = m_application,
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("ProvisionableDevice")],
                ProductUri = "uri:opcfoundation.org:UA-.NETStandard:ProvisionableDevice",
                DiscoveryUrls = [m_endpoint],
                ServerCapabilities = []
            };
        }

        /// <summary>
        /// Keeps ticket validation separate from the operator's exact product admission decision.
        /// </summary>
        private void RequireDevice(string product, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(product, m_product, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Rejects transient namespace indexes, remote node addresses and implicit model identifiers.
        /// </summary>
        private static ExpandedNodeId ReadAddress(IConfiguration configuration, string name)
        {
            ExpandedNodeId address = ExpandedNodeId.Parse(Required(configuration, name));
            if (address.IsNull || address.ServerIndex != 0 ||
                (address.NamespaceIndex != 0 && string.IsNullOrEmpty(address.NamespaceUri)))
            {
                throw new ArgumentException($"--{name} requires a local nsu= URI or namespace-zero address.");
            }
            if (!string.IsNullOrEmpty(address.NamespaceUri))
            {
                RequireAbsoluteUri(address.NamespaceUri, name);
            }
            return address;
        }

        /// <summary>
        /// Reads an explicit nonempty command-line or host configuration value.
        /// </summary>
        private static string Required(IConfiguration configuration, string name)
        {
            string? value = configuration[name];
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Push registrar mode requires --{name}.");
        }

        /// <summary>
        /// Reads outbound credentials without allowing command-line password arguments or committed defaults.
        /// </summary>
        private static string RequiredEnvironment(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"Set {name} before starting push registrar mode.");
        }

        /// <summary>
        /// Requires an absolute URI for admission decisions and manager descriptions.
        /// </summary>
        private static Uri RequireAbsoluteUri(string value, string name)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                ? uri
                : throw new ArgumentException($"--{name} requires an absolute URI.");
        }

        /// <summary>The explicitly admitted product instance.</summary>
        private readonly string m_product;

        /// <summary>The only admitted DCA application URI.</summary>
        private readonly string m_application;

        /// <summary>The operator-selected device discovery and connection endpoint.</summary>
        private readonly string m_endpoint;

        /// <summary>The provisionable device object in its model namespace.</summary>
        private readonly ExpandedNodeId m_deviceId;

        /// <summary>The mandatory device singleton property.</summary>
        private readonly ExpandedNodeId m_singletonId;

        /// <summary>The target's server configuration object.</summary>
        private readonly ExpandedNodeId m_configurationId;

        /// <summary>The target's application certificate group.</summary>
        private readonly ExpandedNodeId m_groupId;

        /// <summary>The target's standard concrete certificate type.</summary>
        private readonly ExpandedNodeId m_typeId;

        /// <summary>The trust list belonging to the target certificate group.</summary>
        private readonly ExpandedNodeId m_trustId;

        /// <summary>The one managed Server or ClientAndServer application permitted by sample policy.</summary>
        private readonly string? m_managedApplicationUri;

        /// <summary>The exact endpoint URL selected for the managed application.</summary>
        private readonly string? m_managedApplicationEndpoint;

        /// <summary>The exact encrypted security policy selected for the managed application.</summary>
        private readonly string? m_managedApplicationSecurityPolicyUri;

        /// <summary>The operator-supplied public certificate file for the managed application.</summary>
        private readonly string? m_managedApplicationCertificatePath;

        /// <summary>
        /// The optional operator-selected HTTPS leaf certificate, separate from the application pin.
        /// </summary>
        private readonly string? m_managedApplicationHttpsCertificatePath;

        /// <summary>The explicit managed server capability identifiers selected by the operator.</summary>
        private readonly ArrayOf<string> m_managedApplicationServerCapabilities;

        /// <summary>The optional managed application certificate type selected by the operator.</summary>
        private readonly ExpandedNodeId m_managedApplicationCertificateTypeId;

        /// <summary>The existing software manager; this sample does not implement a firmware engine.</summary>
        private readonly ManagerDescription? m_softwareManager;

        /// <summary>The host-owned supervised push queue.</summary>
        private IDeviceRegistrarPushScheduler? m_scheduler;
    }
}
