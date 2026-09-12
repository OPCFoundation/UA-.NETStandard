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
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using UaLens.Plugins.Alarms;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Continuity;
using UaLens.Plugins.Models;
using UaLens.Plugins.PubSub;
using UaLens.ViewModels;

namespace UaLens;

/// <summary>
/// Registers native-safe, replaceable document factories without starting network or sample workloads.
/// </summary>
internal static class UaLensShowcaseServiceCollectionExtensions
{
    public static IServiceCollection AddUaLensShowcases(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            return services;
        }
        services.AddSingleton(new RegistrationMarker());
        AddFactory(services, PluginKind.Alarms, static host => new AlarmsPlugin(host));
        AddFactory(services, PluginKind.Models, static host => new ModelInspectorPlugin(host));
        AddFactory(services, PluginKind.Continuity, static host => new ContinuityPlugin(host));

        services.TryAddSingleton<IPubSubRuntimeFactory>(provider => new PubSubRuntimeFactory(
            provider.GetRequiredService<ITelemetryContext>(),
            provider.GetService<TimeProvider>(),
            [.. provider.GetServices<IPubSubTransportProvider>()],
            [.. provider.GetServices<IPubSubKeyProviderResolver>()],
            [.. provider.GetServices<IPubSubAdapterProvider>()]));
        services.AddUaLensPluginFactory<IPubSubRuntimeFactory>(
            PluginKind.PubSub, static (factory, host) => new PubSubPlugin(host, factory));

        services.TryAddSingleton<ICompanionPackageReader, CompanionPackageReader>();
        services.TryAddSingleton(provider => new CompanionPluginFactory(
            timeProvider: provider.GetService<TimeProvider>(),
            packages: provider.GetRequiredService<ICompanionPackageReader>()));
        services.AddUaLensPluginFactory<CompanionPluginFactory>(
            PluginKind.Companions, static (factory, host) => factory.Create(host));
        return services;
    }

    private static void AddFactory<TPlugin>(
        IServiceCollection services,
        PluginKind kind,
        Func<PluginHost, TPlugin> create)
        where TPlugin : class, IPlugin
    {
        services.TryAddSingleton<Func<PluginHost, TPlugin>>(_ => create);
        services.AddUaLensPluginFactory<Func<PluginHost, TPlugin>>(
            kind, static (factory, host) => factory(host));
    }

    private sealed class RegistrationMarker;
}
