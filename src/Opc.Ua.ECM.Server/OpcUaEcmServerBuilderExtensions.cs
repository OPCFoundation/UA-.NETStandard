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
