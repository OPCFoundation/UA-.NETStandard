/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Owns the calculation, authorization and optional persistence of one statistics collection.
    /// </summary>
    /// <remarks>
    /// Reset must commit all values and their StartTime together before returning its snapshot.
    /// Cancellation or failure before that commit leaves the previous collection intact. A provider
    /// must return its committed snapshot after commit, even if cancellation arrives subsequently.
    /// The binding borrows this provider; its application owns its lifetime.
    /// </remarks>
    public interface IIaStatisticsProvider
    {
        /// <summary>
        /// Reads an immutable, internally consistent snapshot in configured component order.
        /// </summary>
        ValueTask<IaStatisticsSnapshot> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the requesting user's permission to reset this entire collection.
        /// </summary>
        ValueTask<ServiceResult> AuthorizeResetAsync(
            ISystemContext requestContext,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets the entire collection and returns the committed state, including the new StartTime.
        /// </summary>
        ValueTask<IaStatisticsSnapshot> ResetAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An owner-supplied coherent observation of a statistics collection.
    /// </summary>
    public sealed record IaStatisticsSnapshot
    {
        /// <summary>
        /// Gets the beginning of collection, including after a persisted restart.
        /// </summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>
        /// Gets statistic values in the binding's component order, with acquisition status and time.
        /// </summary>
        public required ArrayOf<DataValue> Values { get; init; }
    }

    /// <summary>
    /// Selects aggregate semantics or one rolling-window representation.
    /// </summary>
    public sealed record IaStatisticsOptions
    {
        /// <summary>
        /// Gets whether only a rolling subset contributes to each statistic.
        /// </summary>
        public bool Rolling { get; init; }

        /// <summary>
        /// Gets the rolling time window in milliseconds; mutually exclusive with WindowNumberOfValues.
        /// </summary>
        public double? WindowDuration { get; init; }

        /// <summary>
        /// Gets the rolling sample-count window; mutually exclusive with WindowDuration.
        /// </summary>
        public uint? WindowNumberOfValues { get; init; }

        /// <summary>
        /// Gets the provider's non-client reset condition, or null to omit this aggregate property.
        /// </summary>
        public string? ResetCondition { get; init; }

        /// <summary>
        /// Gets the owning application's lookup for nonstandard grouping ObjectTypes.
        /// </summary>
        public Func<NodeId, BaseObjectTypeState?>? ResolveGroupType { get; init; }

        /// <summary>
        /// Validates the selected interface's metadata before changing an address space.
        /// </summary>
        public void Validate()
        {
            if ((WindowDuration.HasValue && WindowNumberOfValues.HasValue) ||
                (!Rolling && (WindowDuration.HasValue || WindowNumberOfValues.HasValue)) ||
                (Rolling && ResetCondition != null) ||
                (WindowDuration is double duration && (double.IsNaN(duration) ||
                    double.IsInfinity(duration) || duration <= 0)) ||
                WindowNumberOfValues == 0)
            {
                throw new ArgumentException("Select aggregate metadata or at most one positive rolling window.");
            }
        }
    }

    /// <summary>
    /// Composes an IA statistics interface onto an application-owned object.
    /// </summary>
    /// <remarks>
    /// Construct before registering the object's nodes. The application supplies the statistical
    /// variables and calculation; this binding supplies interface references, metadata and grouped
    /// reset. Dispose only after the owner has stopped accepting calls and drained all refresh and
    /// reset operations, including reset authorization.
    /// </remarks>
    public sealed class IaStatistics : IDisposable
    {
        /// <summary>
        /// Adds one aggregate or rolling statistics interface with a fixed set of statistic variables.
        /// </summary>
        public IaStatistics(
            ISystemContext context,
            BaseObjectState state,
            ArrayOf<BaseVariableState> components,
            IIaStatisticsProvider provider,
            IaStatisticsOptions? options = null,
            ArrayOf<BaseObjectState> groups = default,
            CancellationToken cancellationToken = default)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            State = state ?? throw new ArgumentNullException(nameof(state));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_options = options ?? new IaStatisticsOptions();
            m_options.Validate();
            if (components.Count == 0)
            {
                throw new ArgumentException("At least one statistic component is required.", nameof(components));
            }
            if (state.NodeId.IsNull)
            {
                throw new ArgumentException("The statistics owner requires an assigned NodeId.", nameof(state));
            }
            m_components = components.Span.ToArray();
            ushort ns = checked((ushort)context.NamespaceUris.GetIndex(Namespaces.IA));
            var names = new HashSet<NodeId>();
            for (int i = 0; i < components.Count; i++)
            {
                BaseVariableState component = components[i];
                if (component == null || component.NodeId.IsNull || component is PropertyState ||
                    component.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasProperty ||
                    component.NodeId == state.NodeId || !names.Add(component.NodeId))
                {
                    throw new ArgumentException(
                        "Statistic components need distinct, assigned NodeIds.", nameof(components));
                }
            }
            ValidateGroups(context, state, groups, names, m_options.ResolveGroupType, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var children = new List<BaseInstanceState>();
            state.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.BrowseName.NamespaceIndex == ns &&
                    child.BrowseName.Name is BrowseNames.StartTime or BrowseNames.ResetStatistics or
                        BrowseNames.ResetCondition or BrowseNames.WindowDuration or BrowseNames.WindowNumberOfValues)
                {
                    throw new ArgumentException("The object already contains statistics metadata.", nameof(state));
                }
            }
            NodeId interfaceId = ExpandedNodeId.ToNodeId(
                m_options.Rolling ? ObjectTypeIds.IRollingStatisticsType : ObjectTypeIds.IAggregateStatisticsType,
                context.NamespaceUris);
            state.AddReference(Opc.Ua.ReferenceTypeIds.HasInterface, false, interfaceId);
            NodeId statisticReference = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasStatisticComponent,
                context.NamespaceUris);
            for (int i = 0; i < components.Count; i++)
            {
                BaseVariableState component = components[i];
                bool grouped = false;
                for (int j = 0; j < groups.Count; j++)
                {
                    grouped |= IsDescendant(component, groups[j]);
                }
                if (!grouped)
                {
                    state.AddReference(statisticReference, false, component.NodeId);
                    component.AddReference(statisticReference, true, state.NodeId);
                }
                component.AccessLevel = AccessLevels.CurrentRead;
                component.UserAccessLevel = AccessLevels.CurrentRead;
            }
            for (int i = 0; i < groups.Count; i++)
            {
                state.AddReference(statisticReference, false, groups[i].NodeId);
                groups[i].AddReference(statisticReference, true, state.NodeId);
            }
            StartTime = AddProperty(BrowseNames.StartTime, Opc.Ua.DataTypeIds.DateTime, Variant.Null, ns);
            if (m_options.ResetCondition != null)
            {
                AddProperty(BrowseNames.ResetCondition, Opc.Ua.DataTypeIds.String,
                    Variant.From(m_options.ResetCondition), ns);
            }
            if (m_options.WindowDuration is double window)
            {
                AddProperty(BrowseNames.WindowDuration, Opc.Ua.DataTypeIds.Duration, Variant.From(window), ns);
            }
            if (m_options.WindowNumberOfValues is uint count)
            {
                AddProperty(BrowseNames.WindowNumberOfValues, Opc.Ua.DataTypeIds.UInt32, Variant.From(count), ns);
            }
            ResetStatistics = new MethodState(state);
            ResetStatistics.Create(context, NodeId.Null, new QualifiedName(BrowseNames.ResetStatistics, ns),
                new LocalizedText(BrowseNames.ResetStatistics), true);
            ResetStatistics.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            ResetStatistics.MethodDeclarationId = ExpandedNodeId.ToNodeId(
                MethodIds.IStatisticsType_ResetStatistics, context.NamespaceUris);
            ResetStatistics.Executable = true;
            ResetStatistics.UserExecutable = true;
            ResetStatistics.OnCallMethod2Async = (request, _, _, _, _, ct) => ResetAsync(request, ct);
            state.AddChild(ResetStatistics);
        }

        /// <summary>
        /// Gets the application-owned object implementing the selected interface.
        /// </summary>
        public BaseObjectState State { get; }

        /// <summary>
        /// Gets the collection StartTime property.
        /// </summary>
        public PropertyState StartTime { get; }

        /// <summary>
        /// Gets the authorized grouped reset method.
        /// </summary>
        public MethodState ResetStatistics { get; }

        /// <summary>
        /// Publishes a provider snapshot, serializing reads, reset commits and notification dispatch on this binding.
        /// </summary>
        /// <remarks>
        /// The publication gate remains held until committed notifications complete. Notification sinks must
        /// not await or synchronously block on another refresh or reset through this binding. Notifications
        /// ignore caller cancellation; errors propagate without reverting published values.
        /// </remarks>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            IaStatisticsSnapshot snapshot;
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Publish(snapshot);
                await ClearPublishedChangeMasksAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Authorizes and resets the complete collection, publishing only a successful committed snapshot.
        /// </summary>
        /// <remarks>
        /// The reset and publication gates remain held until committed notifications complete. Notification
        /// sinks must not await or synchronously block on another refresh or reset through this binding.
        /// Notifications ignore caller cancellation; errors propagate without reverting the provider commit
        /// or published values.
        /// </remarks>
        public async ValueTask<ServiceResult> ResetAsync(
            ISystemContext requestContext,
            CancellationToken cancellationToken = default)
        {
            if (requestContext == null)
            {
                throw new ArgumentNullException(nameof(requestContext));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await m_resetGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ServiceResult authorization = await m_provider.AuthorizeResetAsync(
                    requestContext, cancellationToken).ConfigureAwait(false);
                bool authorized = ServiceResult.IsGood(authorization);
                await m_gate.WaitAsync(
                    authorized ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (!authorized)
                    {
                        return authorization;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    IaStatisticsSnapshot snapshot = await m_provider.ResetAsync(cancellationToken)
                        .ConfigureAwait(false);
                    Publish(snapshot);
                    await ClearPublishedChangeMasksAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_gate.Release();
                }
            }
            finally
            {
                m_resetGate.Release();
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Releases serialization resources after the owner has drained operations.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
            m_resetGate.Dispose();
        }

        /// <summary>
        /// Validates indirect groups and prevents a nested statistics owner or unbound statistic.
        /// </summary>
        private static void ValidateGroups(
            ISystemContext context,
            BaseObjectState owner,
            ArrayOf<BaseObjectState> groups,
            HashSet<NodeId> quantities,
            Func<NodeId, BaseObjectTypeState?>? resolveType,
            CancellationToken cancellationToken)
        {
            var identities = new HashSet<NodeId>();
            NodeId statisticsType = ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType, context.NamespaceUris);
            for (int i = 0; i < groups.Count; i++)
            {
                BaseObjectState group = groups[i];
                if (group == null || group.NodeId.IsNull || group == owner || !identities.Add(group.NodeId))
                {
                    throw new ArgumentException(
                        "Statistic groups require distinct assigned identities.", nameof(groups));
                }
                var pending = new Stack<NodeState>();
                var seen = new HashSet<NodeState>();
                pending.Push(group);
                bool hasQuantity = false;
                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NodeState node = pending.Pop();
                    if (!seen.Add(node))
                    {
                        throw new ArgumentException(
                            "Statistic group children must not contain cycles.", nameof(groups));
                    }
                    if (node is BaseObjectState instance)
                    {
                        ValidateGroupType(context, instance.TypeDefinitionId, statisticsType,
                            resolveType, cancellationToken);
                    }
                    var references = new List<IReference>();
                    node.GetReferences(context, references);
                    foreach (IReference reference in references)
                    {
                        if (!reference.IsInverse && reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasInterface)
                        {
                            NodeId implemented = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                            if (implemented == statisticsType ||
                                context.TypeTable.IsTypeOf(implemented, statisticsType))
                            {
                                throw new ArgumentException(
                                    "An indirect statistic group cannot contain another statistics owner.",
                                    nameof(groups));
                            }
                        }
                    }
                    if (node is BaseVariableState variable && variable.ReferenceTypeId !=
                        Opc.Ua.ReferenceTypeIds.HasProperty)
                    {
                        if (!quantities.Contains(variable.NodeId))
                        {
                            throw new ArgumentException(
                                "Every grouped statistic must have a provider snapshot slot.", nameof(groups));
                        }
                        hasQuantity = true;
                    }
                    var children = new List<BaseInstanceState>();
                    node.GetChildren(context, children);
                    foreach (BaseInstanceState child in children)
                    {
                        if (child.ReferenceTypeId != Opc.Ua.ReferenceTypeIds.HasProperty && child is not MethodState)
                        {
                            pending.Push(child);
                        }
                    }
                }
                if (!hasQuantity)
                {
                    throw new ArgumentException("A statistic group requires at least one quantity.", nameof(groups));
                }
            }
        }

        /// <summary>
        /// Rejects inherited statistics interfaces using the owner's indexed ObjectType metadata.
        /// </summary>
        private static void ValidateGroupType(
            ISystemContext context,
            NodeId type,
            NodeId statisticsType,
            Func<NodeId, BaseObjectTypeState?>? resolveType,
            CancellationToken cancellationToken)
        {
            var seen = new HashSet<NodeId>();
            while (type != Opc.Ua.ObjectTypeIds.BaseObjectType && type != Opc.Ua.ObjectTypeIds.FolderType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (type.IsNull || !seen.Add(type) || seen.Count > 128)
                {
                    throw new ArgumentException(
                        "Statistic group types require a bounded, acyclic ObjectType ancestry.");
                }
                BaseObjectTypeState? definition = resolveType?.Invoke(type);
                if (definition == null || definition.NodeId != type)
                {
                    throw new ArgumentException("The statistic group ObjectType metadata is missing or mismatched.");
                }
                var references = new List<IReference>();
                definition.GetReferences(context, references);
                foreach (IReference reference in references)
                {
                    if (!reference.IsInverse && reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasInterface)
                    {
                        NodeId implemented = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                        if (implemented == statisticsType || context.TypeTable.IsTypeOf(implemented, statisticsType))
                        {
                            throw new ArgumentException(
                                "An indirect statistic group type cannot implement a statistics interface.");
                        }
                    }
                }
                NodeId supertype = context.TypeTable.FindSuperType(type);
                if (supertype.IsNull || supertype != definition.SuperTypeId)
                {
                    throw new ArgumentException("Statistic group ObjectType metadata disagrees with the type table.");
                }
                type = supertype;
            }
        }

        /// <summary>
        /// Follows the application's existing parent chain without adding redundant direct references.
        /// </summary>
        private static bool IsDescendant(BaseInstanceState node, BaseObjectState group)
        {
            NodeState? parent = node.Parent;
            var seen = new HashSet<NodeState>();
            while (parent != null && seen.Add(parent))
            {
                if (ReferenceEquals(parent, group))
                {
                    return true;
                }
                parent = (parent as BaseInstanceState)?.Parent;
            }
            return false;
        }

        /// <summary>
        /// Validates a complete provider response before publishing any field.
        /// </summary>
        private void Publish(IaStatisticsSnapshot snapshot)
        {
            if (snapshot == null || snapshot.StartTime.IsNull || snapshot.Values.Count != m_components.Count)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The statistics provider returned an incomplete snapshot.");
            }
            var values = new DataValue[m_components.Count];
            for (int i = 0; i < m_components.Count; i++)
            {
                DataValue value = snapshot.Values[i];
                if (value.IsNull || value.SourceTimestamp.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A statistic requires a source timestamp and explicit quality.");
                }
                value = value.WithWrappedValue(IaValueCopy.Own(value.WrappedValue, m_context));
                if ((value.WrappedValue.IsNull && !StatusCode.IsBad(value.StatusCode)) ||
                    !value.WrappedValue.IsNull &&
                    TypeInfo.IsInstanceOfDataType(value.WrappedValue, m_components[i].DataType,
                        m_components[i].ValueRank, m_context.NamespaceUris, m_context.TypeTable).IsUnknown)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }
                values[i] = value;
            }
            for (int i = 0; i < m_components.Count; i++)
            {
                DataValue value = values[i];
                BaseVariableState component = m_components[i];
                component.WrappedValue = value.WrappedValue;
                component.StatusCode = value.StatusCode;
                component.Timestamp = value.SourceTimestamp;
            }
            StartTime.Value = Variant.From(snapshot.StartTime);
            StartTime.StatusCode = StatusCodes.Good;
            StartTime.Timestamp = snapshot.StartTime;
        }

        /// <summary>
        /// Notifies observers while the collection serialization gate remains held.
        /// </summary>
        private async ValueTask ClearPublishedChangeMasksAsync()
        {
            await State.ClearChangeMasksAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < m_components.Count; i++)
            {
                await m_components[i].ClearChangeMasksAsync(m_context, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds read-only collection metadata in the IA namespace.
        /// </summary>
        private PropertyState AddProperty(string name, NodeId type, Variant value, ushort ns)
        {
            var property = new PropertyState(State);
            property.Create(m_context, NodeId.Null, new QualifiedName(name, ns), new LocalizedText(name), true);
            property.DataType = type;
            property.ValueRank = ValueRanks.Scalar;
            property.Value = value;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.StatusCode = value.IsNull ? StatusCodes.BadWaitingForInitialData : StatusCodes.Good;
            State.AddChild(property);
            return property;
        }

        /// <summary>The owning address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The borrowed statistics owner.</summary>
        private readonly IIaStatisticsProvider m_provider;

        /// <summary>The fixed component order.</summary>
        private readonly ArrayOf<BaseVariableState> m_components;

        /// <summary>The selected calculation semantics.</summary>
        private readonly IaStatisticsOptions m_options;

        /// <summary>Serializes publication and notification dispatch without blocking asynchronous providers.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Serializes reset authorization, publication, and notification dispatch.</summary>
        private readonly SemaphoreSlim m_resetGate = new(1, 1);
    }
}
