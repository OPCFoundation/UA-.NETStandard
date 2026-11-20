// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.PADIM.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Standalone and composed hosting of the OPC 30081 model and runtime.
    /// </summary>
    public static class OpcUaPadimServerBuilderExtensions
    {
        /// <summary>
        /// Registers a standalone DI owner with PA-DIM and IRDI, without Machinery dependencies.
        /// </summary>
        public static IOpcUaServerBuilder AddPadim(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddOpcUaDi().AddDiModel<PadimModelProvider>();
        }

        /// <summary>
        /// Adds PA-DIM to an existing DI owner instead of registering a second DeviceSet.
        /// </summary>
        public static IOpcUaServerBuilder AddPadimModel(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddDiModel<PadimModelProvider>();
        }

        /// <summary>
        /// Configures standalone PA-DIM devices after the model and namespace table are initialized.
        /// </summary>
        public static IOpcUaServerBuilder ConfigurePadim(
            this IOpcUaServerBuilder builder,
            Func<IDiPostSetupContext, ValueTask> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureDevicesFor<DiNodeManager>(configure);
        }
    }
}
