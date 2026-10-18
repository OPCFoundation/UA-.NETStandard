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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Di.Server;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Standalone and composed drying-device hosting through a single existing Machinery owner.
    /// </summary>
    public static class GranulateDryingHostingExtensions
    {
        /// <summary>
        /// Creates the stock Machinery owner and adds drying support.
        /// Use AddGranulateDryingToMachinery when an owner is already registered.
        /// </summary>
        public static IOpcUaServerBuilder AddGranulateDrying(
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
            }).AddGranulateDryingToMachinery();
        }

        /// <summary>
        /// Adds GDD and its common-model dependency to an already registered Machinery owner.
        /// No second manager claims DI, Machinery, or an instance namespace.
        /// </summary>
        public static IOpcUaServerBuilder AddGranulateDryingToMachinery(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (!builder.Services.Any(service => service.ServiceType == typeof(MachineryNodeManagerFactory)))
            {
                throw new InvalidOperationException("Register AddMachinery before AddGranulateDryingToMachinery.");
            }
            builder.Services.TryAddSingleton<IFileTypeBinder, FileTypeBinder>();
            return builder.AddPlasticsRubberToMachinery().AddMachineryModel<GranulateDryingModelProvider>();
        }

        /// <summary>
        /// Configures drying devices during the stock Machinery owner's ordered startup pipeline.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureGranulateDrying(
            this IOpcUaServerBuilder builder,
            Func<IGranulateDryingBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureGranulateDryingFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures drying devices on an exact custom DI/Machinery owner.
        /// The owner must load GranulateDryingModelProvider and dispose its Machinery resources.
        /// </summary>
        /// <typeparam name="TNodeManager">The existing DI/Machinery owner type.</typeparam>
        public static IOpcUaServerBuilder ConfigureGranulateDryingFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IGranulateDryingBuildContext, CancellationToken, ValueTask> configure)
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
            builder.Services.TryAddSingleton<IFileTypeBinder, FileTypeBinder>();
            return builder.ConfigureMachineryFor<TNodeManager>((context, ct) =>
                configure(new BuildContext(context, ct), ct));
        }

        /// <summary>
        /// Carries provider resolution and the owner's shared runtime through one configurator.
        /// </summary>
        private sealed class BuildContext : IGranulateDryingBuildContext
        {
            /// <summary>
            /// Opens the shared runtime on the selected owner.
            /// </summary>
            internal BuildContext(IMachineryBuildContext machinery, CancellationToken cancellationToken)
            {
                m_machinery = machinery;
                CancellationToken = cancellationToken;
                Runtime = machinery.Manager.CreateGranulateDryingRuntime(
                    machinery.GetService<IFileTypeBinder>());
            }

            /// <inheritdoc/>
            public GranulateDryingRuntime Runtime { get; }

            /// <inheritdoc/>
            public DiNodeManager Manager => Runtime.Manager;

            /// <inheritdoc/>
            public CancellationToken CancellationToken { get; }

            /// <inheritdoc/>
            public T GetRequiredService<T>() where T : notnull => m_machinery.GetRequiredService<T>();

            /// <summary>
            /// The active Machinery configuration scope.
            /// </summary>
            private readonly IMachineryBuildContext m_machinery;
        }
    }
}

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// The services, cancellation, and runtime available while configuring drying devices.
    /// </summary>
    public interface IGranulateDryingBuildContext
    {
        /// <summary>
        /// Gets the owner's shared drying runtime.
        /// </summary>
        GranulateDryingRuntime Runtime { get; }

        /// <summary>
        /// Gets the single address-space owner.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets startup cancellation.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves an injected hardware provider, policy, or asset-management module.
        /// </summary>
        /// <typeparam name="T">The registered service type.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
