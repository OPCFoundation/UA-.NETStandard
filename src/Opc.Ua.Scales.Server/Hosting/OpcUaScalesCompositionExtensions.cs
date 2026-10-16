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
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes the complete Scales runtime into the existing Machinery/DI owner.
    /// </summary>
    public static class OpcUaScalesCompositionExtensions
    {
        /// <summary>
        /// Adds PackML and Scales to an existing AddMachinery registration with BuildingBlocks enabled.
        /// </summary>
        public static IOpcUaServerBuilder AddScalesToMachinery(
            this IOpcUaServerBuilder builder,
            Action<ScalesServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (!builder.Services.Any(service => service.ServiceType == typeof(MachineryNodeManagerFactory)))
            {
                throw new InvalidOperationException("Register AddMachinery before AddScalesToMachinery.");
            }
            var options = new ScalesServerOptions();
            configure?.Invoke(options);
            options.Validate();
            builder.Services.AddOptions<ScalesServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            return builder.AddMachineryModel<ScalesMachineryModelProvider>();
        }

        /// <summary>
        /// Configures the shared Scales runtime on the exact selected DI owner.
        /// </summary>
        /// <remarks>
        /// Machinery configurators share its ordered configuration pipeline and finish before sealing.
        /// Custom Machinery-compatible DI owners load the models and dispose their Machinery resources.
        /// Existing ConfigureScales delegates and standalone managers remain supported unchanged.
        /// </remarks>
        /// <typeparam name="TNodeManager">The exact DI/Machinery owner type.</typeparam>
        public static IOpcUaServerBuilder ConfigureScalesFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IScalesBuildContext, CancellationToken, ValueTask> configure)
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
            builder.Services.AddOptions<ScalesServerOptions>();
            if (typeof(TNodeManager) == typeof(ScalesNodeManager))
            {
                return builder.ConfigureDevicesFor<TNodeManager>(context => configure(
                    new ScalesBuildContext(
                        context.Manager.CreateScalesRuntime(),
                        context.GetRequiredService<IServiceProvider>(),
                        context.CancellationToken),
                    context.CancellationToken));
            }
            return builder.ConfigureMachineryFor<TNodeManager>((machinery, ct) => configure(
                new ScalesBuildContext(
                    machinery.Manager.CreateScalesRuntime(
                        machinery.GetRequiredService<IOptions<ScalesServerOptions>>().Value),
                    machinery.GetRequiredService<IServiceProvider>(),
                    ct),
                ct));
        }

        /// <summary>
        /// Carries configurator services and cancellation separately from the shared runtime.
        /// </summary>
        private sealed class ScalesBuildContext : IScalesBuildContext
        {
            /// <summary>
            /// Creates the context for the current configurator.
            /// </summary>
            public ScalesBuildContext(
                ScalesRuntime runtime,
                IServiceProvider services,
                CancellationToken cancellationToken)
            {
                Runtime = runtime;
                m_services = services;
                CancellationToken = cancellationToken;
            }

            /// <inheritdoc/>
            public ScalesRuntime Runtime { get; }

            /// <inheritdoc/>
            public DiNodeManager Manager => Runtime.Manager;

            /// <inheritdoc/>
            public CancellationToken CancellationToken { get; }

            /// <inheritdoc/>
            public T GetRequiredService<T>() where T : notnull
            {
                return m_services.GetRequiredService<T>();
            }

            /// <summary>The hosting container.</summary>
            private readonly IServiceProvider m_services;
        }
    }
}
