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
using Opc.Ua.Machinery.Server;
using static Opc.Ua.MachineTool.Server.MachineToolInventoryPublisher;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Publishes bounded equipment observations on the actual generated MachineTool equipment tree.
    /// </summary>
    public sealed partial class MachineToolEquipmentController : IAsyncDisposable
    {
        /// <summary>
        /// Creates a direct equipment module. The containing owner initializes it before registering the machine.
        /// </summary>
        public MachineToolEquipmentController(
            IMachineryBuildContext context, MachineToolState machine, MachineToolEquipmentOptions options)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            State = machine.Equipment ?? throw Invalid("The generated machine has no mandatory Equipment.");
            ValidateOptions();
            m_publisher = new MachineToolInventoryPublisher(context, machine, options.PublishFinalObservationsAsync);
        }

        /// <summary>
        /// Gets the actual generated equipment object, retaining a derived GMS equipment node.
        /// </summary>
        public EquipmentState State { get; }

        /// <summary>
        /// Gets the last committed full provider observation.
        /// </summary>
        public MachineToolSnapshot? Observation => m_publisher.Observation;

        /// <summary>
        /// Initializes an unpublished generated equipment tree.
        /// </summary>
        public ValueTask InitializeAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return ApplyCoreAsync(snapshot, true, cancellationToken);
        }

        /// <summary>
        /// Updates the inventory directly. Use the containing runtime's RefreshAsync for a coherent whole machine.
        /// </summary>
        public ValueTask ApplyAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return ApplyCoreAsync(snapshot, false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_publisher.DisposeAsync();

        /// <summary>
        /// Gets whether this execution is a staging or publication callback owned by the module.
        /// </summary>
        internal bool IsInCallback => m_publisher.IsInCallback;

        /// <summary>
        /// Stages all equipment changes before the containing runtime commits any other observation.
        /// </summary>
        internal ValueTask<PreparedUpdate> PrepareAsync(
            MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            return m_publisher.PrepareAsync(snapshot, initialize,
                (batch, ct) => StageAsync(batch, snapshot, ct), cancellationToken);
        }

        /// <summary>
        /// Gets the actual prospective tools for sibling prognosis validation, including newly created subtypes.
        /// </summary>
        internal Dictionary<string, ToolNodes> GetPreparedTools(PreparedUpdate prepared) =>
            prepared.Batch.Tools ?? m_tools;

        /// <summary>
        /// Runs a direct transaction using the same staging and lifecycle as the containing runtime.
        /// </summary>
        private async ValueTask ApplyCoreAsync(
            MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            PreparedUpdate prepared = await PrepareAsync(snapshot, initialize, cancellationToken).ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                prepared.ValidateCommit();
                prepared.Commit();
                await prepared.PublishAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds an entirely reversible inventory and only then accepts its index at commit.
        /// </summary>
        private async ValueTask StageAsync(
            Batch batch, MachineToolSnapshot snapshot, CancellationToken cancellationToken)
        {
            ValidateObservation(snapshot);
            if (!Has(MachineToolFeatures.Tools))
            {
                return;
            }
            ToolListState list = State.Tools ?? m_context.Context.CreateInstanceOfToolListType(
                State, ModelName(BrowseNames.Tools));
            batch.Root = list;
            batch.ReportChanges = Has(MachineToolFeatures.DynamicTools);
            if (State.Tools == null)
            {
                list.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                if (batch.ReportChanges)
                {
                    m_publisher.Track(list);
                }
                Add(batch, State, list, () => State.Tools = list, () => State.Tools = null);
            }
            else if (batch.Initializing && batch.ReportChanges)
            {
                m_publisher.Track(list);
            }
            var next = new Dictionary<string, ToolNodes>(StringComparer.Ordinal);
            ArrayOf<MachineToolTool> observations = snapshot.Equipment!.Tools;
            await StageToolsAsync(batch, list, observations, next, cancellationToken).ConfigureAwait(false);
            foreach ((string key, ToolNodes previous) in m_tools)
            {
                if (!next.ContainsKey(key) && previous.Parent is not MultiToolState)
                {
                    m_publisher.Remove(batch, previous.State);
                }
                else if (!next.ContainsKey(key) && previous.Parent is MultiToolState multi &&
                    next.ContainsKey(multi.BrowseName.Name!))
                {
                    m_publisher.Remove(batch, previous.State);
                }
            }
            batch.OrderChanged = Observation != null &&
                !SameOrder(Observation.Equipment!.Tools, observations);
            batch.CommitActions.Add(() =>
            {
                Reorder(list, observations, next);
                m_tools = next;
                Link(m_machine, State);
                if (batch.ReportChanges)
                {
                    Link(State, list);
                }
            });
            batch.Tools = next;
        }

        /// <summary>
        /// Stages single tools and the actual constituent tools of a multitool.
        /// </summary>
        private async ValueTask StageToolsAsync(
            Batch batch, BaseObjectState parent, ArrayOf<MachineToolTool> observations,
            Dictionary<string, ToolNodes> next, CancellationToken cancellationToken)
        {
            for (int index = 0; index < observations.Count; index++)
            {
                MachineToolTool observation = observations[index];
                cancellationToken.ThrowIfCancellationRequested();
                m_tools.TryGetValue(observation.Key, out ToolNodes? prior);
                BaseToolState node;
                if (prior == null)
                {
                    QualifiedName name = InstanceName(observation.Key);
                    node = await CreateToolAsync(batch, parent, name, observation, cancellationToken)
                        .ConfigureAwait(false);
                    SelectMembers(node, observation);
                }
                else
                {
                    node = prior.State;
                    if (!ReferenceEquals(prior.Parent, parent))
                    {
                        throw Invalid("A tool's parent cannot change while retaining its slot key.");
                    }
                    ValidateShape(prior.Observation, observation);
                }
                var current = new ToolNodes(node, parent, observation);
                next.Add(observation.Key, current);
                StageValues(batch, node, observation);
                if (node is ToolState single)
                {
                    StageLife(batch, single, observation, prior == null);
                }
                if (node is MultiToolState multi)
                {
                    await StageToolsAsync(batch, multi, observation.Tools, next, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Invokes only the factory appropriate to the requested generated base type.
        /// </summary>
        private async ValueTask<BaseToolState> CreateToolAsync(
            Batch batch, BaseObjectState parent, QualifiedName name, MachineToolTool observation,
            CancellationToken cancellationToken)
        {
            BaseToolState? existing = parent.FindChild(m_context.Context, name) as BaseToolState;
            switch (observation.Kind)
            {
                case MachineToolToolKind.Tool:
                    MachineToolEquipmentNode<ToolState> tool = m_options.CreateToolAsync == null ?
                        new(existing as ToolState ?? m_context.Context.CreateInstanceOfToolType(parent, name)) :
                        await m_options.CreateToolAsync(m_context.Context, parent, name, observation, cancellationToken)
                            .ConfigureAwait(false);
                    return await AcceptAsync(tool, ObjectTypeIds.ToolType).ConfigureAwait(false);
                case MachineToolToolKind.MultiTool:
                    MachineToolEquipmentNode<MultiToolState> multi = m_options.CreateMultiToolAsync == null ?
                        new(existing as MultiToolState ??
                            m_context.Context.CreateInstanceOfMultiToolType(parent, name)) :
                        await m_options.CreateMultiToolAsync(
                            m_context.Context, parent, name, observation, cancellationToken).ConfigureAwait(false);
                    return await AcceptAsync(multi, ObjectTypeIds.MultiToolType).ConfigureAwait(false);
                default:
                    MachineToolEquipmentNode<BaseToolState> companion = await m_options.CreateCompanionToolAsync!(
                        m_context.Context, parent, name, observation, cancellationToken).ConfigureAwait(false);
                    return await AcceptAsync(companion, observation.CompanionTypeId).ConfigureAwait(false);
            }

            async ValueTask<BaseToolState> AcceptAsync<TNode>(
                MachineToolEquipmentNode<TNode> created, ExpandedNodeId type) where TNode : BaseToolState
            {
                if (created == null)
                {
                    throw Invalid("The equipment factory returned no node.");
                }
                await m_publisher.ValidateFactoryAsync(created, parent, name,
                    ExpandedNodeId.ToNodeId(type, m_context.Context.NamespaceUris), batch).ConfigureAwait(false);
                BaseToolState result = created.State;
                if (result.ReferenceTypeId.IsNull)
                {
                    result.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                }
                Add(batch, parent, result, () =>
                {
                    if (!ReferenceEquals(parent.FindChild(m_context.Context, name), result))
                    {
                        parent.AddChild(result);
                    }
                }, () => parent.RemoveChild(result), created.Resource);
                if (!m_context.Context.TypeTable.IsTypeOf(result.ReferenceTypeId, ReferenceTypeIds.HasComponent))
                {
                    throw Invalid("A tool must use the standard HasComponent relationship or a subtype.");
                }
                if (observation.Kind == MachineToolToolKind.Companion && result is ToolState or MultiToolState)
                {
                    throw Invalid("A concrete ToolType or MultiToolType must use its corresponding typed factory.");
                }
                return result;
            }
        }

        /// <summary>
        /// Selects optional generated declarations only on a newly created unpublished tool.
        /// </summary>
        private void SelectMembers(BaseToolState node, MachineToolTool observation)
        {
            if (observation.Identifier != null)
            {
                node.AddIdentifier(m_context.Context);
            }
            if (observation.Name != null)
            {
                node.AddName(m_context.Context);
            }
            if (observation.Location != null)
            {
                node.AddLocation(m_context.Context);
            }
            if ((node.Identifier != null) != (observation.Identifier != null) ||
                (node.Name != null) != (observation.Name != null) ||
                (node.Location != null) != (observation.Location != null))
            {
                throw Invalid("The factory selected a base-tool member without its provider observation.");
            }
            if (node is ToolState tool)
            {
                MachineToolToolControl control = observation.Control!;
                if (control.ControlIdentifier2.HasValue)
                {
                    tool.AddControlIdentifier2(m_context.Context);
                }
                if (!control.LastUsage.IsNull)
                {
                    tool.AddLastUsage(m_context.Context);
                }
                if (control.PlannedForOperating.HasValue)
                {
                    tool.AddPlannedForOperating(m_context.Context);
                }
                if (observation.Life.Count > 0)
                {
                    tool.AddToolLife(m_context.Context);
                }
                if ((tool.ControlIdentifier2 != null) != control.ControlIdentifier2.HasValue ||
                    (tool.LastUsage != null) != !control.LastUsage.IsNull ||
                    (tool.PlannedForOperating != null) != control.PlannedForOperating.HasValue ||
                    (tool.ToolLife != null) != (observation.Life.Count > 0))
                {
                    throw Invalid("Every selected ToolType member needs an explicit provider observation.");
                }
            }
        }

        /// <summary>
        /// Stages base identification, actual standard location children and controller values.
        /// </summary>
        private void StageValues(Batch batch, BaseToolState node, MachineToolTool observation)
        {
            MachineToolSampleInfo sample = observation.Sample;
            if (node.Identifier != null)
            {
                Write(batch, node.Identifier, Variant.From(observation.Identifier!), sample);
            }
            if (node.Name != null)
            {
                Write(batch, node.Name, Variant.From(observation.Name!), sample);
            }
            if (node.Location != null)
            {
                Write(batch, Child(node.Location, BrowseNames.Name), Variant.From(observation.Location!.Name), sample);
                Write(batch, Child(node.Location, BrowseNames.PlaceNumber),
                    Variant.From(observation.Location.PlaceNumber), sample);
            }
            batch.CommitActions.Add(() => node.DisplayName = new LocalizedText(observation.Name ?? observation.Key));
            if (node is ToolState tool)
            {
                MachineToolToolControl control = observation.Control!;
                Write(batch, tool.ControlIdentifier1!, Variant.From(control.ControlIdentifier1), sample);
                Write(batch, tool.ControlIdentifierInterpretation!, Variant.From(control.Interpretation), sample);
                Write(batch, tool.Locked!, Variant.From(control.Locked), sample);
                Write(batch, Child(tool.Locked!, BrowseNames.ReasonForLocking),
                    Variant.From(control.ReasonForLocking), sample);
                if (tool.ControlIdentifier2 != null)
                {
                    Write(batch, tool.ControlIdentifier2, Variant.From(control.ControlIdentifier2!.Value), sample);
                }
                if (tool.LastUsage != null)
                {
                    Write(batch, tool.LastUsage, control.LastUsage);
                }
                if (tool.PlannedForOperating != null)
                {
                    Write(batch, tool.PlannedForOperating, Variant.From(control.PlannedForOperating!.Value), sample);
                }
            }
        }

        /// <summary>
        /// Binds actual ToolLife variables, adopting preselected derived declarations such as GMS Qualified.
        /// </summary>
        private void StageLife(Batch batch, ToolState tool, MachineToolTool observation, bool created)
        {
            var expected = new HashSet<QualifiedName>();
            foreach (MachineToolToolLife value in observation.Life)
            {
                QualifiedName name = LifeName(value);
                expected.Add(name);
                ToolLifeState? life = tool.ToolLife!.FindChild(m_context.Context, name) as ToolLifeState;
                if (life == null)
                {
                    if (!created)
                    {
                        throw Invalid("The configured life measurement is absent from the existing tool.");
                    }
                    if (value.NamespaceUri != null)
                    {
                        life = CreateDeclaredLife(tool, name);
                    }
                    else
                    {
                        life = m_context.Context.CreateInstanceOfToolLifeType(tool.ToolLife, name);
                        life.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                        tool.ToolLife.AddChild(life);
                    }
                }
                if (tool.ToolLife != null)
                {
                    var children = new List<BaseInstanceState>();
                    tool.ToolLife.GetChildren(m_context.Context, children);
                    foreach (BaseInstanceState child in children)
                    {
                        if (child is ToolLifeState && !expected.Contains(child.BrowseName))
                        {
                            throw Invalid("A selected tool-life entry has no explicit provider sample.");
                        }
                    }
                }
                if (created)
                {
                    if (!value.StartValue.IsNull)
                    {
                        life.AddStartValue(m_context.Context);
                    }
                    if (!value.WarningValue.IsNull)
                    {
                        life.AddWarningValue(m_context.Context);
                    }
                    if (!value.LimitValue.IsNull)
                    {
                        life.AddLimitValue(m_context.Context);
                    }
                }
                if ((life.StartValue != null) != !value.StartValue.IsNull ||
                    (life.WarningValue != null) != !value.WarningValue.IsNull ||
                    (life.LimitValue != null) != !value.LimitValue.IsNull)
                {
                    throw Invalid("A tool-life optional member has no matching provider sample.");
                }
                if (!value.Value.WrappedValue.IsNull && !m_context.Context.TypeTable.IsTypeOf(
                    new NodeId((uint)value.Value.WrappedValue.TypeInfo.BuiltInType), life.DataType))
                {
                    throw Invalid("The numeric representation does not match the derived tool-life declaration.");
                }
                Write(batch, life, value.Value);
                Write(batch, life.EngineeringUnits!, Variant.FromStructure(value.EngineeringUnits!), value.Sample);
                Write(batch, life.Indication!, Variant.From(value.Indication), value.Sample);
                Write(batch, life.IsCountingUp!, Variant.From(value.IsCountingUp), value.Sample);
                if (life.StartValue != null)
                {
                    Write(batch, life.StartValue, value.StartValue);
                }
                if (life.WarningValue != null)
                {
                    Write(batch, life.WarningValue, value.WarningValue);
                }
                if (life.LimitValue != null)
                {
                    Write(batch, life.LimitValue, value.LimitValue);
                }
            }
        }

        /// <summary>
        /// Instantiates an actual loaded companion declaration, retaining its generated generic type and children.
        /// Nested generated factories are assembly-private, so the shared clone/rebase APIs preserve the declaration.
        /// </summary>
        private ToolLifeState CreateDeclaredLife(ToolState tool, QualifiedName name)
        {
            NodeId typeId = tool.TypeDefinitionId;
            for (int depth = 0; depth < 64 && !typeId.IsNull; depth++)
            {
                NodeState? type = m_context.Manager.FindPredefinedNode(typeId);
                NodeState? container = type?.FindChild(m_context.Context, ModelName(BrowseNames.ToolLife));
                if (container?.FindChild(m_context.Context, name) is ToolLifeState declaration)
                {
                    var instance = (ToolLifeState)declaration.Clone();
                    tool.ToolLife!.AddChild(instance);
                    NodeId previous = m_context.Context.AssignInstanceNodeId(instance);
                    m_context.Context.AssignInstanceChildNodeIds(instance, previous);
                    return instance;
                }
                typeId = m_context.Context.TypeTable.FindSuperType(typeId);
            }
            throw Invalid("The selected tool subtype does not declare this companion tool-life variable.");
        }

        /// <summary>
        /// Retains provider order using ordinary HasComponent references, without inventing NumberInList.
        /// </summary>
        private void Reorder(
            BaseObjectState parent, ArrayOf<MachineToolTool> order, Dictionary<string, ToolNodes> next)
        {
            foreach (ToolNodes old in m_tools.Values)
            {
                if (ReferenceEquals(old.Parent, parent))
                {
                    parent.RemoveChild(old.State);
                }
            }
            foreach (MachineToolTool item in order)
            {
                BaseToolState node = next[item.Key].State;
                parent.RemoveChild(node);
                parent.AddChild(node);
                if (node is MultiToolState multi)
                {
                    Reorder(multi, item.Tools, next);
                }
            }
        }

        /// <summary>
        /// Resolves a mandatory generated MachineTool-qualified scalar.
        /// </summary>
        private BaseVariableState Child(NodeState parent, string name) =>
            parent.FindChild(m_context.Context, ModelName(name)) as BaseVariableState ??
                throw Invalid($"A generated equipment node has no mandatory {name}.");

        /// <summary>
        /// Qualifies a standard MachineTool browse name.
        /// </summary>
        private QualifiedName ModelName(string name) =>
            new(name, (ushort)m_context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool));

        /// <summary>
        /// Qualifies an application slot key.
        /// </summary>
        private QualifiedName InstanceName(string name) => new(name, m_context.InstanceNamespaceIndex);

        /// <summary>
        /// Resolves a life entry's explicitly selected namespace.
        /// </summary>
        private QualifiedName LifeName(MachineToolToolLife life)
        {
            int index = life.NamespaceUri == null ? m_context.InstanceNamespaceIndex :
                m_context.Context.NamespaceUris.GetIndex(life.NamespaceUri);
            if (index < 0)
            {
                throw Invalid("The tool-life companion namespace is not loaded.");
            }
            return new QualifiedName(life.Name, (ushort)index);
        }

        /// <summary>
        /// Tests an explicitly selected equipment capability.
        /// </summary>
        private bool Has(MachineToolFeatures feature) => (m_options.Features & feature) != 0;

        /// <summary>
        /// An actual generated tool and the immutable observation which selected its schema.
        /// </summary>
        internal sealed record ToolNodes(BaseToolState State, BaseObjectState Parent, MachineToolTool Observation);

        /// <summary>The shared namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The actual containing machine subtype.</summary>
        private readonly MachineToolState m_machine;

        /// <summary>The bounded per-instance configuration.</summary>
        private readonly MachineToolEquipmentOptions m_options;

        /// <summary>The reversible shared inventory publication mechanism.</summary>
        private readonly MachineToolInventoryPublisher m_publisher;

        /// <summary>The committed tools indexed by stable application slot key.</summary>
        private Dictionary<string, ToolNodes> m_tools = new(StringComparer.Ordinal);
    }
}
