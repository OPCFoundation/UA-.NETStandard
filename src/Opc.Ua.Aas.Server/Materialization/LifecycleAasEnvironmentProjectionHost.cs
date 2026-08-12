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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;
using AasV2Environment = Opc.Ua.Aas.V2.AasEnvironment;
using AasV2EnvironmentMaterializer = Opc.Ua.Aas.V2.AasEnvironmentMaterializer;

namespace Opc.Ua.Aas.Server.Materialization
{
    /// <summary>
    /// Projects AAS environments with runtime NodeSet NodeManagers.
    /// </summary>
    public sealed class LifecycleAasEnvironmentProjectionHost : IAasEnvironmentProjectionHost
    {
        /// <summary>
        /// Initializes a lifecycle projection host.
        /// </summary>
        public LifecycleAasEnvironmentProjectionHost(INodeManagerLifecycle lifecycle)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> AddAsync(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .AddRuntimeNodeSetAsync(options, callerContext: null, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> AddAsync(
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .AddRuntimeNodeSetAsync(options, callerContext: null, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .ShadowReloadRuntimeNodeSetAsync(Unwrap(current), options, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .ShadowReloadRuntimeNodeSetAsync(Unwrap(current), options, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .ImmediateReloadRuntimeNodeSetAsync(Unwrap(current), options, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public async ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            RuntimeNodeSetOptions options = CreateOptions(environment, valueProvider, operationHandler);
            NodeManagerRegistration registration = await m_lifecycle
                .ImmediateReloadRuntimeNodeSetAsync(Unwrap(current), options, cancellationToken)
                .ConfigureAwait(false);
            return new AasEnvironmentProjectionHandle(new NodeManagerProjectionRegistration(registration));
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            AasEnvironmentProjectionHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (handle is null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            return m_lifecycle.RemoveAsync(Unwrap(handle), callerContext: null, cancellationToken);
        }

        /// <summary>
        /// Recovers the lifecycle registration this host put into a handle.
        /// </summary>
        /// <remarks>
        /// The handle is deliberately host-agnostic, so a handle produced by a
        /// different host carries a registration this one cannot act on. That
        /// is a programming error rather than a runtime condition, so it is
        /// named as one instead of being silently ignored.
        /// </remarks>
        private static NodeManagerRegistration Unwrap(AasEnvironmentProjectionHandle handle)
        {
            if (handle.Registration is not NodeManagerProjectionRegistration wrapper)
            {
                throw new ArgumentException(
                    "The projection handle was not produced by this host.", nameof(handle));
            }

            return wrapper.Registration;
        }

        private static RuntimeNodeSetOptions CreateOptions(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            if (valueProvider is null)
            {
                throw new ArgumentNullException(nameof(valueProvider));
            }
            if (operationHandler is null)
            {
                throw new ArgumentNullException(nameof(operationHandler));
            }

            AasMaterializationResult materialization = AasEnvironmentMaterializer.Materialize(environment);
            if (materialization.HasErrors)
            {
                throw new InvalidOperationException("The AAS environment could not be materialized.");
            }

            byte[] nodeSetXml = SerializeNodeSet(materialization.NodeSet);

            // ConfigureAsync returns the runtime as the IAsyncDisposable the
            // runtime NodeSet lifecycle takes ownership of, so the lifecycle
            // disposes it when the generation is retired. CA2000 cannot model
            // ownership transfer through that return value.
            // TODO: Remove this suppression when CA2000 recognizes it.
#pragma warning disable CA2000
            var runtime = new AasEnvironmentRuntime(environment, valueProvider, operationHandler);
#pragma warning restore CA2000
            return new RuntimeNodeSetOptions
            {
                Sources = new ArrayOf<RuntimeNodeSetSource>(new[]
                {
                    RuntimeNodeSetSource.FromStream(
                        "AAS Environment",
                        _ => new ValueTask<Stream>(new MemoryStream(nodeSetXml, writable: false)),
                        new ArrayOf<string>(OwnedModelUris(materialization.NodeSet)))
                }),
                DefaultNamespaceUri = Opc.Ua.Aas.V3.Namespaces.AasV3,
                AllowLifecycleFromRequestCallback = true,
                ConfigureAsync = runtime.ConfigureAsync
            };
        }

        private static RuntimeNodeSetOptions CreateOptions(
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            if (valueProvider is null)
            {
                throw new ArgumentNullException(nameof(valueProvider));
            }
            if (operationHandler is null)
            {
                throw new ArgumentNullException(nameof(operationHandler));
            }

            AasMaterializationResult materialization = AasV2EnvironmentMaterializer.Materialize(environment);
            if (materialization.HasErrors)
            {
                throw new InvalidOperationException("The AAS V2 environment could not be materialized.");
            }

            byte[] nodeSetXml = SerializeNodeSet(materialization.NodeSet);

#pragma warning disable CA2000
            var runtime = new Opc.Ua.Aas.Server.V2.AasV2EnvironmentRuntime(
                environment,
                valueProvider,
                operationHandler);
#pragma warning restore CA2000
            return new RuntimeNodeSetOptions
            {
                Sources = new ArrayOf<RuntimeNodeSetSource>(new[]
                {
                    RuntimeNodeSetSource.FromStream(
                        "AAS V2 Environment",
                        _ => new ValueTask<Stream>(new MemoryStream(nodeSetXml, writable: false)),
                        new ArrayOf<string>(OwnedModelUris(materialization.NodeSet, Opc.Ua.Aas.V2.Namespaces.AasV2)))
                }),
                DefaultNamespaceUri = Opc.Ua.Aas.V2.Namespaces.AasV2,
                AllowLifecycleFromRequestCallback = true,
                ConfigureAsync = runtime.ConfigureAsync
            };
        }

        private static byte[] SerializeNodeSet(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static string[] OwnedModelUris(UANodeSet nodeSet)
        {
            return OwnedModelUris(nodeSet, Opc.Ua.Aas.V3.Namespaces.AasV3);
        }

        private static string[] OwnedModelUris(UANodeSet nodeSet, string fallbackNamespaceUri)
        {
            if (nodeSet.Models is { Length: > 0 })
            {
                var result = new List<string>();
                foreach (ModelTableEntry model in nodeSet.Models)
                {
                    if (!string.IsNullOrEmpty(model.ModelUri))
                    {
                        result.Add(model.ModelUri);
                    }
                }
                if (result.Count > 0)
                {
                    return result.ToArray();
                }
            }
            return new[] { fallbackNamespaceUri };
        }

        private readonly INodeManagerLifecycle m_lifecycle;

        /// <summary>
        /// Carries the lifecycle registration owned by this host through the
        /// host-agnostic <see cref="AasEnvironmentProjectionHandle"/>.
        /// </summary>
        private sealed class NodeManagerProjectionRegistration : IAasProjectionRegistration
        {
            public NodeManagerProjectionRegistration(NodeManagerRegistration registration)
            {
                Registration = registration;
            }

            public Guid Id => Registration.Id;

            public NodeManagerRegistration Registration { get; }
        }
    }
}
