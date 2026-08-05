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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The NodeManager that owns the projection-document <c>View</c> Nodes. It is
    /// created once through the public NodeManager lifecycle and shares the
    /// <see cref="Namespaces.WotCon"/> namespace with the stable
    /// <see cref="WotRegistryNodeManager"/>, so a projection View lives in the
    /// registry namespace yet is added and removed independently of the registry
    /// model. Each <see cref="WotViewProjectionPlan"/> materializes to exactly one
    /// runtime-created <c>View</c> Node plus one organizational <c>Object</c> per
    /// group; the View <c>Organizes</c> the already-materialized member Nodes
    /// (which are owned by other NodeManagers) with forward-only references and
    /// creates no affordance Node of its own.
    /// </summary>
    internal sealed class WotProjectionViewNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes a new projection-view NodeManager.
        /// </summary>
        /// <param name="server">The server that owns the NodeManager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger for the NodeManager.</param>
        public WotProjectionViewNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : base(server, configuration, logger, Namespaces.WotCon)
        {
        }

        /// <summary>
        /// Materializes (or re-materializes) the View for one projection document
        /// and returns the number of Nodes created (the View plus every
        /// organizational Object). A View that already exists under the same
        /// NodeId is torn down first so a refresh that resolves a changed
        /// membership replaces it in place.
        /// </summary>
        /// <param name="request">The materialization request.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The count of Nodes the materializer created.</returns>
        public async ValueTask<int> ApplyViewAsync(
            WotViewProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Find(request.ViewNodeId) is not null)
            {
                await RemoveViewAsync(request.ViewNodeId, cancellationToken).ConfigureAwait(false);
            }

            ViewState view = BuildView(request);
            await AddPredefinedNodeAsync(SystemContext, view, cancellationToken).ConfigureAwait(false);

            // The View is browsable from the standard Views folder; that folder is
            // owned by the core NodeManager, so the forward Organizes edge is added
            // through the master. The inverse edge on the View (added in BuildView)
            // makes the deletion path remove this forward edge automatically.
            var viewsFolderLink = new List<IReference>
            {
                new ReferenceNode
                {
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                    IsInverse = false,
                    TargetId = request.ViewNodeId
                }
            };
            await Server.NodeManager
                .AddReferencesAsync(Ua.ObjectIds.ViewsFolder, viewsFolderLink, cancellationToken)
                .ConfigureAwait(false);

            m_logger.MaterializedProjectionView(
                request.ViewNodeId,
                request.Plan.MaterializedNodeCount,
                request.Plan.OrganizedNodeIds.Count);
            return request.Plan.MaterializedNodeCount;
        }

        /// <summary>
        /// Removes a materialized View and its organizational Objects, leaving the
        /// organized member Nodes untouched. The forward <c>Organizes</c> edge
        /// from the Views folder is torn down together with the View through the
        /// standard predefined-node reference cleanup.
        /// </summary>
        /// <param name="viewNodeId">The NodeId of the View to remove.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        public async ValueTask RemoveViewAsync(
            NodeId viewNodeId,
            CancellationToken cancellationToken = default)
        {
            if (viewNodeId.IsNull || Find(viewNodeId) is null)
            {
                return;
            }
            await DeleteNodeAsync(SystemContext, viewNodeId, cancellationToken).ConfigureAwait(false);
            m_logger.RemovedProjectionView(viewNodeId);
        }

        private ViewState BuildView(WotViewProjectionRequest request)
        {
            WotViewProjectionPlan plan = request.Plan;
            ushort namespaceIndex = request.ViewNodeId.NamespaceIndex;
            string baseId = request.ViewNodeId.IdentifierAsString;

            var view = new ViewState
            {
                SymbolicName = ViewSymbolicName,
                NodeId = request.ViewNodeId,
                BrowseName = new QualifiedName(ViewSymbolicName, namespaceIndex),
                DisplayName = LocalizedText.From(ViewSymbolicName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None,
                ContainsNoLoops = false
            };

            PropertyState<uint> viewVersion = PropertyState<uint>.With<VariantBuilder>(view);
            viewVersion.SymbolicName = ViewVersionBrowseName;
            viewVersion.NodeId = new NodeId(baseId + "/ViewVersion", namespaceIndex);
            viewVersion.BrowseName = new QualifiedName(ViewVersionBrowseName);
            viewVersion.DisplayName = LocalizedText.From(ViewVersionBrowseName);
            viewVersion.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            viewVersion.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            viewVersion.DataType = Ua.DataTypeIds.UInt32;
            viewVersion.ValueRank = ValueRanks.Scalar;
            viewVersion.AccessLevel = AccessLevels.CurrentRead;
            viewVersion.UserAccessLevel = AccessLevels.CurrentRead;
            viewVersion.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            viewVersion.Historizing = false;
            viewVersion.Value = plan.ViewVersion;
            viewVersion.StatusCode = StatusCodes.Good;
            view.AddChild(viewVersion);

            AddOrganizedMembers(view, plan.OrganizedNodeIds);

            int groupIndex = 0;
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                BuildGroup(view, plan.Groups[i], baseId, namespaceIndex, ref groupIndex);
            }

            // The inverse Organizes edge to the Views folder is what the deletion
            // path uses to remove the forward edge added by the master.
            view.AddReference(Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ViewsFolder);
            return view;
        }

        private void BuildGroup(
            NodeState parent,
            WotOrganizationalGroup group,
            string baseId,
            ushort namespaceIndex,
            ref int groupIndex)
        {
            int index = groupIndex++;
            string identifier = string.Format(
                CultureInfo.InvariantCulture, "{0}/group/{1}", baseId, index);
            var folder = new FolderState(parent)
            {
                SymbolicName = group.RefName,
                ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                NodeId = new NodeId(identifier, namespaceIndex),
                BrowseName = new QualifiedName(group.RefName, namespaceIndex),
                DisplayName = LocalizedText.From(group.RefName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            AddOrganizedMembers(folder, group.OrganizedNodeIds);
            parent.AddChild(folder);

            for (int i = 0; i < group.Groups.Count; i++)
            {
                BuildGroup(folder, group.Groups[i], baseId, namespaceIndex, ref groupIndex);
            }
        }

        private static void AddOrganizedMembers(NodeState organizer, ArrayOf<NodeId> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                NodeId member = members[i];
                if (!member.IsNull)
                {
                    // Forward-only: the organized Node lives in another NodeManager
                    // and is never modified, so no inverse reference is added to it.
                    organizer.AddReference(Ua.ReferenceTypeIds.Organizes, false, member);
                }
            }
        }

        private const string ViewSymbolicName = "View";
        private const string ViewVersionBrowseName = "ViewVersion";
    }

    /// <summary>
    /// Holds source-generated log messages emitted by the WoT projection-view NodeManager.
    /// </summary>
    internal static partial class WotProjectionViewNodeManagerLog
    {
        /// <summary>
        /// Logs that a projection View was materialized into the address space.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotProjectionViewNodeManager + 0, Level = LogLevel.Debug,
            Message = "Materialized WoT projection View {ViewNodeId} ({NodeCount} Node(s), {MemberCount} organized).")]
        public static partial void MaterializedProjectionView(
            this ILogger logger,
            NodeId viewNodeId,
            int nodeCount,
            int memberCount);

        /// <summary>
        /// Logs that a projection View was removed from the address space.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotProjectionViewNodeManager + 1, Level = LogLevel.Debug,
            Message = "Removed WoT projection View {ViewNodeId}.")]
        public static partial void RemovedProjectionView(this ILogger logger, NodeId viewNodeId);
    }
}
