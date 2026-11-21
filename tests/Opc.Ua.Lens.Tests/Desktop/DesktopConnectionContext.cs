// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop;

/// <summary>
/// Controlled transport/configuration boundary. No method creates a real
/// session, listener, certificate store or resource monitor.
/// </summary>
internal sealed class DesktopConnectionContext : IAsyncDisposable
{
    public DesktopConnectionContext(
        ConnectionConfigurationCatalog? configurations = null,
        IWorkspaceDispatcher? dispatcher = null)
    {
        Bindings.Setup(b => b.HasChannelFactory(It.IsAny<string>()))
            .Returns((string scheme) => scheme is "opc.tcp" or "ws" or "wss" or "opc.wss");
        Bindings.Setup(b => b.HasListenerFactory(It.IsAny<string>()))
            .Returns((string scheme) => scheme is "opc.tcp" or "wss" or "opc.wss");
        Runtime.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Runtime.Setup(r => r.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Runtime.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        RuntimeFactory.Setup(f => f.Create(It.IsAny<ReverseConnectionProfile>())).Returns(Runtime.Object);
        Reverse = new ReverseConnectionService(RuntimeFactory.Object);
        Backend.SetupGet(b => b.Transports).Returns(new ConnectionTransportCatalog(Bindings.Object));
        Backend.SetupGet(b => b.Configurations).Returns(configurations ?? new ConnectionConfigurationCatalog());
        Backend.SetupGet(b => b.ReverseConnections).Returns(Reverse);
        Backend.Setup(b => b.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => CreateConfigurationAsync(token));
        Backend.Setup(b => b.CreateConfigurationAsync(It.IsAny<ConnectionProfile?>(), It.IsAny<CancellationToken>()))
            .Returns((ConnectionProfile? _, CancellationToken token) => CreateConfigurationAsync(token));
        Backend.Setup(b => b.DiscoverAsync(It.IsAny<ApplicationConfiguration>(),
            It.IsAny<ConnectionSetupSelection>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationConfiguration _, ConnectionSetupSelection setup, CancellationToken token) =>
            {
                Discoveries.Add(setup);
                return DiscoverAsync(setup, token);
            });
        Backend.Setup(b => b.DiscoverAsync(It.IsAny<ApplicationConfiguration>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationConfiguration _, string url, CancellationToken token) =>
            {
                var setup = new ConnectionSetupSelection(url);
                Discoveries.Add(setup);
                return DiscoverAsync(setup, token);
            });
        Connection = new ConnectionService(Telemetry, null, Backend.Object, new ProfileCredentialProvider());
        Browser = new BrowserViewModel(Telemetry, Connection, dispatcher);
    }

    public AppTelemetryContext Telemetry { get; } = new(new LogRingBuffer(64));
    public Mock<IConfiguredConnectionBackend> Backend { get; } = new(MockBehavior.Strict);
    public Mock<ITransportBindingRegistry> Bindings { get; } = new(MockBehavior.Strict);
    public Mock<IReverseConnectionRuntime> Runtime { get; } = new(MockBehavior.Strict);
    public Mock<IReverseConnectionRuntimeFactory> RuntimeFactory { get; } = new(MockBehavior.Strict);
    public ReverseConnectionService Reverse { get; }
    public ConnectionService Connection { get; }
    public BrowserViewModel Browser { get; }
    public List<ConnectionSetupSelection> Discoveries { get; } = [];
    public int ConfigurationsCreated { get; private set; }
    public int ConfigurationsDisposed { get; private set; }
    public Func<ConnectionSetupSelection, CancellationToken, Task<ArrayOf<EndpointDescription>>> DiscoverAsync { get; set; } = (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(ArrayOf<EndpointDescription>.Empty);
    };

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync().ConfigureAwait(false);
        await Reverse.DisposeAsync().ConfigureAwait(false);
    }

    private Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ConfigurationsCreated++;
        var certificates = new Mock<ICertificateManager>();
        certificates.SetupProperty(c => c.AcceptError);
        certificates.As<IAsyncDisposable>().Setup(c => c.DisposeAsync()).Returns(() =>
        {
            ConfigurationsDisposed++;
            return ValueTask.CompletedTask;
        });
        return Task.FromResult(new ApplicationConfiguration(Telemetry)
        {
            ApplicationName = "UaLens controlled desktop",
            ApplicationUri = "urn:unit:test:desktop",
            CertificateManager = certificates.Object,
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = false,
                UseValidatedCertificates = false
            }
        });
    }
}
