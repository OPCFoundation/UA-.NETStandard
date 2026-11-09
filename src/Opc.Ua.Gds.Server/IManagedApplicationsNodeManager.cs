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
    /// well-known <c>ManagedApplications</c> folder. A runtime-backed
    /// <see cref="DefaultManagedApplicationsNodeManager"/> binds the standard certificate and trust methods
    /// through a separate <see cref="ApplicationConfigurationPushBinding"/> for each application.
    /// </para>
    /// <para>
    /// The <see cref="StubManagedApplicationsNodeManager"/> provides a
    /// minimal implementation without application instances.
    /// An <see cref="IConfigurationDataStore"/> supplies application metadata only; it does not supply
    /// certificate stores, application activation or ConfigurationFile handlers.
    /// Use an <see cref="IManagedApplicationPushProvider"/> when application push management is required.
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
    /// that relies on the core address space's <c>ManagedApplications</c> folder
    /// without contributing application instances or method handlers. A production GDS should replace
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
    /// that creates <see cref="ApplicationConfigurationState"/> instances
    /// under the <c>ManagedApplications</c> folder. The data-store constructor exposes metadata;
    /// the runtime-provider constructor binds certificate and trust push management to independent applications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plug in any <see cref="IConfigurationDataStore"/> implementation
    /// (e.g. <see cref="InMemoryConfigurationDataStore"/> for testing or
    /// a production database-backed store) to enumerate application metadata.
    /// This does not expose the optional ConfigurationFile transaction surface.
    /// </para>
    /// <para>
    /// Each managed application is exposed as an
    /// <see cref="ApplicationConfigurationState"/> node with its
    /// <c>ApplicationUri</c>, <c>ProductUri</c>, <c>ApplicationType</c>,
    /// and <c>Enabled</c> properties populated from
    /// <see cref="ManagedApplicationInfo"/>.
    /// </para>
    /// </remarks>
    public partial class DefaultManagedApplicationsNodeManager
        : AsyncCustomNodeManager, IManagedApplicationsNodeManager, INodeManagerShutdown
    {
        /// <summary>
        /// Well-known NodeIds from the base UA namespace (ns=0).
        /// Using numeric literals avoids ambiguity between the GDS and
        /// base-UA generated ObjectTypes/Objects classes.
        /// </summary>
        private static readonly NodeId ManagedApplicationsFolderId = new(16706u);
        private static readonly NodeId ApplicationConfigurationTypeId = new(25731u);
        private readonly Dictionary<string, ApplicationConfigurationState> m_appNodes =
            new(System.StringComparer.Ordinal);

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
            m_managementConfiguration = configuration;
            // Namespace 0 is reserved for the OPC UA namespace (OPC 10000-5
            // §6.3.1): the application nodes are minted in an own namespace,
            // only the reference from the ns=0 ManagedApplications folder is
            // added through the external references.
            NamespaceUris = [ManagedApplicationsNamespaceUri];
        }

        /// <summary>
        /// Creates a node manager backed by independent application runtimes with real push handlers.
        /// </summary>
        public DefaultManagedApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IManagedApplicationPushProvider pushProvider)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DefaultManagedApplicationsNodeManager>())
        {
            m_pushProvider = pushProvider ?? throw new System.ArgumentNullException(nameof(pushProvider));
            m_managementConfiguration = configuration;
            NamespaceUris = [ManagedApplicationsNamespaceUri];
        }

        /// <summary>
        /// The namespace of the <see cref="ApplicationConfigurationState"/>
        /// nodes created by this node manager.
        /// </summary>
        public const string ManagedApplicationsNamespaceUri =
            Namespaces.OpcUaGds + "ManagedApplications/";

        /// <inheritdoc/>
        public IConfigurationDataStore? ConfigurationDataStore { get; }

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

            if (m_pushProvider != null)
            {
                ArrayOf<ManagedApplicationPushRegistration> applications = await m_pushProvider
                    .GetApplicationsAsync(cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < applications.Count; i++)
                {
                    ManagedApplicationPushRegistration registration = applications[i];
                    await CreatePushApplicationAsync(
                        registration, null, externalReferences, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            // Query the data store for managed applications and
            // create ApplicationConfigurationState instances under the
            // ManagedApplications folder.
            IReadOnlyList<ManagedApplicationInfo> apps = await ConfigurationDataStore!
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
            // Build a deterministic NodeId from the ApplicationUri. The URI
            // itself is the identifier, so distinct URIs never collide.
            var nodeId = new NodeId(info.ApplicationUri, NamespaceIndexes[0]);

            // Create instantiates the mandatory children of the type
            // (ApplicationUri, ApplicationType, ...); the optional ones
            // this node manager populates are added explicitly.
            var appNode = new ApplicationConfigurationState(null);
            appNode.Create(
                SystemContext,
                nodeId,
                new QualifiedName(info.ApplicationUri, NamespaceIndexes[0]),
                new LocalizedText(info.ApplicationUri),
                true);
            appNode.TypeDefinitionId = ApplicationConfigurationTypeId;
            appNode.WriteMask = AttributeWriteMask.None;
            appNode.UserWriteMask = AttributeWriteMask.None;
            appNode.AddProductUri(SystemContext);
            appNode.AddIsNonUaApplication(SystemContext);

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
