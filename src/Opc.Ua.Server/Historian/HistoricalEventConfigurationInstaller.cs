/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Installs a populated <c>HistoricalEventConfigurationType</c>
    /// companion object under a historical event notifier.
    /// </summary>
    public static class HistoricalEventConfigurationInstaller
    {
        /// <summary>
        /// Ensures that <paramref name="notifier"/> has a historical event
        /// configuration populated from <paramref name="provider"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public static async ValueTask<HistoricalEventConfigurationState>
            EnsureInstalledAsync(
                ISystemContext context,
                BaseObjectState notifier,
                IHistorianProvider provider,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(notifier.NodeId, cancellationToken)
                .ConfigureAwait(false);
            HistoricalEventConfigurationState? configuration =
                FindExistingConfiguration(context, notifier);
            if (configuration == null)
            {
                configuration = context.CreateInstanceOfHistoricalEventConfigurationType(
                    notifier,
                    new QualifiedName(BrowseNames.HAConfiguration));
                if (context.NodeIdFactory != null)
                {
                    configuration.NodeId = context.NodeIdFactory.New(
                        context,
                        configuration);
                    AssignInstanceNodeIds(
                        context,
                        context.NodeIdFactory,
                        configuration);
                }
                else
                {
                    AssignGeneratedNodeIds(
                        context,
                        configuration,
                        notifier.NodeId.NamespaceIndex);
                }

                notifier.AddReference(
                    ReferenceTypeIds.HasHistoricalConfiguration,
                    false,
                    configuration.NodeId);
                configuration.AddReference(
                    ReferenceTypeIds.HasHistoricalConfiguration,
                    true,
                    notifier.NodeId);
                notifier.AddChild(configuration);
            }

            PopulateProperties(context, configuration, capabilities);
            return configuration;
        }

        private static void PopulateProperties(
            ISystemContext context,
            HistoricalEventConfigurationState configuration,
            HistorianNodeCapabilities capabilities)
        {
            configuration
                .AddStartOfArchive(
                    context,
                    capabilities.StartOfArchive != DateTimeUtc.MinValue,
                    property => property.Value = capabilities.StartOfArchive)
                .AddStartOfOnlineArchive(
                    context,
                    capabilities.StartOfOnlineArchive != DateTimeUtc.MinValue,
                    property => property.Value = capabilities.StartOfOnlineArchive)
                .AddSortByEventFields(
                    context,
                    !capabilities.SortByEventFields.IsEmpty,
                    property => property.Value = capabilities.SortByEventFields);

            FolderState? eventTypes = configuration.EventTypes ??
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "HistoricalEventConfigurationType is missing its mandatory EventTypes folder.");
            var configuredTypes = new HashSet<NodeId>();
            for (int i = 0; i < capabilities.EventTypes.Count; i++)
            {
                NodeId eventType = capabilities.EventTypes[i];
                if (!eventType.IsNull)
                {
                    configuredTypes.Add(eventType);
                }
            }
            var references = new List<IReference>();
            eventTypes.GetReferences(context, references);
            for (int i = 0; i < references.Count; i++)
            {
                IReference reference = references[i];
                if (!reference.IsInverse &&
                    reference.ReferenceTypeId == ReferenceTypeIds.Organizes)
                {
                    var target = ExpandedNodeId.ToNodeId(
                        reference.TargetId,
                        context.NamespaceUris);
                    if (!target.IsNull && !configuredTypes.Contains(target))
                    {
                        eventTypes.RemoveReference(
                            ReferenceTypeIds.Organizes,
                            false,
                            reference.TargetId);
                    }
                }
            }
            for (int i = 0; i < capabilities.EventTypes.Count; i++)
            {
                NodeId eventType = capabilities.EventTypes[i];
                if (!eventType.IsNull &&
                    !eventTypes.ReferenceExists(
                        ReferenceTypeIds.Organizes,
                        false,
                        eventType))
                {
                    eventTypes.AddReference(
                        ReferenceTypeIds.Organizes,
                        false,
                        eventType);
                }
            }
        }

        private static HistoricalEventConfigurationState? FindExistingConfiguration(
            ISystemContext context,
            BaseObjectState notifier)
        {
            return notifier.FindChild(
                context,
                new QualifiedName(BrowseNames.HAConfiguration)) as
                HistoricalEventConfigurationState;
        }

        private static void AssignInstanceNodeIds(
            ISystemContext context,
            INodeIdFactory nodeIdFactory,
            NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = nodeIdFactory.New(context, child);
                AssignInstanceNodeIds(context, nodeIdFactory, child);
            }
        }

        private static void AssignGeneratedNodeIds(
            ISystemContext context,
            NodeState node,
            ushort namespaceIndex)
        {
            node.NodeId = new NodeId(Guid.NewGuid(), namespaceIndex);
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                AssignGeneratedNodeIds(context, child, namespaceIndex);
            }
        }
    }
}
