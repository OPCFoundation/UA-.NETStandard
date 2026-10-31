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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision.AMCM;
using Opc.Ua.Vision.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the optional OPC 40100-2 module with the selected address-space owner.
    /// </summary>
    public static class OpcUaServerVisionMachineryExtensions
    {
        /// <summary>
        /// Adds Part 2 assets to a standalone AddVision registration.
        /// One node manager owns the shared DI/Machinery namespaces and all system instances.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static IOpcUaServerBuilder AddMachineVision(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool visionRegistered = false;
            bool ownsDi = false;
            foreach (ServiceDescriptor service in builder.Services)
            {
                visionRegistered |= service.ServiceType == typeof(IVisionModelProvider);
                if (service.ServiceType == typeof(DiAddressSpaceOwnership))
                {
                    if (service.ImplementationInstance is not DiAddressSpaceOwnership ownership ||
                        ownership.OwnerName != nameof(OpcUaServerVisionBuilderExtensions.AddVision))
                    {
                        builder.Services.ClaimDiAddressSpace(
                            nameof(OpcUaServerVisionBuilderExtensions.AddVision),
                            "Use AddVisionToMachinery to share that owner.");
                    }
                    ownsDi = true;
                }
            }
            if (!visionRegistered)
            {
                throw new InvalidOperationException("Register AddVision before AddMachineVision.");
            }
            if (!ownsDi)
            {
                builder.Services.ClaimDiAddressSpace(nameof(OpcUaServerVisionBuilderExtensions.AddVision));
            }
            builder.Services.Configure<VisionServerOptions>(
                options => options.MachineVisionParts |= MachineVisionParts.Assets);
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IVisionModelProvider, VisionMachineryStandaloneModelProvider>());
            return builder;
        }

        /// <summary>
        /// Composes Perception, Part 1, and optional Part 2 on the existing Machinery owner.
        /// Configure multiple uniquely named systems through ConfigureVisionFor.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddVisionToMachinery(
            this IOpcUaServerBuilder builder, Action<VisionServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.AddVisionComposition(configure);
            return builder.AddMachineryModel<VisionMachineryAssetModelProvider>();
        }
    }
}

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Loads the Part 2 dependency closure for a standalone Vision node manager.
    /// </summary>
    public sealed class VisionMachineryStandaloneModelProvider : IVisionDependencyModelProvider
    {
        /// <inheritdoc/>
        public int Order => int.MinValue + 1;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
        [
            Di.Namespaces.OpcUaDi,
            IA.Namespaces.IA,
            Machinery.Namespaces.Machinery,
            MachineVisionModels.AssetNamespaceUri
        ];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaMachineVisionAMCM(context);
            context.EncodeableFactory.Builder.AddOpcUaMachineVisionAMCM().Commit();
        }
    }

    /// <summary>
    /// Adds Part 2 to a Machinery provider pipeline without loading duplicate dependency models.
    /// </summary>
    public sealed class VisionMachineryAssetModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Uses the configured released-model selection.
        /// </summary>
        public VisionMachineryAssetModelProvider(IOptions<VisionServerOptions> options)
        {
            m_options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public int Order => 101;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => (m_options.MachineVisionParts & MachineVisionParts.Assets) != 0
            ? [MachineVisionModels.AssetNamespaceUri] : [];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if ((m_options.MachineVisionParts & MachineVisionParts.Assets) == 0)
            {
                return;
            }
            var machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines,
                context.NamespaceUris);
            bool loaded = false;
            foreach (NodeState node in nodes)
            {
                loaded |= node.NodeId == machines;
            }
            if (!loaded)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Part 2 assets require MachineryParts.BuildingBlocks on the owning manager.");
            }
            nodes.AddOpcUaMachineVisionAMCM(context);
            context.EncodeableFactory.Builder.AddOpcUaMachineVisionAMCM().Commit();
        }

        private readonly VisionServerOptions m_options;
    }
}
