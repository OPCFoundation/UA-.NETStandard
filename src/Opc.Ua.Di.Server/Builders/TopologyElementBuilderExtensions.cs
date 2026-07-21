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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Common fluent operations for DI topology elements.
    /// </summary>
    public static class TopologyElementBuilderExtensions
    {
        /// <summary>
        /// Configures the element's <c>Identification</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithIdentificationGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Identification,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: true,
                configure);
        }

        /// <summary>
        /// Configures the <c>Configuration</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithConfigurationGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Configuration,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>Maintenance</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithMaintenanceGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Maintenance,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>Diagnostics</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithDiagnosticsGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Diagnostics,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>Status</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithStatusGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Status,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>Operational</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithOperationalGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Operational,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>Statistics</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithStatisticsGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.Statistics,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Configures the <c>OperationCounters</c> functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithOperationCountersGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                new QualifiedName(
                    WellKnownFunctionalGroups.OperationCounters,
                    builder.Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Adds or configures an application-defined functional group.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> WithFunctionalGroup<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            QualifiedName name,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            return ConfigureFunctionalGroup(
                builder,
                name,
                useIdentificationSlot: false,
                configure);
        }

        /// <summary>
        /// Adds a forward DI <c>ConnectsTo</c> reference.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> ConnectsTo<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            NodeId other)
            where TElement : TopologyElementState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (other.IsNull)
            {
                throw new ArgumentNullException(nameof(other));
            }
            builder.Element.AddReference(
                ResolveConnectsToRefType(builder),
                false,
                other);
            return builder;
        }

        /// <summary>
        /// Adds an inverse DI <c>ConnectsTo</c> reference.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> ConnectsToParent<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            NodeId parent)
            where TElement : TopologyElementState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (parent.IsNull)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            builder.Element.AddReference(
                ResolveConnectsToRefType(builder),
                true,
                parent);
            return builder;
        }

        /// <summary>
        /// Invokes application configuration against the raw state and context.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        public static ITopologyElementBuilder<TElement> Configure<TElement>(
            this ITopologyElementBuilder<TElement> builder,
            Action<TElement, ISystemContext> configure)
            where TElement : TopologyElementState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(builder.Element, builder.Context);
            return builder;
        }

        private static ITopologyElementBuilder<TElement> ConfigureFunctionalGroup<TElement>(
            ITopologyElementBuilder<TElement> builder,
            QualifiedName browseName,
            bool useIdentificationSlot,
            Action<IFunctionalGroupBuilder> configure)
            where TElement : TopologyElementState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FunctionalGroupState group = GetOrCreateFunctionalGroup(
                builder,
                browseName,
                useIdentificationSlot);

            INodeBuilder groupNode = builder.Node.Builder.Node(group.NodeId);
            var groupBuilder = new FunctionalGroupBuilder(
                group,
                groupNode,
                builder.Context);
            configure(groupBuilder);
            return builder;
        }

        private static FunctionalGroupState GetOrCreateFunctionalGroup<TElement>(
            ITopologyElementBuilder<TElement> builder,
            QualifiedName browseName,
            bool useIdentificationSlot)
            where TElement : TopologyElementState
        {
            NodeState? existing = builder.Element.FindChild(
                builder.Context,
                browseName);
            if (existing is FunctionalGroupState reusable)
            {
                return reusable;
            }

            FunctionalGroupState group;
            if (useIdentificationSlot && builder.Element.Identification == null)
            {
                builder.Element.AddIdentification(builder.Context);
                group = builder.Element.Identification!;
            }
            else
            {
                group = builder.Element.AddGroupIdentifier(
                    builder.Context,
                    browseName);
            }

            group.SymbolicName = browseName.Name ?? string.Empty;
            group.BrowseName = browseName;
            group.DisplayName = new LocalizedText(browseName.Name);
            group.NodeId = builder.Context.NodeIdFactory.New(
                builder.Context,
                group);
            group.TypeDefinitionId = NodeId.Create(
                Opc.Ua.Di.ObjectTypes.FunctionalGroupType,
                DiNodeManager.DiNamespaceUri,
                builder.Manager.Server.NamespaceUris);
            group.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            group.ModellingRuleId = NodeId.Null;

            builder.Manager.AddPredefinedNodeAsync(
                group,
                System.Threading.CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            return group;
        }

        private static NodeId ResolveConnectsToRefType<TElement>(
            ITopologyElementBuilder<TElement> builder)
            where TElement : TopologyElementState
        {
            return NodeId.Create(
                Opc.Ua.Di.ReferenceTypes.ConnectsTo,
                DiNodeManager.DiNamespaceUri,
                builder.Manager.Server.NamespaceUris);
        }
    }
}
