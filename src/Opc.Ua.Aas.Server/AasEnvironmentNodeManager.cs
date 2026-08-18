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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// Stable AAS metamodel NodeManager that publishes environments as runtime NodeSets.
    /// </summary>
    public sealed class AasEnvironmentNodeManager : AsyncCustomNodeManager, IConformanceContributor
    {
        /// <summary>
        /// Initializes a NodeManager.
        /// </summary>
        public AasEnvironmentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AasServerOptions options,
            IAasEnvironmentProvider environmentProvider,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            IAasEnvironmentProjectionHost projectionHost)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<AasEnvironmentNodeManager>(),
                (options ?? throw new ArgumentNullException(nameof(options))).ControlNamespaceUri)
        {
            m_environmentProvider = environmentProvider ??
                throw new ArgumentNullException(nameof(environmentProvider));
            m_valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            m_projectionHost = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The metamodel half always projects shells, submodels and concept
        /// descriptions with the value fidelity of clause 6.3.1, so those four
        /// units are unconditional. Invoke is claimed only when an operation
        /// handler is actually wired up, because a Server that cannot execute
        /// an Operation does not satisfy clause 6.2.5.
        /// </remarks>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new List<QualifiedName>
                {
                    new("AAS-Metamodel"),
                    new("AAS-SubmodelElements"),
                    new("AAS-ValueFidelity"),
                    new("AAS-InstanceMaterialization"),
                    new("AAS-LosslessRoundTrip")
                };
                if (m_operationHandler is not DefaultAasOperationHandler)
                {
                    units.Add(new QualifiedName("AAS-OperationInvoke"));
                }
                return new ArrayOf<QualifiedName>(units.ToArray());
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Clause 10 defines conformance units but assigns no server profile
        /// URIs, and the IDTA identifier of Annex G applies only to a Server
        /// that also implements the HTTP binding, which this one does not.
        /// </remarks>
        public ArrayOf<string> ServerProfiles => [];

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            await foreach (AasEnvironment environment in m_environmentProvider
                .GetEnvironmentsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (m_valueProvider is DocumentAasValueProvider documentProvider)
                {
                    documentProvider.AddEnvironment(environment);
                }

                AasEnvironmentProjectionHandle handle = await m_projectionHost
                    .AddAsync(environment, m_valueProvider, m_operationHandler, cancellationToken)
                    .ConfigureAwait(false);
                m_handles.Add(handle);
            }
        }

        private readonly IAasEnvironmentProvider m_environmentProvider;
        private readonly IAasValueProvider m_valueProvider;
        private readonly IAasOperationHandler m_operationHandler;
        private readonly IAasEnvironmentProjectionHost m_projectionHost;
        private readonly List<AasEnvironmentProjectionHandle> m_handles = [];
    }
}
