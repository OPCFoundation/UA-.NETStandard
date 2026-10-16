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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosts cranes through the same Machinery/Robotics runtime in standalone or composed applications.
    /// </summary>
    public static class OpcUaCranesHoistsServerExtensions
    {
        /// <summary>
        /// Selects one Machinery owner with the crane dependency closure and mandatory building blocks.
        /// </summary>
        public static IOpcUaServerBuilder AddCranesHoists(
            this IOpcUaServerBuilder builder,
            Action<MachineryServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.AddMachinery(options =>
            {
                options.Parts = MachineryParts.BuildingBlocks;
                configure?.Invoke(options);
                if (!options.Parts.HasFlag(MachineryParts.BuildingBlocks))
                {
                    throw new ArgumentException("Cranes require MachineryParts.BuildingBlocks.", nameof(configure));
                }
            });
            return builder.AddCranesHoistsToMachinery();
        }

        /// <summary>
        /// Adds crane models to an already selected Machinery owner without registering another owner.
        /// </summary>
        public static IOpcUaServerBuilder AddCranesHoistsToMachinery(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddMachineryModel<CranesHoistsModelProvider>();
        }

        /// <summary>
        /// Adds crane and Machinery models to an existing Robotics owner using its application namespace.
        /// </summary>
        public static IOpcUaServerBuilder AddCranesHoistsToRobotics(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<MachineryServerOptions>()
                .Configure<IOptions<RoboticsServerOptions>>((machinery, robotics) =>
                {
                    machinery.Parts = MachineryParts.BuildingBlocks;
                    machinery.InstanceNamespaceUri = robotics.Value.InstanceNamespaceUri;
                });
            return builder.AddRoboticsModel<CranesHoistsModelProvider>();
        }

        /// <summary>
        /// Awaits crane construction on the standalone Machinery owner.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureCranesHoists(
            this IOpcUaServerBuilder builder,
            Func<CranesHoistsBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureCranesHoistsFor<MachineryNodeManager>(configure);
        }

        /// <summary>
        /// Awaits crane construction on the standalone owner using a one-argument callback.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureCranesHoists(
            this IOpcUaServerBuilder builder,
            Func<CranesHoistsBuildContext, ValueTask> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureCranesHoistsFor<MachineryNodeManager>((context, _) => configure(context));
        }

        /// <summary>
        /// Uses an existing DI-derived owner and the same construction/runtime path.
        /// </summary>
        /// <typeparam name="TManager">The exact existing owner type.</typeparam>
        public static IOpcUaServerBuilder ConfigureCranesHoistsFor<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager>(
            this IOpcUaServerBuilder builder,
            Func<CranesHoistsBuildContext, CancellationToken, ValueTask> configure)
            where TManager : DiNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureMachineryFor<TManager>(async (machinery, cancellationToken) =>
            {
                IRoboticsBuildContext robotics = machinery.Manager.CreateRoboticsBuildContext(
                    new RoboticsServerOptions
                    {
                        InstanceNamespaceUri = machinery.Context.NamespaceUris
                            .GetString(machinery.InstanceNamespaceIndex)!
                    },
                    cancellationToken);
                var context = new CranesHoistsBuildContext(
                    robotics, machinery, machinery.GetService<TimeProvider>());
                await configure(context, cancellationToken).ConfigureAwait(false);
                await robotics.SealAsync(cancellationToken).ConfigureAwait(false);
            });
        }
    }
}
