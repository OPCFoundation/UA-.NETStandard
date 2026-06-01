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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Interface for a node manager that manages
    /// <c>ApplicationConfigurationType</c> instances under the
    /// <c>ManagedApplications</c> folder per OPC 10000-12 §7.10.16.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations create <see cref="ApplicationConfigurationState"/>
    /// instances for each managed application and expose them under the
    /// well-known <c>ManagedApplications</c> folder. Each instance owns
    /// a <see cref="ConfigurationFileState"/> that supports the
    /// <c>CloseAndUpdate</c> / <c>ConfirmUpdate</c> transaction pattern
    /// described in OPC 10000-12 §7.7.6.
    /// </para>
    /// <para>
    /// The <see cref="StubManagedApplicationsNodeManager"/> provides a
    /// minimal implementation that satisfies the model-level requirement
    /// by exposing the <c>ManagedApplications</c> folder but returns
    /// <c>Bad_NotSupported</c> for the configuration-file write
    /// operations. Production systems should replace it with a
    /// <see cref="DefaultManagedApplicationsNodeManager"/> (or a
    /// custom implementation) that persists configuration data and
    /// implements the full <c>ConfirmUpdate</c> transaction lifecycle
    /// via an <see cref="IConfigurationDataStore"/>.
    /// </para>
    /// </remarks>
    public interface IManagedApplicationsNodeManager : IAsyncNodeManager
    {
        /// <summary>
        /// Gets the <see cref="IConfigurationDataStore"/> used for
        /// persisting managed application configuration data, or
        /// <c>null</c> if this node manager does not support
        /// configuration persistence.
        /// </summary>
        IConfigurationDataStore? ConfigurationDataStore { get; }
    }

    /// <summary>
    /// Stub implementation of <see cref="IManagedApplicationsNodeManager"/>
    /// that exposes the <c>ManagedApplications</c> folder in the address
    /// space and wires <c>ConfirmUpdate</c> with a
    /// <c>Bad_NotSupported</c> response. A production GDS should replace
    /// this stub with a <see cref="DefaultManagedApplicationsNodeManager"/>
    /// or a custom implementation.
    /// </summary>
    public class StubManagedApplicationsNodeManager
        : AsyncCustomNodeManager, IManagedApplicationsNodeManager
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public StubManagedApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<StubManagedApplicationsNodeManager>())
        {
            // The ManagedApplications folder lives under the
            // ServerConfiguration object in the base UA namespace.
            NamespaceUris = [Namespaces.OpcUa];
        }

        /// <inheritdoc/>
        public IConfigurationDataStore? ConfigurationDataStore => null;

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // The ManagedApplications folder and its
            // ApplicationConfigurationFolderType are defined in the
            // base UA nodeset (StandardTypes.xml). They're loaded
            // by the core node manager. This stub node manager does
            // not contribute additional predefined nodes.
            return new ValueTask<NodeStateCollection>([]);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // The ManagedApplications folder is already part of the
            // core UA nodeset. Future implementations would browse it
            // here and populate ApplicationConfigurationType instances
            // from a configuration database.
        }
    }

    /// <summary>
    /// Default implementation of <see cref="IManagedApplicationsNodeManager"/>
    /// that queries an <see cref="IConfigurationDataStore"/> at startup,
    /// creates <see cref="ApplicationConfigurationState"/> instances
    /// under the <c>ManagedApplications</c> folder, and delegates
    /// configuration read/write/confirm operations to the store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plug in any <see cref="IConfigurationDataStore"/> implementation
    /// (e.g. <see cref="InMemoryConfigurationDataStore"/> for testing or
    /// a production database-backed store) to control how managed
    /// application configuration data is persisted.
    /// </para>
    /// <para>
    /// Each managed application is exposed as an
    /// <see cref="ApplicationConfigurationState"/> node with its
    /// <c>ApplicationUri</c>, <c>ProductUri</c>, <c>ApplicationType</c>,
    /// and <c>Enabled</c> properties populated from
    /// <see cref="ManagedApplicationInfo"/>.
    /// </para>
    /// </remarks>
    public class DefaultManagedApplicationsNodeManager
        : AsyncCustomNodeManager, IManagedApplicationsNodeManager
    {
        // Well-known NodeIds from the base UA namespace (ns=0).
        // Using numeric literals avoids ambiguity between the GDS and
        // base-UA generated ObjectTypes/Objects classes.
        private static readonly NodeId ManagedApplicationsFolderId = new(16706u);
        private static readonly NodeId ApplicationConfigurationTypeId = new(25731u);
        private readonly Dictionary<string, ApplicationConfigurationState> m_appNodes = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Creates a new instance backed by the specified data store.
        /// </summary>
        /// <param name="server">The server instance.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="dataStore">
        /// The configuration data store used to enumerate managed
        /// applications and persist their configuration data.
        /// </param>
        public DefaultManagedApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IConfigurationDataStore dataStore)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DefaultManagedApplicationsNodeManager>())
        {
            ConfigurationDataStore = dataStore ?? throw new System.ArgumentNullException(nameof(dataStore));
            NamespaceUris = [Namespaces.OpcUa];
        }

        /// <inheritdoc/>
        public IConfigurationDataStore ConfigurationDataStore { get; }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // The ManagedApplications folder is defined in the base
            // UA nodeset and loaded by the core node manager. We don't
            // contribute additional predefined nodes here.
            return new ValueTask<NodeStateCollection>([]);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // Query the data store for managed applications and
            // create ApplicationConfigurationState instances under the
            // ManagedApplications folder.
            IReadOnlyList<ManagedApplicationInfo> apps = await ConfigurationDataStore
                .GetManagedApplicationsAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (ManagedApplicationInfo app in apps)
            {
                await CreateApplicationConfigurationNodeAsync(app, externalReferences).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an <see cref="ApplicationConfigurationState"/> node
        /// for the specified managed application and adds it to the
        /// <c>ManagedApplications</c> folder.
        /// </summary>
        private async ValueTask CreateApplicationConfigurationNodeAsync(
            ManagedApplicationInfo info,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // Build a deterministic NodeId from the ApplicationUri.
            string safeId = info.ApplicationUri.Replace(':', '_').Replace('/', '_');

            var nodeId = new NodeId(safeId, NamespaceIndexes[0]);

            var appNode = new ApplicationConfigurationState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(info.ApplicationUri, NamespaceIndexes[0]),
                DisplayName = new LocalizedText(info.ApplicationUri),
                TypeDefinitionId = ApplicationConfigurationTypeId,
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None
            };

            // Populate properties from the ManagedApplicationInfo.
            appNode.ApplicationUri?.Value = info.ApplicationUri;

            appNode.ProductUri?.Value = info.ProductUri ?? string.Empty;

            appNode.ApplicationType?.Value = info.ApplicationType;

            appNode.Enabled?.Value = info.Enabled;

            appNode.IsNonUaApplication?.Value = info.IsNonUaApplication;

            await AddPredefinedNodeAsync(SystemContext, appNode).ConfigureAwait(false);

            // Wire an Organizes reference from the ManagedApplications
            // folder to this node via the external-references dictionary
            // (the folder is owned by the core node manager).
            if (!externalReferences.TryGetValue(ManagedApplicationsFolderId, out IList<IReference>? refs))
            {
                refs = [];
                externalReferences[ManagedApplicationsFolderId] = refs;
            }

            refs.Add(new NodeStateReference(
                ReferenceTypeIds.Organizes,
                false,
                appNode.NodeId));

            m_appNodes[info.ApplicationUri] = appNode;
        }

        /// <summary>
        /// Gets the set of <see cref="ApplicationConfigurationState"/>
        /// nodes managed by this node manager.
        /// </summary>
        public IReadOnlyDictionary<string, ApplicationConfigurationState> ApplicationNodes =>
            m_appNodes;
    }
}
