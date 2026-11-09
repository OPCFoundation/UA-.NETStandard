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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Programs;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Opt-in registration of managed OPC UA Part 10 Programs without starting workloads.
    /// </summary>
    public static class ProgramServerBuilderExtensions
    {
        /// <summary>
        /// Adds one per-server Program host with explicitly registered concrete types.
        /// </summary>
        public static IOpcUaServerBuilder AddPrograms(
            this IOpcUaServerBuilder builder,
            ArrayOf<ProgramDefinition> definitions,
            string instanceNamespaceUri = ProgramNodeManager.DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var factory = new ProgramNodeManagerFactory(
                definitions, instanceNamespaceUri, addNodesTypes, modelLoader);
            RegisterInfrastructure(builder);
            return builder.AddNodeManager((IAsyncNodeManagerFactory)factory);
        }

        /// <summary>
        /// Resolves Program providers and definitions from dependency injection once per server start.
        /// </summary>
        public static IOpcUaServerBuilder AddPrograms(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, ArrayOf<ProgramDefinition>> definitions,
            string instanceNamespaceUri = ProgramNodeManager.DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (definitions is null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }
            RegisterInfrastructure(builder);
            return builder.AddNodeManagers((services, _) =>
                [new ProgramNodeManagerFactory(
                    definitions(services), instanceNamespaceUri, addNodesTypes, modelLoader)]);
        }

        private static void RegisterInfrastructure(IOpcUaServerBuilder builder)
        {
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(ProgramRegistration))
                {
                    throw new InvalidOperationException(
                        "Register Programs once with the complete set of concrete type definitions.");
                }
            }
            builder.Services.AddSingleton(new ProgramRegistration());
        }

        private sealed class ProgramRegistration;
    }
}
