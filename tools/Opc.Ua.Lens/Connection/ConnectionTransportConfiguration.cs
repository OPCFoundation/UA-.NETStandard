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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace UaLens.Connection;

internal sealed record ConnectionSetupSelection(
    string EndpointUrl,
    ReverseConnectionProfile? ReverseConnection = null,
    string? ApplicationIdentityId = null);

internal sealed record ReverseConnectionProfile
{
    public required string ListenerUrl { get; init; }

    public required string ServerUri { get; init; }

    public required string EndpointUrl { get; init; }

    public string? TlsConfigurationId { get; init; }

    public int WaitTimeoutSeconds { get; init; } = 20;

    public int HoldTimeSeconds { get; init; } = 15;

    public void Validate(string? endpointUrl = null, string? serverUri = null)
    {
        ConnectionReference.ValidateUri(ServerUri);
        if (ListenerUrl is null || ListenerUrl.Length > 2048 ||
            EndpointUrl is null || EndpointUrl.Length > 2048 ||
            !Uri.TryCreate(ListenerUrl, UriKind.Absolute, out Uri? listener) ||
            !Uri.TryCreate(EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
            !listener.IsWellFormedOriginalString() || !endpoint.IsWellFormedOriginalString() ||
            string.IsNullOrEmpty(listener.Host) || string.IsNullOrEmpty(endpoint.Host) ||
            !string.IsNullOrEmpty(listener.UserInfo) || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(listener.Query) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(listener.Fragment) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            listener.Port <= 0 || endpoint.Port <= 0 ||
            listener.Scheme != endpoint.Scheme ||
            listener.Scheme is not ("opc.tcp" or "wss" or "opc.wss"))
        {
            throw new ArgumentException(
                "Reverse connect requires explicit TCP/WSS listener and matching-scheme server URLs " +
                "without credentials.");
        }
        if (WaitTimeoutSeconds is < 1 or > 300 || HoldTimeSeconds is < 1 or > 60)
        {
            throw new ArgumentException("Reverse wait must be 1–300 seconds and hold time 1–60 seconds.");
        }
        if (TlsConfigurationId is { } tls)
        {
            ConnectionReference.Validate(tls);
        }
        if (listener.Scheme is "wss" or "opc.wss" && TlsConfigurationId is null)
        {
            throw new InvalidOperationException(
                "WSS reverse connect requires a configured listener TLS certificate and peer validator.");
        }
        if ((endpointUrl is not null && !ConnectionProfile.EndpointUrlsMatch(endpointUrl, EndpointUrl)) ||
            (serverUri is not null && !string.Equals(serverUri, ServerUri, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The reverse peer must match the selected endpoint and ServerUri exactly.");
        }
    }

    public bool MatchesPeer(string serverUri, Uri endpointUrl)
    {
        return endpointUrl is not null && endpointUrl.IsAbsoluteUri &&
            endpointUrl.IsWellFormedOriginalString() && endpointUrl.AbsoluteUri.Length <= 2048 &&
            string.IsNullOrEmpty(endpointUrl.UserInfo) && string.IsNullOrEmpty(endpointUrl.Query) &&
            string.IsNullOrEmpty(endpointUrl.Fragment) &&
            string.Equals(ServerUri, serverUri, StringComparison.Ordinal) &&
            ConnectionProfile.EndpointUrlsMatch(EndpointUrl, endpointUrl.AbsoluteUri);
    }

    public ReverseConnectClientConfiguration CreateConfiguration()
    {
        Validate();
        return new ReverseConnectClientConfiguration
        {
            ClientEndpoints = [new ReverseConnectClientEndpoint { EndpointUrl = ListenerUrl }],
            HoldTime = HoldTimeSeconds * 1000,
            WaitTimeout = WaitTimeoutSeconds * 1000
        };
    }
}

internal sealed record ConnectionTransportCapability(
    string Scheme,
    bool ForwardRegistered,
    bool ReverseRegistered,
    string Prerequisites)
{
    public bool RequiresListenerTls => Scheme is "wss" or "opc.wss";

    public override string ToString()
    {
        return $"{Scheme}: forward {(ForwardRegistered ? "registered" : "not registered")}; " +
            $"reverse {(ReverseRegistered ? "registered" : "unavailable")} — {Prerequisites}";
    }
}

/// <summary>
/// Uses the same explicitly registered factories for discovery, sessions and
/// capability presentation. No assembly probing, implicit TLS or new transport.
/// </summary>
internal sealed class ConnectionTransportCatalog : ITransportChannelBindings
{
    public ConnectionTransportCatalog(
        ITransportBindingRegistry? bindings = null,
        ArrayOf<string> additionalSchemes = default)
    {
        Bindings = bindings ?? CreateDefaultBindings();
        var schemes = new HashSet<string>(StringComparer.Ordinal)
        {
            "opc.tcp", "https", "opc.https", "wss", "opc.wss", "opc.wss+json", "opc.quic"
        };
        foreach (string scheme in additionalSchemes)
        {
            schemes.Add(ValidateScheme(scheme));
        }
        var ordered = new List<string>(schemes);
        ordered.Sort(StringComparer.Ordinal);
        m_schemes = [.. ordered];
    }

    public ITransportBindingRegistry Bindings { get; }

    public ArrayOf<ConnectionTransportCapability> Capabilities
    {
        get
        {
            var result = new List<ConnectionTransportCapability>();
            foreach (string scheme in m_schemes)
            {
                result.Add(GetCapability(scheme));
            }
            return [.. result];
        }
    }

    public ConnectionTransportCapability GetCapability(string scheme)
    {
        scheme = ValidateScheme(scheme);
        bool forward = Bindings.HasChannelFactory(scheme);
        bool reverse = scheme is "opc.tcp" or "wss" or "opc.wss" &&
            forward && Bindings.HasListenerFactory(scheme);
        return new ConnectionTransportCapability(
            scheme,
            forward,
            reverse,
            scheme switch
            {
                "opc.tcp" =>
                    "UA-TCP. Reverse mode also needs the server's reverse target and inbound firewall access.",
                "https" or "opc.https" =>
                    "UA-binary HTTPS endpoint, platform TLS support and a trusted TLS chain are required. " +
                    "This workflow does not support reverse HTTPS.",
                "wss" or "opc.wss" =>
                    "Registered WSS binding, platform TLS/WebSocket support and trusted TLS chains are required. " +
                    "Reverse WSS also needs an explicit listener certificate and peer validator.",
                "opc.wss+json" =>
                    "Registered JSON WebSocket binding and its authentication flow are required; " +
                    "registration alone does not make an OpenAPI endpoint usable by the UA-binary session flow.",
                "opc.quic" =>
                    "Optional QUIC binding, compatible platform and UDP access are required; " +
                    "reverse mode is not supported by this workflow.",
                _ => "Host-registered binding; verify its platform and network prerequisites externally. " +
                    "Reverse mode is not supported by this workflow."
            });
    }

    public void RequireForward(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint) ||
            endpointUrl.Length > 2048 || !endpoint.IsWellFormedOriginalString() ||
            endpointUrl.AsSpan().IndexOfAny('\r', '\n', '\t') >= 0 ||
            string.IsNullOrEmpty(endpoint.Host) || endpoint.Port == 0 ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("Enter a bounded endpoint URL without embedded credentials or query strings.");
        }
        if (!Bindings.HasChannelFactory(ValidateScheme(endpoint.Scheme)))
        {
            throw new NotSupportedException("No client transport is registered for the selected endpoint scheme.");
        }
    }

    public static string? GetSessionProfileUnavailableReason(EndpointDescription endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Profiles.IsHttpsOpenApi(endpoint.TransportProfileUri) ||
            Profiles.IsWssOpenApi(endpoint.TransportProfileUri)
            ? "This session flow uses UA-binary transports. Select the binary endpoint; " +
                "OpenAPI requires its separately configured HTTP authentication flow."
            : null;
    }

    public void RequireReverse(ReverseConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        RequireForward(profile.EndpointUrl);
        if (!Bindings.HasListenerFactory(new Uri(profile.ListenerUrl).Scheme))
        {
            throw new NotSupportedException("No reverse listener transport is registered for this scheme.");
        }
    }

    /// <summary>
    /// Checks safe setup intent and named registrations without acquiring keys,
    /// creating a channel, starting a listener or expanding certificate trust.
    /// </summary>
    public void RequireSetup(
        ConnectionSetupSelection setup,
        ConnectionConfigurationCatalog configurations,
        ConnectionProfile? selectedProfile = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(configurations);
        RequireForward(setup.EndpointUrl);
        if (selectedProfile is not null)
        {
            selectedProfile.Validate();
            if (!ConnectionProfile.EndpointUrlsMatch(setup.EndpointUrl, selectedProfile.EndpointUrl) ||
                setup.ReverseConnection != selectedProfile.ReverseConnection ||
                !string.Equals(setup.ApplicationIdentityId, selectedProfile.ApplicationIdentityId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The setup differs from the explicitly selected endpoint, direction or application identity.");
            }
            string? unavailable = GetSessionProfileUnavailableReason(new EndpointDescription(
                selectedProfile.EndpointUrl)
            {
                TransportProfileUri = selectedProfile.TransportProfileUri
            });
            if (unavailable is not null)
            {
                throw new NotSupportedException(unavailable);
            }
        }
        if (setup.ApplicationIdentityId is { } application)
        {
            ConnectionReference.Validate(application);
            configurations.ResolveApplication(application).ValidateConfiguration(selectedProfile?.SecurityPolicyUri);
        }
        if (setup.ReverseConnection is { } reverse)
        {
            RequireReverse(reverse);
            reverse.Validate(setup.EndpointUrl, selectedProfile?.ServerApplicationUri);
            if (reverse.TlsConfigurationId is { } tls)
            {
                configurations.ResolveTls(tls);
            }
        }
    }

    public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
    {
        return Bindings.CreateChannel(uriScheme, telemetry);
    }

    private static DefaultTransportBindingRegistry CreateDefaultBindings()
    {
        DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry.WithDefaultTcp();
        registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
        registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());
        return registry;
    }

    private static string ValidateScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme) || scheme.Length > 128 || !Uri.CheckSchemeName(scheme))
        {
            throw new ArgumentException("A bounded URI scheme name is required, not a URL or provider path.");
        }
        return scheme.ToLowerInvariant();
    }

    private readonly ArrayOf<string> m_schemes;
}

