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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di.Server;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosts LADS (OPC 30500-1) devices on a Machinery namespace owner.
    /// </summary>
    public static class OpcUaServerLadsExtensions
    {
        /// <summary>
        /// Registers a standalone Machinery owner, the asset management the LADS model
        /// depends on and the LADS model.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the LADS options.</param>
        /// <returns>The server builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">LADS is already registered.</exception>
        public static IOpcUaServerBuilder AddLads(
            this IOpcUaServerBuilder builder,
            Action<LadsServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new LadsServerOptions();
            configure?.Invoke(options);
            options.Validate();
            ThrowIfRegistered(builder);
            builder.AddMachinery(owner =>
            {
                owner.Parts = options.RequiredMachineryParts;
                owner.InstanceNamespaceUri = options.InstanceNamespaceUri;
            });
            return builder.AddLadsToMachinery(selected => Copy(options, selected));
        }

        /// <summary>
        /// Adds the LADS model to an existing Machinery owner without registering a
        /// second namespace owner. Registers the asset management when it is missing.
        /// </summary>
        /// <param name="builder">The server builder with an existing Machinery owner.</param>
        /// <param name="configure">Configures the LADS options.</param>
        /// <returns>The server builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The Machinery owner is missing or LADS is already registered.
        /// </exception>
        public static IOpcUaServerBuilder AddLadsToMachinery(
            this IOpcUaServerBuilder builder,
            Action<LadsServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool machinery = false;
            bool assetManagement = false;
            foreach (ServiceDescriptor service in builder.Services)
            {
                machinery |= service.ServiceType == typeof(IMachineryModelProvider);
                assetManagement |= service.ServiceType == typeof(AssetManagement);
            }
            if (!machinery)
            {
                throw new InvalidOperationException("Register the Machinery owner before AddLadsToMachinery.");
            }
            ThrowIfRegistered(builder);
            var options = new LadsServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.Services.AddOptions<LadsServerOptions>().Configure(selected => Copy(options, selected));
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<IFileTypeBinder, FileTypeBinder>();
            builder.Services.TryAddSingleton<ILadsDeviceProvider, InMemoryLadsDeviceProvider>();
            builder.Services.TryAddSingleton<ILadsFunctionProvider, InMemoryLadsFunctionProvider>();
            builder.Services.TryAddSingleton<ILadsProgramExecutor, InMemoryLadsProgramExecutor>();
            builder.Services.TryAddSingleton<ILadsDeviceRegistry>(_ => new LadsDeviceRegistry());
            if (!assetManagement)
            {
                builder.AddAssetManagement();
            }
            return builder.AddMachineryModel<LadsModelProvider>();
        }

        /// <summary>
        /// Configures LADS devices on the standalone Machinery owner.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Adds the devices.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureLads(
            this IOpcUaServerBuilder builder,
            Action<ILadsSetupContext> configure)
        {
            return builder.ConfigureLadsFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures LADS devices on the standalone Machinery owner with awaited setup.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Adds the devices.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureLads(
            this IOpcUaServerBuilder builder,
            Func<ILadsSetupContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureLadsFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures LADS devices on a selected existing DI/Machinery owner.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact namespace-owner type.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Adds the devices.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureLadsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Action<ILadsSetupContext> configure)
            where TNodeManager : DiNodeManager
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureLadsFor<TNodeManager>((context, _) =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Configures LADS devices on a selected existing DI/Machinery owner with awaited setup.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact namespace-owner type.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Adds the devices.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureLadsFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<ILadsSetupContext, CancellationToken, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureMachineryFor<TNodeManager>(
                (context, ct) => LadsSetup.ConfigureAsync(context, configure, ct));
        }

        /// <summary>
        /// Rejects a second LADS registration.
        /// </summary>
        private static void ThrowIfRegistered(IOpcUaServerBuilder builder)
        {
            foreach (ServiceDescriptor service in builder.Services)
            {
                if (service.ImplementationType == typeof(LadsModelProvider))
                {
                    throw new InvalidOperationException("LADS is already registered on this server.");
                }
            }
        }

        /// <summary>
        /// Copies the options.
        /// </summary>
        private static void Copy(LadsServerOptions source, LadsServerOptions target)
        {
            target.InstanceNamespaceUri = source.InstanceNamespaceUri;
            target.MaxResultFileBytes = source.MaxResultFileBytes;
            target.MaxRetainedResults = source.MaxRetainedResults;
        }
    }
}
