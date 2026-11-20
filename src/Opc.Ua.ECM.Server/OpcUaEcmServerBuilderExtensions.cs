/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent standalone ECM hosting with direct construction retained.
    /// </summary>
    public static class OpcUaEcmServerBuilderExtensions
    {
        /// <summary>
        /// Adds ECM to the selected DI owner without registering a second owner.
        /// Attach provider bindings to generated graphs in that owner's post-setup callback.
        /// </summary>
        public static IOpcUaServerBuilder AddEcmModel(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddDiModel<EcmModelProvider, DiNodeManager>(static context =>
            {
                EcmConformanceContributor.For(context.Manager);
                return default;
            });
        }

        /// <summary>
        /// Registers the standalone ECM owner. Cannot be combined with another DI owner.
        /// Providers registered by the application remain owned by their DI container.
        /// </summary>
        public static IOpcUaServerBuilder AddEcm(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.ClaimDiAddressSpace(
                nameof(AddEcm), "Load the ECM model into an existing DI owner for composed hosting.");
            builder.Services.AddSingleton(static services => new EcmNodeManagerFactory(
                services.GetService<IDiPostSetupRunner>(),
                services.GetServices<IDiModelProvider>().ToArray()));
            builder.Services.AddSingleton(static services => new OpcUaServerNodeManagerRegistration(
                services.GetRequiredService<EcmNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Configures ECM after its model is loaded, resolving providers through the shared DI context.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureEcm(
            this IOpcUaServerBuilder builder,
            Func<EcmNodeManager, IDiPostSetupContext, ValueTask> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureDevicesFor<EcmNodeManager>(context =>
                configure((EcmNodeManager)context.Manager, context));
        }
    }
}
