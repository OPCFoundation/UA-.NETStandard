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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry;

namespace Opc.Ua.Aas.Client.Registry
{
    /// <summary>
    /// Base wrapper for one AAS registry group folder.
    /// </summary>
    public abstract class AasRegistryGroupClient
    {
        /// <summary>
        /// Creates a group wrapper.
        /// </summary>
        protected AasRegistryGroupClient(
            ISession session,
            NodeId groupNodeId,
            GroupTypeClient proxy,
            string sourceIdentityPropertyName,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (groupNodeId.IsNull)
            {
                throw new ArgumentException("A group NodeId is required.", nameof(groupNodeId));
            }
            if (proxy is null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (string.IsNullOrEmpty(sourceIdentityPropertyName))
            {
                throw new ArgumentException(
                    "A source identity Property name is required.",
                    nameof(sourceIdentityPropertyName));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            GroupNodeId = groupNodeId;
            Proxy = proxy;
            SourceIdentityPropertyName = sourceIdentityPropertyName;
            Telemetry = telemetry;
        }

        /// <summary>
        /// OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Group Object NodeId.
        /// </summary>
        public NodeId GroupNodeId { get; }

        /// <summary>
        /// Source-generated group proxy.
        /// </summary>
        public GroupTypeClient Proxy { get; }

        /// <summary>
        /// Telemetry context for generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// BrowseName of the source identity Property.
        /// </summary>
        protected string SourceIdentityPropertyName { get; }

        /// <summary>
        /// Reads the source identity defined by AAS clause 6.5.3.
        /// </summary>
        public ValueTask<string> ReadSourceIdentityAsync(CancellationToken ct = default)
        {
            return AasRegistryNodeReader.ReadRequiredStringPropertyAsync(
                Session,
                GroupNodeId,
                Session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3),
                SourceIdentityPropertyName,
                ct);
        }

        /// <summary>
        /// Deletes this group through the inherited xRegistry group lifecycle.
        /// </summary>
        public ValueTask DeleteAsync(uint expectedEpoch, CancellationToken ct = default)
        {
            return Proxy.DeleteAsync(expectedEpoch, ct);
        }

        /// <summary>
        /// Browses the documents organized by this group.
        /// </summary>
        protected ValueTask<ArrayOf<NodeId>> BrowseDocumentNodeIdsAsync(CancellationToken ct)
        {
            return AasRegistryNodeReader.BrowseOrganizedObjectsAsync(Session, GroupNodeId, ct);
        }
    }

    /// <summary>
    /// Client for an <c>AASShellGroupType</c> folder.
    /// </summary>
    public sealed class AasShellGroupClient : AasRegistryGroupClient
    {
        /// <summary>
        /// Creates a shell group client.
        /// </summary>
        public AasShellGroupClient(ISession session, NodeId groupNodeId, ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                new AASShellGroupTypeClient(session, groupNodeId, telemetry),
                "AasIdentifier",
                telemetry)
        {
            Proxy = new AASShellGroupTypeClient(session, groupNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS shell group proxy.
        /// </summary>
        public new AASShellGroupTypeClient Proxy { get; }

        /// <summary>
        /// Browses the submodel documents held by this shell group.
        /// </summary>
        /// <example>
        /// <code>
        /// ArrayOf&lt;AasSubmodelFileClient&gt; submodels = await shell.ListSubmodelsAsync(ct);
        /// </code>
        /// </example>
        public async ValueTask<ArrayOf<AasSubmodelFileClient>> ListSubmodelsAsync(CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodes = await BrowseDocumentNodeIdsAsync(ct).ConfigureAwait(false);
            var resources = new AasSubmodelFileClient[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                resources[i] = new AasSubmodelFileClient(Session, GroupNodeId, nodes[i], Telemetry);
            }
            return resources.ToArrayOf();
        }
    }

    /// <summary>
    /// Client for an <c>AASSubmodelTemplateGroupType</c> folder.
    /// </summary>
    public sealed class AasSubmodelTemplateGroupClient : AasRegistryGroupClient
    {
        /// <summary>
        /// Creates a submodel template group client.
        /// </summary>
        public AasSubmodelTemplateGroupClient(ISession session, NodeId groupNodeId, ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                new AASSubmodelTemplateGroupTypeClient(session, groupNodeId, telemetry),
                "TemplateNamespace",
                telemetry)
        {
            Proxy = new AASSubmodelTemplateGroupTypeClient(session, groupNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS submodel template group proxy.
        /// </summary>
        public new AASSubmodelTemplateGroupTypeClient Proxy { get; }

        /// <summary>
        /// Browses the submodel template documents held by this group.
        /// </summary>
        public async ValueTask<ArrayOf<AasSubmodelFileClient>> ListSubmodelTemplatesAsync(
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodes = await BrowseDocumentNodeIdsAsync(ct).ConfigureAwait(false);
            var resources = new AasSubmodelFileClient[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                resources[i] = new AasSubmodelFileClient(Session, GroupNodeId, nodes[i], Telemetry);
            }
            return resources.ToArrayOf();
        }
    }

    /// <summary>
    /// Client for an <c>AASConceptDictionaryGroupType</c> folder.
    /// </summary>
    public sealed class AasConceptDictionaryGroupClient : AasRegistryGroupClient
    {
        /// <summary>
        /// Creates a concept dictionary group client.
        /// </summary>
        public AasConceptDictionaryGroupClient(ISession session, NodeId groupNodeId, ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                new AASConceptDictionaryGroupTypeClient(session, groupNodeId, telemetry),
                "DictionaryIdentifier",
                telemetry)
        {
            Proxy = new AASConceptDictionaryGroupTypeClient(session, groupNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS concept dictionary group proxy.
        /// </summary>
        public new AASConceptDictionaryGroupTypeClient Proxy { get; }

        /// <summary>
        /// Browses the concept description documents held by this group.
        /// </summary>
        public async ValueTask<ArrayOf<AasConceptDescriptionFileClient>> ListConceptDescriptionsAsync(
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodes = await BrowseDocumentNodeIdsAsync(ct).ConfigureAwait(false);
            var resources = new AasConceptDescriptionFileClient[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                resources[i] = new AasConceptDescriptionFileClient(Session, GroupNodeId, nodes[i], Telemetry);
            }
            return resources.ToArrayOf();
        }
    }

    /// <summary>
    /// Client for an <c>AASPackageStoreGroupType</c> folder.
    /// </summary>
    public sealed class AasPackageStoreGroupClient : AasRegistryGroupClient
    {
        /// <summary>
        /// Creates a package store group client.
        /// </summary>
        public AasPackageStoreGroupClient(ISession session, NodeId groupNodeId, ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                new AASPackageStoreGroupTypeClient(session, groupNodeId, telemetry),
                "StoreIdentifier",
                telemetry)
        {
            Proxy = new AASPackageStoreGroupTypeClient(session, groupNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS package store group proxy.
        /// </summary>
        public new AASPackageStoreGroupTypeClient Proxy { get; }

        /// <summary>
        /// Browses the package documents held by this store.
        /// </summary>
        public async ValueTask<ArrayOf<AasPackageFileClient>> ListPackagesAsync(CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodes = await BrowseDocumentNodeIdsAsync(ct).ConfigureAwait(false);
            var resources = new AasPackageFileClient[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                resources[i] = new AasPackageFileClient(Session, GroupNodeId, nodes[i], Telemetry);
            }
            return resources.ToArrayOf();
        }
    }
}
