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
    /// Publishes only explicitly reported forecasts on actual generated prognosis states of one machine.
    /// </summary>
    public sealed partial class MachineToolPrognosisController : IAsyncDisposable
    {
        /// <summary>
        /// Creates a direct prognosis module; referenced equipment and production must already be initialized.
        /// The public MachineTool runtime instead supplies prospective sibling inventories before a coherent commit.
        /// </summary>
        public MachineToolPrognosisController(
            IMachineryBuildContext context, MachineToolState machine, MachineToolPrognosisOptions options)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_notification = machine.Notification ?? throw Invalid("The machine has no mandatory Notification node.");
            ValidateOptions();
            m_publisher = new MachineToolInventoryPublisher(context, machine, options.PublishFinalObservationsAsync);
        }

        /// <summary>
        /// Gets the actual optional prognosis list, or null when that surface was not selected.
        /// </summary>
        public PrognosisListState? State => m_notification.Prognoses;

        /// <summary>
        /// Gets the last committed complete observation.
        /// </summary>
        public MachineToolSnapshot? Observation => m_publisher.Observation;

        /// <summary>
        /// Initializes an unpublished prognosis list without estimating times or commanding equipment.
        /// </summary>
        public ValueTask InitializeAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default) =>
            ApplyCoreAsync(snapshot, true, cancellationToken);

        /// <summary>
        /// Updates a directly composed prognosis module. Whole-machine updates use the containing runtime.
        /// </summary>
        public ValueTask ApplyAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default) =>
            ApplyCoreAsync(snapshot, false, cancellationToken);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_publisher.DisposeAsync();

        /// <summary>
        /// Gets whether this execution is inside a module-owned callback.
        /// </summary>
        internal bool IsInCallback => m_publisher.IsInCallback;

        /// <summary>
        /// Stages forecasts and their actual affected entities before any sibling module commits.
        /// </summary>
        internal ValueTask<PreparedUpdate> PrepareAsync(
            MachineToolSnapshot snapshot, bool initialize,
            Dictionary<string, MachineToolEquipmentController.ToolNodes>? tools,
            Func<string, ProductionJobState?>? resolveJob,
            Func<string, string, string, ProductionPartState?>? resolvePart,
            CancellationToken cancellationToken)
        {
            return m_publisher.PrepareAsync(snapshot, initialize,
                (batch, ct) => StageAsync(batch, snapshot, tools, resolveJob, resolvePart, ct), cancellationToken);
        }

        /// <summary>
        /// Performs a direct transaction on already initialized sibling inventories.
        /// </summary>
        private async ValueTask ApplyCoreAsync(
            MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            PreparedUpdate prepared = await PrepareAsync(snapshot, initialize, null, null, null, cancellationToken)
                .ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                prepared.ValidateCommit();
                prepared.Commit();
                await prepared.PublishAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates and resolves every forecast before invoking any companion factory.
        /// </summary>
        private async ValueTask StageAsync(
            Batch batch, MachineToolSnapshot snapshot,
            Dictionary<string, MachineToolEquipmentController.ToolNodes>? tools,
            Func<string, ProductionJobState?>? resolveJob,
            Func<string, string, string, ProductionPartState?>? resolvePart,
            CancellationToken cancellationToken)
        {
            ArrayOf<ResolvedPrognosis> observations = ValidateAndResolve(snapshot, tools, resolveJob, resolvePart);
            if (!Has(MachineToolFeatures.Prognoses))
            {
                return;
            }
            PrognosisListState list = State ?? m_context.Context.CreateInstanceOfPrognosisListType(
                m_notification, ModelName(BrowseNames.Prognoses));
            batch.Root = list;
            batch.ReportChanges = Has(MachineToolFeatures.DynamicPrognoses);
            if (State == null)
            {
                list.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                if (batch.ReportChanges)
                {
                    m_publisher.Track(list);
                }
                Add(batch, m_notification, list,
                    () => m_notification.Prognoses = list, () => m_notification.Prognoses = null);
            }
            else if (batch.Initializing && batch.ReportChanges)
            {
                m_publisher.Track(list);
            }
            var next = new Dictionary<string, PrognosisNodes>(StringComparer.Ordinal);
            for (int i = 0; i < observations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResolvedPrognosis observation = observations[i];
                MachineToolPrognosis value = observation.Observation;
                m_entries.TryGetValue(value.Key, out PrognosisNodes? prior);
                PrognosisState node;
                if (prior == null)
                {
                    QualifiedName name = PrognosisName(value);
                    MachineToolEquipmentNode<PrognosisState> created = m_options.CreatePrognosisAsync == null ?
                        new(list.FindChild(m_context.Context, name) as PrognosisState ??
                            CreateStandard(list, name, value.Kind)) :
                        await m_options.CreatePrognosisAsync(
                            m_context.Context, list, name, value, cancellationToken).ConfigureAwait(false);
                    if (created == null)
                    {
                        throw Invalid("The prognosis factory returned no node.");
                    }
                    await m_publisher.ValidateFactoryAsync(created, list, name,
                        ExpectedType(value), batch).ConfigureAwait(false);
                    node = created.State;
                    if (node.ReferenceTypeId.IsNull)
                    {
                        node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                    }
                    Add(batch, list, node, () =>
                    {
                        if (!ReferenceEquals(list.FindChild(m_context.Context, name), node))
                        {
                            list.AddChild(node);
                        }
                    }, () => list.RemoveChild(node), created.Resource);
                    if (!m_context.Context.TypeTable.IsTypeOf(node.ReferenceTypeId, ReferenceTypeIds.HasComponent))
                    {
                        throw Invalid("A prognosis must use the standard HasComponent relationship or a subtype.");
                    }
                }
                else
                {
                    ValidateShape(prior.Observation, observation);
                    node = prior.State;
                }
                StageFields(batch, node, observation, prior == null);
                next.Add(value.Key, new PrognosisNodes(node, observation));
            }
            foreach ((string key, PrognosisNodes prior) in m_entries)
            {
                if (!next.ContainsKey(key))
                {
                    m_publisher.Remove(batch, prior.State);
                }
            }
            batch.OrderChanged = Observation != null &&
                !SameOrder(Observation.Prognoses!.Entries, snapshot.Prognoses!.Entries);
            batch.CommitActions.Add(() =>
            {
                foreach (PrognosisNodes old in m_entries.Values)
                {
                    list.RemoveChild(old.State);
                }
                foreach (ResolvedPrognosis item in observations)
                {
                    PrognosisState current = next[item.Observation.Key].State;
                    list.RemoveChild(current);
                    list.AddChild(current);
                }
                m_entries = next;
                Link(m_machine, m_notification);
                Link(m_notification, list);
            });
        }

        /// <summary>
        /// Creates only concrete standard generated prognosis types.
        /// </summary>
        private PrognosisState CreateStandard(NodeState parent, QualifiedName name, MachineToolPrognosisKind kind)
        {
            ISystemContext context = m_context.Context;
            return kind switch
            {
                MachineToolPrognosisKind.Maintenance => context.CreateInstanceOfMaintenancePrognosisType(parent, name),
                MachineToolPrognosisKind.ManualActivity =>
                    context.CreateInstanceOfManualActivityPrognosisType(parent, name),
                MachineToolPrognosisKind.PartLoad => context.CreateInstanceOfPartLoadPrognosisType(parent, name),
                MachineToolPrognosisKind.PartUnload => context.CreateInstanceOfPartUnloadPrognosisType(parent, name),
                MachineToolPrognosisKind.ProcessChangeover =>
                    context.CreateInstanceOfProcessChangeoverPrognosisType(parent, name),
                MachineToolPrognosisKind.ProductionJobEnd =>
                    context.CreateInstanceOfProductionJobEndPrognosisType(parent, name),
                MachineToolPrognosisKind.ToolChange => context.CreateInstanceOfToolChangePrognosisType(parent, name),
                MachineToolPrognosisKind.ToolLoad => context.CreateInstanceOfToolLoadPrognosisType(parent, name),
                MachineToolPrognosisKind.ToolUnload => context.CreateInstanceOfToolUnloadPrognosisType(parent, name),
                MachineToolPrognosisKind.UtilityChange =>
                    context.CreateInstanceOfUtilityChangePrognosisType(parent, name),
                _ => throw Invalid("A companion prognosis requires its actual generated factory.")
            };
        }

        /// <summary>
        /// Stages the actual standard components for each kind and preserves all unrelated derived children.
        /// </summary>
        private void StageFields(Batch batch, PrognosisState node, ResolvedPrognosis resolved, bool created)
        {
            MachineToolPrognosis value = resolved.Observation;
            MachineToolSampleInfo sample = value.Sample;
            if (node.PredictedTime == null)
            {
                throw Invalid("The generated prognosis has no mandatory PredictedTime.");
            }
            Write(batch, node.PredictedTime, value.PredictedTime);
            switch (value.Kind)
            {
                case MachineToolPrognosisKind.Maintenance when node is MaintenancePrognosisState maintenance:
                    Write(batch, maintenance.Activity!, Variant.From(value.Activity), sample);
                    break;
                case MachineToolPrognosisKind.ManualActivity when node is ManualActivityPrognosisState manual:
                    Write(batch, manual.Activity!, Variant.From(value.Activity), sample);
                    break;
                case MachineToolPrognosisKind.ProcessChangeover when node is ProcessChangeoverPrognosisState changeover:
                    Write(batch, changeover.Activity!, Variant.From(value.Activity), sample);
                    Write(batch, changeover.Location!, Variant.From(value.Location), sample);
                    break;
                case MachineToolPrognosisKind.UtilityChange when node is UtilityChangePrognosisState utility:
                    Write(batch, utility.UtilityName!, Variant.From(value.UtilityName!), sample);
                    break;
                case MachineToolPrognosisKind.PartLoad when node is PartLoadPrognosisState partLoad:
                    if (created && resolved.Identifier != null)
                    {
                        partLoad.AddPartIdentifier(m_context.Context);
                    }
                    if (created && !resolved.NodeId.IsNull)
                    {
                        partLoad.AddPartNodeId(m_context.Context);
                    }
                    Write(batch, partLoad.Location!, Variant.From(value.Location), sample);
                    Write(batch, partLoad.PartName!, Variant.From(resolved.Name!), sample);
                    Optional(batch, partLoad.PartIdentifier, resolved.Identifier, sample);
                    Reference(batch, partLoad.PartNodeId, resolved.NodeId, sample);
                    break;
                case MachineToolPrognosisKind.PartUnload when node is PartUnloadPrognosisState partUnload:
                    if (created && resolved.Identifier != null)
                    {
                        partUnload.AddPartIdentifier(m_context.Context);
                    }
                    if (created && !resolved.NodeId.IsNull)
                    {
                        partUnload.AddPartNodeId(m_context.Context);
                    }
                    Write(batch, partUnload.Location!, Variant.From(value.Location), sample);
                    Write(batch, partUnload.PartName!, Variant.From(resolved.Name!), sample);
                    Optional(batch, partUnload.PartIdentifier, resolved.Identifier, sample);
                    Reference(batch, partUnload.PartNodeId, resolved.NodeId, sample);
                    break;
                case MachineToolPrognosisKind.ProductionJobEnd when node is ProductionJobEndPrognosisState job:
                    if (created && !resolved.NodeId.IsNull)
                    {
                        job.AddJobNodeId(m_context.Context);
                    }
                    Write(batch, job.SourceIdentifier!, Variant.From(resolved.Identifier!), sample);
                    Reference(batch, job.JobNodeId, resolved.NodeId, sample);
                    break;
                case MachineToolPrognosisKind.ToolChange when node is ToolChangePrognosisState toolChange:
                    if (created && resolved.Identifier != null)
                    {
                        toolChange.AddToolIdentifier(m_context.Context);
                    }
                    if (created && resolved.Name != null)
                    {
                        toolChange.AddToolName(m_context.Context);
                    }
                    if (created && !resolved.NodeId.IsNull)
                    {
                        toolChange.AddToolNodeId(m_context.Context);
                    }
                    Write(batch, toolChange.Location!, Variant.From(value.Location), sample);
                    Optional(batch, toolChange.ToolIdentifier, resolved.Identifier, sample);
                    Optional(batch, toolChange.ToolName, resolved.Name, sample);
                    Reference(batch, toolChange.ToolNodeId, resolved.NodeId, sample);
                    break;
                case MachineToolPrognosisKind.ToolLoad when node is ToolLoadPrognosisState toolLoad:
                    if (created && resolved.Identifier != null)
                    {
                        toolLoad.AddToolIdentifier(m_context.Context);
                    }
                    if (created && resolved.Name != null)
                    {
                        toolLoad.AddToolName(m_context.Context);
                    }
                    Write(batch, toolLoad.Location!, Variant.From(value.Location), sample);
                    Optional(batch, toolLoad.ToolIdentifier, resolved.Identifier, sample);
                    Optional(batch, toolLoad.ToolName, resolved.Name, sample);
                    break;
                case MachineToolPrognosisKind.ToolUnload when node is ToolUnloadPrognosisState toolUnload:
                    if (created && resolved.Identifier != null)
                    {
                        toolUnload.AddToolIdentifier(m_context.Context);
                    }
                    if (created && resolved.Name != null)
                    {
                        toolUnload.AddToolName(m_context.Context);
                    }
                    if (created && !resolved.NodeId.IsNull)
                    {
                        toolUnload.AddToolNodeId(m_context.Context);
                    }
                    Write(batch, toolUnload.Location!, Variant.From(value.Location), sample);
                    Optional(batch, toolUnload.ToolIdentifier, resolved.Identifier, sample);
                    Optional(batch, toolUnload.ToolName, resolved.Name, sample);
                    Reference(batch, toolUnload.ToolNodeId, resolved.NodeId, sample);
                    break;
                case MachineToolPrognosisKind.Companion:
                    break;
                default:
                    throw Invalid("The factory did not return the selected actual generated prognosis class.");
            }
        }

        /// <summary>
        /// Writes an optional string only when its selected node and observation both exist.
        /// </summary>
        private static void Optional(
            Batch batch, BaseVariableState? node, string? value, MachineToolSampleInfo sample)
        {
            if ((node == null) != (value == null))
            {
                throw Invalid("A selected prognosis component has no provider observation.");
            }
            if (node != null)
            {
                Write(batch, node, Variant.From(value!), sample);
            }
        }

        /// <summary>
        /// Writes the actual resolved entity identity and never substitutes a null NodeId with Good quality.
        /// </summary>
        private static void Reference(
            Batch batch, BaseVariableState? node, NodeId target, MachineToolSampleInfo sample)
        {
            if ((node == null) != target.IsNull)
            {
                throw Invalid("The affected-entity component must match the actually modeled entity.");
            }
            if (node != null)
            {
                Write(batch, node, Variant.From(target), sample);
            }
        }

        /// <summary>
        /// Resolves the canonical generated base type required by a prognosis.
        /// </summary>
        private NodeId ExpectedType(MachineToolPrognosis value)
        {
            ExpandedNodeId type = value.Kind switch
            {
                MachineToolPrognosisKind.Maintenance => ObjectTypeIds.MaintenancePrognosisType,
                MachineToolPrognosisKind.ManualActivity => ObjectTypeIds.ManualActivityPrognosisType,
                MachineToolPrognosisKind.PartLoad => ObjectTypeIds.PartLoadPrognosisType,
                MachineToolPrognosisKind.PartUnload => ObjectTypeIds.PartUnloadPrognosisType,
                MachineToolPrognosisKind.ProcessChangeover => ObjectTypeIds.ProcessChangeoverPrognosisType,
                MachineToolPrognosisKind.ProductionJobEnd => ObjectTypeIds.ProductionJobEndPrognosisType,
                MachineToolPrognosisKind.ToolChange => ObjectTypeIds.ToolChangePrognosisType,
                MachineToolPrognosisKind.ToolLoad => ObjectTypeIds.ToolLoadPrognosisType,
                MachineToolPrognosisKind.ToolUnload => ObjectTypeIds.ToolUnloadPrognosisType,
                MachineToolPrognosisKind.UtilityChange => ObjectTypeIds.UtilityChangePrognosisType,
                _ => value.CompanionTypeId
            };
            return ExpandedNodeId.ToNodeId(type, m_context.Context.NamespaceUris);
        }

        /// <summary>
        /// Qualifies a standard MachineTool declaration.
        /// </summary>
        private QualifiedName ModelName(string name) =>
            new(name, (ushort)m_context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool));

        /// <summary>
        /// Qualifies a per-instance provider key.
        /// </summary>
        private QualifiedName InstanceName(string name) => new(name, m_context.InstanceNamespaceIndex);

        /// <summary>
        /// Tests an explicit instance capability.
        /// </summary>
        private bool Has(MachineToolFeatures feature) => (m_options.Features & feature) != 0;

        /// <summary>
        /// Stores the actual generated node and its resolved coherent observation.
        /// </summary>
        private sealed record PrognosisNodes(PrognosisState State, ResolvedPrognosis Observation);

        /// <summary>
        /// A forecast whose optional fields refer to actual same-instance prospective entities.
        /// </summary>
        private sealed record ResolvedPrognosis(
            MachineToolPrognosis Observation, NodeId NodeId, string? Identifier, string? Name);

        /// <summary>The existing namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The actual generated machine subtype.</summary>
        private readonly MachineToolState m_machine;

        /// <summary>The actual generated notification node.</summary>
        private readonly NotificationState m_notification;

        /// <summary>The explicit per-instance capabilities and bounds.</summary>
        private readonly MachineToolPrognosisOptions m_options;

        /// <summary>The shared equipment transaction and resource-lifetime mechanism.</summary>
        private readonly MachineToolInventoryPublisher m_publisher;

        /// <summary>The explicitly configured forecast kinds.</summary>
        private readonly HashSet<MachineToolPrognosisKind> m_supported = [];

        /// <summary>The committed forecasts, never shared with another machine.</summary>
        private Dictionary<string, PrognosisNodes> m_entries = new(StringComparer.Ordinal);
    }
}
