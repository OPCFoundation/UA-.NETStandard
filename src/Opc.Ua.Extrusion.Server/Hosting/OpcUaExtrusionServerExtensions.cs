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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers standalone or composed extrusion hosting without a second DI/Machinery owner.
    /// </summary>
    public static class OpcUaExtrusionServerExtensions
    {
        /// <summary>
        /// Registers a standalone extrusion endpoint using the stock Machinery owner.
        /// </summary>
        public static IOpcUaServerBuilder AddExtrusion(
            this IOpcUaServerBuilder builder,
            Action<MachineryServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddMachinery(options =>
            {
                options.Parts = MachineryParts.BuildingBlocks;
                configure?.Invoke(options);
            }).AddExtrusionToMachinery();
        }

        /// <summary>
        /// Composes all twelve standard extrusion models into the already registered Machinery owner.
        /// </summary>
        public static IOpcUaServerBuilder AddExtrusionToMachinery(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (!builder.Services.Any(service => service.ServiceType == typeof(MachineryNodeManagerFactory)))
            {
                throw new InvalidOperationException("Register AddMachinery before AddExtrusionToMachinery.");
            }
            return builder.AddPlasticsRubberToMachinery().AddMachineryModel<ExtrusionModelProvider>();
        }

        /// <summary>
        /// Adds an ordered asynchronous configurator for the stock owner.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureExtrusion(
            this IOpcUaServerBuilder builder,
            Func<IExtrusionBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureExtrusionFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures an exact custom DI/Machinery owner that loaded the extrusion model provider.
        /// </summary>
        /// <typeparam name="TNodeManager">The existing address-space owner.</typeparam>
        public static IOpcUaServerBuilder ConfigureExtrusionFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IExtrusionBuildContext, CancellationToken, ValueTask> configure)
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
            return builder.ConfigureMachineryFor<TNodeManager>((context, ct) =>
                configure(new BuildContext(ExtrusionRuntime.Create(context.Manager),
                    context.GetRequiredService<IServiceProvider>()), ct));
        }

        /// <summary>
        /// Carries only the current owner and the provider-resolution seam.
        /// </summary>
        private sealed class BuildContext(ExtrusionRuntime runtime, IServiceProvider services) : IExtrusionBuildContext
        {
            /// <inheritdoc/>
            public ExtrusionRuntime Runtime { get; } = runtime;

            /// <inheritdoc/>
            public T GetRequiredService<T>() where T : notnull
            {
                return services.GetRequiredService<T>();
            }
        }
    }
}

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// The injectable extrusion configuration seam on an existing Machinery owner.
    /// </summary>
    public interface IExtrusionBuildContext
    {
        /// <summary>
        /// Gets the owner's single extrusion runtime.
        /// </summary>
        ExtrusionRuntime Runtime { get; }

        /// <summary>
        /// Resolves an explicitly registered asynchronous hardware or persistence adapter.
        /// </summary>
        /// <typeparam name="T">The registered service type.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
