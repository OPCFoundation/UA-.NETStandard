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
using Opc.Ua;
using Opc.Ua.Di.Server;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosts native GMS on one existing Machinery/DI namespace owner with its exact canonical model closure.
    /// </summary>
    public static class OpcUaServerGmsExtensions
    {
        /// <summary>
        /// Registers a standalone Machinery owner and the GMS model without enabling any optional instance feature.
        /// </summary>
        public static IOpcUaServerBuilder AddGms(
            this IOpcUaServerBuilder builder, Action<GmsServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new GmsServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.AddMachinery(owner =>
            {
                owner.Parts = MachineryParts.BuildingBlocks | MachineryParts.Result;
                owner.InstanceNamespaceUri = options.InstanceNamespaceUri;
            });
            return builder.AddGmsToMachinery();
        }

        /// <summary>
        /// Adds the GMS and MachineTool/Result model closure to an existing owner, never another DI/Machinery manager.
        /// No result store or equipment provider is registered globally.
        /// </summary>
        public static IOpcUaServerBuilder AddGmsToMachinery(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool registered = false;
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                registered |= descriptor.ServiceType == typeof(IMachineryModelProvider);
            }
            if (!registered)
            {
                throw new InvalidOperationException(
                    "Register the compatible Machinery owner before AddGmsToMachinery.");
            }
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.PostConfigure<MachineryServerOptions>(static owner =>
            {
                const MachineryParts required = MachineryParts.BuildingBlocks | MachineryParts.Result;
                if ((owner.Parts & required) != required)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The GMS owner must enable Machinery building blocks and the mandatory result binding.");
                }
            });
            return builder.AddMachineryModel<GmsModelProvider>();
        }

        /// <summary>
        /// Configures native instances on the standalone shared owner with awaited startup and rollback.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureGms(
            this IOpcUaServerBuilder builder,
            Func<IMachineryBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureGmsFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures native instances on an explicitly selected compatible existing namespace owner.
        /// </summary>
        /// <typeparam name="TNodeManager">The exact existing DI/Machinery owner.</typeparam>
        public static IOpcUaServerBuilder ConfigureGmsFor<TNodeManager>(
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
