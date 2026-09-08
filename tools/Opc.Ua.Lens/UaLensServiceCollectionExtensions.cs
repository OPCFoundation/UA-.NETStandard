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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Telemetry;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens;

/// <summary>
/// Typed composition for the desktop modules. Explicit factories keep construction
/// compatible with NativeAOT; callers can replace real seams before registering defaults.
/// </summary>
internal static class UaLensServiceCollectionExtensions
{
    public static IServiceCollection AddUaLens(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ => new LogRingBuffer(capacity: 4096));
        services.TryAddSingleton(provider => new AppTelemetryContext(provider.GetRequiredService<LogRingBuffer>()));
        services.TryAddSingleton<ITelemetryContext>(provider => provider.GetRequiredService<AppTelemetryContext>());
        services.TryAddSingleton(_ => new PublishLogObserver());
        services.TryAddSingleton(_ => new AppearancePreferences());
        services.TryAddSingleton<IWorkspaceDispatcher>(_ => new AvaloniaWorkspaceDispatcher());
        services.AddUaLensConnection();
        services.AddUaLensShowcases();
        services.TryAddSingleton<ICapabilityProbe>(_ => new SessionCapabilityProbe());
        services.TryAddSingleton<ICapabilityService>(provider => new CapabilityService(
            provider.GetRequiredService<ConnectionService>(),
            provider.GetRequiredService<ICapabilityProbe>()));
        services.TryAddSingleton<IPluginFactory>(
            provider => new PluginFactory([.. provider.GetServices<PluginFactoryRegistration>()]));
        services.TryAddSingleton(
            provider => new PluginDocumentOperations(provider.GetRequiredService<ConnectionService>()));
        services.TryAddSingleton(provider => new DocumentWorkspace<IPlugin>(
            provider.GetRequiredService<AppTelemetryContext>().CreateLogger("Documents"),
            provider.GetRequiredService<IWorkspaceDispatcher>(),
            provider.GetRequiredService<PluginDocumentOperations>().SynchronizeConnectionAsync));
        services.TryAddSingleton(_ => new CommandRegistry());
        services.TryAddSingleton<Func<CancellationToken, Task<ResourceMonitorHost>>>(
            provider => cancellationToken => ResourceMonitorHost.StartAsync(
                provider.GetRequiredService<AppTelemetryContext>(), cancellationToken));
        services.TryAddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<AppTelemetryContext>(),
            provider.GetRequiredService<ConnectionService>(),
            provider.GetRequiredService<DocumentWorkspace<IPlugin>>(),
            provider.GetRequiredService<CommandRegistry>(),
            provider.GetRequiredService<PluginDocumentOperations>(),
            provider.GetRequiredService<IWorkspaceDispatcher>(),
            provider.GetRequiredService<Func<CancellationToken, Task<ResourceMonitorHost>>>(),
            provider.GetRequiredService<ICapabilityService>(),
            provider.GetRequiredService<IPluginFactory>()));
        services.TryAddSingleton(provider => provider.GetRequiredService<MainViewModel>().CreatePluginHost());
        return services;
    }

    /// <summary>
    /// Binds a registered, typed feature factory at composition time.
    /// The feature and its document never resolve services.
    /// Register the feature factory itself using an explicit construction delegate for NativeAOT.
    /// </summary>
    public static IServiceCollection AddUaLensPluginFactory<TFactory>(
        this IServiceCollection services,
        PluginKind kind,
        Func<TFactory, PluginHost, IPlugin> create)
        where TFactory : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);
        services.AddSingleton(provider =>
        {
            TFactory factory = provider.GetRequiredService<TFactory>();
            return new PluginFactoryRegistration(kind, host => create(factory, host));
        });
        return services;
    }
}
