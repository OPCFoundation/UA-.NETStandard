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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC 10000-110 Asset Management Basics servers.
    /// </summary>
    /// <remarks>
    /// <code>
    /// services.AddOpcUa()
    ///     .AddServer&lt;StandardServer&gt;(ConfigureServer)
    ///     .AddOpcUaDi()                     // or AddMachinery, AddPumps, AddScales
    ///     .AddAssetManagement(o =&gt; o.UseFileSystemStores("amb-state"))
    ///     .ConfigureDevicesFor&lt;DiNodeManager&gt;(async ctx =&gt;
    ///     {
    ///         IDeviceBuilder&lt;DeviceState&gt; device = await ctx.CreateDeviceAsync(name);
    ///         device.WithIdentification(i =&gt; i.ProductInstanceUri = "urn:acme:sensor:4711");
    ///         await ctx.GetRequiredService&lt;IAssetManagement&gt;().RegisterAssetAsync(
    ///             device.Node,
    ///             asset =&gt; asset.WithConfigurableAssetId(),
    ///             ctx.CancellationToken);
    ///     });
    /// </code>
    /// </remarks>
    public static class OpcUaAmbServerBuilderExtensions
    {
        /// <summary>
        /// Registers the Asset Management Basics node manager and the shared
        /// <see cref="IAssetManagement"/> registry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The manager is a sidecar that claims no Device Integration
        /// ownership, so it is combined with the registration that does -
        /// <c>AddOpcUaDi</c>, or a companion specification such as Machinery,
        /// Pumps or Scales - in either order.
        /// </para>
        /// <para>
        /// The configuration clients write to assets is kept in memory unless
        /// <see cref="AmbServerOptions.UseFileSystemStores"/> is set or an
        /// <see cref="IAssetConfigurationStore"/> is registered.
        /// </para>
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the options.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The asset management is already registered.</exception>
        /// <exception cref="ArgumentException">The options are invalid.</exception>
        public static IOpcUaServerBuilder AddAssetManagement(
            this IOpcUaServerBuilder builder,
            Action<AmbServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(AssetManagement))
                {
                    throw new InvalidOperationException(
                        "AddAssetManagement has already been called; a server has one asset management.");
                }
            }

            var options = new AmbServerOptions();
            configure?.Invoke(options);
            options.Validate();

            builder.Services.TryAddSingleton<IAssetConfigurationStore>(services =>
                options.StateDirectory == null
                    ? new MemoryAssetConfigurationStore()
                    : new FileSystemAssetConfigurationStore(
                        options.StateDirectory,
                        services.GetService<ILoggerFactory>()?.CreateLogger<FileSystemAssetConfigurationStore>()));
            builder.Services.AddSingleton(services => new AssetManagement(
                options,
                services.GetRequiredService<IAssetConfigurationStore>()));
            builder.Services.AddSingleton<IAssetManagement>(
                services => services.GetRequiredService<AssetManagement>());

            return builder.AddNodeManager<AmbNodeManagerFactory>();
        }

        /// <summary>
        /// Registers a delegate that runs inside the Asset Management Basics
        /// node manager once its address space is loaded. Multiple
        /// registrations run in registration order, and an exception aborts
        /// the server startup.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the asset management.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureAssetManagement(
            this IOpcUaServerBuilder builder,
            Func<IAssetManagementSetupContext, ValueTask> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddSingleton(new AmbSetupConfigurator(configure));
            return builder;
        }

        /// <summary>
        /// Synchronous overload of
        /// <see cref="ConfigureAssetManagement(IOpcUaServerBuilder, Func{IAssetManagementSetupContext, ValueTask})"/>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the asset management.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureAssetManagement(
            this IOpcUaServerBuilder builder,
            Action<IAssetManagementSetupContext> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureAssetManagement(context =>
            {
                configure(context);
                return default;
            });
        }
    }
}
