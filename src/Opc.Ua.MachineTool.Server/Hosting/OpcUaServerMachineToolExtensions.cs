// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosts MachineTool and wireless peripherals on one compatible Machinery owner.
    /// </summary>
    public static class OpcUaServerMachineToolExtensions
    {
        /// <summary>
        /// Registers one standalone owner and the explicitly selected model closure.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Selects the models and standalone instance namespace.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder AddMachineTool(
            this IOpcUaServerBuilder builder,
            Action<MachineToolServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new MachineToolServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.AddMachinery(owner =>
            {
                owner.Parts = options.RequiredMachineryParts;
                owner.InstanceNamespaceUri = options.InstanceNamespaceUri;
            });
            return builder.AddMachineToolToMachinery(selected =>
            {
                selected.Parts = options.Parts;
                selected.InstanceNamespaceUri = options.InstanceNamespaceUri;
            });
        }

        /// <summary>
        /// Adds the selected model closure to an existing Machinery owner without registering a second manager.
        /// </summary>
        /// <param name="builder">The server builder with an existing Machinery model-provider pipeline.</param>
        /// <param name="configure">Selects the independently hosted models.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder AddMachineToolToMachinery(
            this IOpcUaServerBuilder builder,
            Action<MachineToolServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool registered = false;
            foreach (ServiceDescriptor service in builder.Services)
            {
                registered |= service.ServiceType == typeof(IMachineryModelProvider);
            }
            if (!registered)
            {
                throw new InvalidOperationException("Register the Machinery owner before AddMachineToolToMachinery.");
            }
            var options = new MachineToolServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.Services.AddOptions<MachineToolServerOptions>().Configure(selected =>
            {
                selected.Parts = options.Parts;
                selected.InstanceNamespaceUri = options.InstanceNamespaceUri;
            });
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.AddFileDirectoryBinder();
            return builder.AddMachineryModel<MachineToolModelProvider>();
        }

        /// <summary>
        /// Configures instances on the standalone Machinery owner with awaited setup.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Creates instances using the shared build context.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureMachineTool(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureMachineToolFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures instances on a selected existing DI/Machinery owner.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact namespace-owner type.</typeparam>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Creates instances using the owner's shared build context.</param>
        /// <returns>The server builder.</returns>
        public static IOpcUaServerBuilder ConfigureMachineToolFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
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
            return builder.ConfigureMachineryFor<TNodeManager>(configure);
        }
    }
}
