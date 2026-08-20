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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Aas.V2;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// AAS V2 metamodel NodeManager that publishes OPC 30270 environments as runtime NodeSets.
    /// </summary>
    public sealed class AasV2EnvironmentNodeManager : AsyncCustomNodeManager, IConformanceContributor
    {
        /// <summary>
        /// Initializes a NodeManager.
        /// </summary>
        public AasV2EnvironmentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AasServerOptions options,
            IAasV2EnvironmentProvider environmentProvider,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            IAasEnvironmentProjectionHost projectionHost)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<AasV2EnvironmentNodeManager>(),
                (options ?? throw new ArgumentNullException(nameof(options))).ControlNamespaceUri)
        {
            m_environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
            m_valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            m_projectionHost = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => new(new[]
        {
            new QualifiedName(AasV2ConformanceUnits.Aas),
            new QualifiedName(AasV2ConformanceUnits.Asset),
            new QualifiedName(AasV2ConformanceUnits.Submodel),
            new QualifiedName(AasV2ConformanceUnits.ConceptDescription),
            new QualifiedName(AasV2ConformanceUnits.View),
            new QualifiedName(AasV2ConformanceUnits.RelationshipElement),
            new QualifiedName(AasV2ConformanceUnits.Property),
            new QualifiedName(AasV2ConformanceUnits.MultiLanguageProperty),
            new QualifiedName(AasV2ConformanceUnits.Range),
            new QualifiedName(AasV2ConformanceUnits.Blob),
            new QualifiedName(AasV2ConformanceUnits.File),
            new QualifiedName(AasV2ConformanceUnits.ReferenceElement),
            new QualifiedName(AasV2ConformanceUnits.Capability),
            new QualifiedName(AasV2ConformanceUnits.SubmodelElementCollection),
            new QualifiedName(AasV2ConformanceUnits.Operation),
            new QualifiedName(AasV2ConformanceUnits.Event),
            new QualifiedName(AasV2ConformanceUnits.Entity)
        });

        /// <inheritdoc/>
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
                if (m_valueProvider is DocumentAasV2ValueProvider documentProvider)
                {
                    documentProvider.AddEnvironment(environment);
                }

                AasEnvironmentProjectionHandle handle = await m_projectionHost
                    .AddAsync(environment, m_valueProvider, m_operationHandler, cancellationToken)
                    .ConfigureAwait(false);
                m_handles.Add(handle);
            }
        }

        private readonly IAasV2EnvironmentProvider m_environmentProvider;
        private readonly IAasValueProvider m_valueProvider;
        private readonly IAasOperationHandler m_operationHandler;
        private readonly IAasEnvironmentProjectionHost m_projectionHost;
        private readonly List<AasEnvironmentProjectionHandle> m_handles = [];
    }
}