/// <summary>
/// Trusted host configuration for an application-instance key. The factory must
/// return a fresh, independently owned certificate manager with this key wired
/// into the stack. A user workspace can select the name, never load its module.
/// </summary>
internal sealed class ConfiguredApplicationIdentity
{
    public ConfiguredApplicationIdentity(
        string id,
        string displayName,
        ConfiguredCertificateSource source,
        CertificateIdentityReference certificate,
        Func<CertificateIdentifier, ICertificatePasswordProvider,
            CancellationToken, Task<ApplicationConfiguration>> factory)
    {
        ConnectionReference.Validate(id);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(factory);
        certificate.Validate();
        if (source.Purpose != CryptoPurpose.ApplicationInstanceKey)
        {
            throw new ArgumentException("An application configuration requires an ApplicationInstanceKey source.");
        }
        Id = id;
        DisplayName = displayName;
        Source = source;
        Certificate = certificate;
        m_factory = factory;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public ConfiguredCertificateSource Source { get; }

    public CertificateIdentityReference Certificate { get; }

    public void ValidateConfiguration(string? securityPolicy = null)
    {
        Source.CreateIdentifier(Certificate);
        Source.ResolvePasswordSource(Certificate.PasswordSourceId);
        if (securityPolicy is not null)
        {
            Source.RequireCryptoProvider(CryptoPurpose.ApplicationInstanceKey, securityPolicy);
        }
        else if (Source.CryptoProviderName is not null && Source.CryptoProviders is null)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.RequiresConfiguration,
                "The application-key crypto provider registry is absent. No alternate key will be selected.");
        }
    }

    public async Task<ApplicationConfiguration> CreateAsync(string securityPolicy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Source.RequireCryptoProvider(CryptoPurpose.ApplicationInstanceKey, securityPolicy);
        CertificateIdentifier identifier = Source.CreateIdentifier(Certificate);
        ICertificatePasswordProvider passwords = Source.ResolvePasswordSource(Certificate.PasswordSourceId);
        try
        {
            return await m_factory(identifier, passwords, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Application key acquisition was canceled.", ct);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or CryptographicException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Denied,
                "Application-key access was denied. Check the configured provider/device and password/PIN source.");
        }
        catch (ServiceResultException error)
        {
            throw new ConnectionIdentityException(
                error.StatusCode == StatusCodes.BadUserAccessDenied ||
                    error.StatusCode == StatusCodes.BadCertificateUseNotAllowed
                    ? ConnectionIdentityFailure.Denied
                    : ConnectionIdentityFailure.Unavailable,
                "The application-key provider could not load the selected key. " +
                "Check device/store access and permissions.");
        }
        catch (Exception error) when (error is System.IO.IOException
            or InvalidOperationException or NotSupportedException or ArgumentException or FormatException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Unavailable,
                "The configured application-key provider is unavailable. No alternate application key was selected.");
        }
    }

    public async Task ValidateAsync(ApplicationConfiguration configuration, CancellationToken ct)
    {
        // The connection owner must establish exclusive manager ownership BEFORE
        // this validation or any cleanup. A misconfigured factory must never
        // cause us to dispose a borrowed tools/secondary/listener manager.
        ct.ThrowIfCancellationRequested();
        CertificateIdentifier identifier = Source.CreateIdentifier(Certificate);
        ICertificatePasswordProvider passwords = Source.ResolvePasswordSource(Certificate.PasswordSourceId);
        bool configured = false;
        foreach (CertificateIdentifier application in configuration.SecurityConfiguration.ApplicationCertificates)
        {
            if (string.Equals(application.StoreType, identifier.StoreType, StringComparison.Ordinal) &&
                string.Equals(application.StorePath, identifier.StorePath, StringComparison.Ordinal) &&
                string.Equals(application.Thumbprint, identifier.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(application.SubjectName, identifier.SubjectName, StringComparison.Ordinal))
            {
                configured = true;
                break;
            }
        }
        if (!configured)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Incompatible,
                "The application configuration did not install the selected application-instance certificate.");
        }
        var provider = new ReferencedCertificateProvider(
            configuration.CertificateManager.CertificateProvider, identifier, TimeProvider.System);
        using Certificate? resolved = await provider.GetPrivateKeyCertificateAsync(
            identifier, passwords, ct: ct).ConfigureAwait(false);
    }

    public override string ToString()
    {
        return $"{DisplayName} — ApplicationInstanceKey ({Source.DisplayName})";
    }

    private readonly Func<CertificateIdentifier, ICertificatePasswordProvider,
        CancellationToken, Task<ApplicationConfiguration>> m_factory;
}

