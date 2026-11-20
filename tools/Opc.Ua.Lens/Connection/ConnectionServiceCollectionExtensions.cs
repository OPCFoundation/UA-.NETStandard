/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using UaLens.Diagnostics;

namespace UaLens.Connection;

internal static class ConnectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers one primary workspace. Hosts supply ITelemetryContext and may
    /// replace the credential provider/backend before calling this method.
    /// The host must be disposed asynchronously.
    /// </summary>
    public static IServiceCollection AddUaLensConnection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(provider => ConnectionIdentityConfiguration.CreateDefault(
            provider.GetRequiredService<ITelemetryContext>()));
        services.TryAddSingleton<ConnectionConfigurationCatalog>();
        services.TryAddSingleton(provider => new ConnectionTransportCatalog(
            provider.GetService<ITransportBindingRegistry>()));
        services.TryAddSingleton<IReverseConnectionRuntimeFactory>(provider =>
            new StackReverseConnectionRuntimeFactory(
                provider.GetRequiredService<ITelemetryContext>(),
                provider.GetRequiredService<ConnectionTransportCatalog>(),
                provider.GetRequiredService<ConnectionConfigurationCatalog>(),
                provider.GetService<IReverseConnectConfigurationProvider>()));
        services.TryAddSingleton<ReverseConnectionService>();
        services.TryAddSingleton<IConnectionBackend>(provider => new StackConnectionBackend(
            provider.GetRequiredService<ITelemetryContext>(),
            ct => AppConfig.BuildAsync(provider.GetRequiredService<ITelemetryContext>(), ct),
            provider.GetRequiredService<ConnectionTransportCatalog>(),
            provider.GetRequiredService<ConnectionConfigurationCatalog>(),
            provider.GetRequiredService<ReverseConnectionService>()));
        services.TryAddSingleton<IConnectionCredentialProvider>(provider =>
            new ProfileCredentialProvider(
                provider.GetService<ISecretRegistry>(),
                provider.GetRequiredService<ConnectionIdentityConfiguration>()));
        services.TryAddSingleton(provider => new ConnectionService(
            provider.GetRequiredService<ITelemetryContext>(),
            provider.GetService<PublishLogObserver>(),
            provider.GetRequiredService<IConnectionBackend>(),
            provider.GetRequiredService<IConnectionCredentialProvider>(),
            provider.GetRequiredService<ConnectionIdentityConfiguration>()));
        services.TryAddSingleton<IConnectionWorkspace>(provider => provider.GetRequiredService<ConnectionService>());
        return services;
    }
}
