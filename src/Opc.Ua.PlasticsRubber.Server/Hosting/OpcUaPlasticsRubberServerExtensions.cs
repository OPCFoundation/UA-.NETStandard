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
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosts common plastics/rubber capabilities standalone or in an existing Machinery owner.
    /// </summary>
    public static class OpcUaPlasticsRubberServerExtensions
    {
        /// <summary>
        /// Creates one Machinery owner and adds the common model to it.
        /// </summary>
        public static IOpcUaServerBuilder AddPlasticsRubber(
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
                options.InstanceNamespaceUri = "urn:opcua-netstandard:plastics-rubber:instances";
                configure?.Invoke(options);
            }).AddPlasticsRubberToMachinery();
        }

        /// <summary>
        /// Adds common types without registering a second DI or Machinery manager.
        /// </summary>
        public static IOpcUaServerBuilder AddPlasticsRubberToMachinery(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (!builder.Services.Any(service => service.ServiceType == typeof(MachineryNodeManagerFactory)))
            {
                throw new InvalidOperationException("Register AddMachinery before AddPlasticsRubberToMachinery.");
            }
            return builder.AddMachineryModel<PlasticsRubberModelProvider>();
        }

        /// <summary>
        /// Configures common capabilities on the stock owner using an explicitly registered access policy.
        /// </summary>
        public static IOpcUaServerBuilder ConfigurePlasticsRubber(
            this IOpcUaServerBuilder builder,
            Func<PlasticsRubberRuntime, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigurePlasticsRubberFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Configures common capabilities on the selected existing DI/Machinery owner.
        /// </summary>
        /// <typeparam name="TNodeManager">The existing address-space owner type.</typeparam>
        public static IOpcUaServerBuilder ConfigurePlasticsRubberFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<PlasticsRubberRuntime, CancellationToken, ValueTask> configure)
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
            return builder.ConfigureMachineryFor<TNodeManager>((context, ct) => configure(
                new PlasticsRubberRuntime(
                    context.Manager,
                    context.GetRequiredService<IPlasticsRubberAccessPolicy>()),
                ct));
        }
    }
}