internal sealed record ConfiguredReverseTlsSource(
    string Id,
    string DisplayName,
    Func<CancellationToken, Task<ApplicationConfiguration>> CreateConfigurationAsync)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

internal sealed class ConnectionConfigurationCatalog
{
    public ConnectionConfigurationCatalog(
        ArrayOf<ConfiguredApplicationIdentity> applicationIdentities = default,
        ArrayOf<ConfiguredReverseTlsSource> reverseTlsSources = default)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfiguredApplicationIdentity identity in applicationIdentities)
        {
            if (!names.Add(identity.Id))
            {
                throw new ArgumentException("Application identity names must be unique.");
            }
        }
        names.Clear();
        foreach (ConfiguredReverseTlsSource source in reverseTlsSources)
        {
            ConnectionReference.Validate(source.Id);
            ArgumentNullException.ThrowIfNull(source.CreateConfigurationAsync);
            if (!names.Add(source.Id))
            {
                throw new ArgumentException("Listener TLS configuration names must be unique.");
            }
        }
        ApplicationIdentities = applicationIdentities;
        ReverseTlsSources = reverseTlsSources;
    }

    public ArrayOf<ConfiguredApplicationIdentity> ApplicationIdentities { get; }

    public ArrayOf<ConfiguredReverseTlsSource> ReverseTlsSources { get; }

    public ConfiguredApplicationIdentity ResolveApplication(string id)
    {
        foreach (ConfiguredApplicationIdentity identity in ApplicationIdentities)
        {
            if (string.Equals(identity.Id, id, StringComparison.Ordinal))
            {
                return identity;
            }
        }
        throw new ConnectionIdentityException(
            ConnectionIdentityFailure.RequiresConfiguration,
            "The application-instance key configuration is absent. " +
            "Configure the provider/device and PIN source externally.");
    }

    public ConfiguredReverseTlsSource ResolveTls(string id)
    {
        foreach (ConfiguredReverseTlsSource source in ReverseTlsSources)
        {
            if (string.Equals(source.Id, id, StringComparison.Ordinal))
            {
                return source;
            }
        }
        throw new InvalidOperationException(
            "The selected listener TLS configuration is absent. Configure its certificate and validator externally.");
    }

    public bool TryClaimManager(ApplicationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ICertificateManager? manager = configuration.CertificateManager;
        if (manager is null)
        {
            return false;
        }
        lock (m_gate)
        {
            if (m_claimedManagers.TryGetValue(manager, out _))
            {
                return false;
            }
            m_claimedManagers.Add(manager, new ManagerOwner());
            return true;
        }
    }

    private sealed class ManagerOwner
    {
    }

    private readonly Lock m_gate = new();
    private readonly ConditionalWeakTable<ICertificateManager, ManagerOwner> m_claimedManagers = new();
}

internal static class ConnectionConfigurationLifetime
{
    public static async ValueTask DisposeAsync(ApplicationConfiguration? configuration)
    {
        if (configuration?.CertificateManager is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync().ConfigureAwait(false);
        }
        else if (configuration?.CertificateManager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
