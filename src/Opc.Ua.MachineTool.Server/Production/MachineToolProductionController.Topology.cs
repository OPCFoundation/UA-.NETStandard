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
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolProductionController
    {
        /// <summary>
        /// Creates only missing generated objects and stages writes to existing objects.
        /// </summary>
        private async ValueTask StageTopologyAsync(
            ProductionBatch batch, MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            if (Has(MachineToolFeatures.Production))
            {
                ProductionJobListState plan = State.ProductionPlan ??
                    m_context.Context.CreateInstanceOfProductionJobListType(
                        State, ModelName(BrowseNames.ProductionPlan));
                if (State.ProductionPlan == null)
                {
                    TrackList(plan);
                    Add(batch, State, plan, () => State.ProductionPlan = plan, () => State.ProductionPlan = null);
                }
                if (m_observation != null && OrderChanged(
                    m_observation.Production.Jobs, snapshot.Production.Jobs, static job => Key(job)))
                {
                    batch.ChangedLists.Add(plan);
                }
                for (int i = 0; i < snapshot.Production.Jobs.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MachineToolProductionJob job = snapshot.Production.Jobs[i];
                    string key = Key(job);
                    m_jobs.TryGetValue(key, out JobNodes? prior);
                    ProductionJobState node;
                    if (prior == null)
                    {
                        MachineToolProductionNode<ProductionJobState> created = m_options.CreateJobAsync == null
                            ? new(m_context.Context.CreateInstanceOfProductionJobType(plan, InstanceName(key)))
                            : await m_options.CreateJobAsync(
                                m_context.Context, plan, InstanceName(key), cancellationToken)
                                .ConfigureAwait(false);
                        node = created?.State ?? throw Invalid("The job factory returned no generated job.");
                        await ValidateFactoryNodeAsync(created, plan, key, ObjectTypeIds.ProductionJobType)
                            .ConfigureAwait(false);
                        Add(batch, plan, node,
                            () => plan.AddChild(node), () => plan.RemoveChild(node), created.Resource);
                        node.ReferenceTypeId = ReferenceTypeIds.HasOrderedComponent;
                        node.AddCustomerOrderIdentifier(m_context.Context);
                        node.AddOrderIdentifier(m_context.Context);
                        if (Has(MachineToolFeatures.ProductionParts))
                        {
                            node.AddPartsCompleted(m_context.Context);
                            node.AddPartsGood(m_context.Context);
                        }
                        PrepareState(node.State!, true);
                        TrackList(node.ProductionPrograms!);
                    }
                    else
                    {
                        node = prior.State;
                    }
                    var current = new JobNodes(node, job);
                    batch.Jobs.Add(key, current);
                    StageJob(batch.Writes, node, prior?.Observation, job, (ushort)i);
                    StagePrograms(batch, current, prior);
                    await StagePartSetsAsync(batch, current, prior, cancellationToken).ConfigureAwait(false);
                    StageInterruptions(batch, current, prior);
                }
                foreach ((string key, JobNodes prior) in m_jobs)
                {
                    if (!batch.Jobs.ContainsKey(key))
                    {
                        Remove(batch, prior.State);
                    }
                }
                if (batch.Removals.Count > 0)
                {
                    var removed = new NodeState[batch.Removals.Count];
                    for (int i = 0; i < removed.Length; i++)
                    {
                        removed[i] = batch.Removals[i].Node;
                    }
                    if (m_options.PublishFinalObservationsAsync == null)
                    {
                        throw Invalid("Removing production nodes requires a final-observation delivery barrier.");
                    }
                    await m_options.PublishFinalObservationsAsync(new ArrayOf<NodeState>(removed), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            if (Has(MachineToolFeatures.ProductionStatistics))
            {
                ProductionStatisticsState statistics = State.Statistics ??
                    m_context.Context.CreateInstanceOfProductionStatisticsType(
                        State, ModelName(BrowseNames.Statistics));
                if (State.Statistics == null)
                {
                    statistics.AddPartsProducedInLifetime(m_context.Context);
                    Add(batch, State, statistics, () => State.Statistics = statistics, () => State.Statistics = null);
                }
                MachineToolProductionStatistics observation = snapshot.Production.Statistics!;
                Write(batch.Writes, statistics.PartsProducedInLifetime!,
                    Variant.From(observation.PartsProducedInLifetime), observation.Sample);
            }
        }

        /// <summary>
        /// Stages the job snapshot and its standard CurrentState and LastTransition.
        /// </summary>
        private void StageJob(
            List<VariableWrite> writes, ProductionJobState node,
            MachineToolProductionJob? prior, MachineToolProductionJob job, ushort index)
        {
            MachineToolSampleInfo sample = job.Sample;
            Write(writes, node.Identifier!, Variant.From(job.Identifier), sample);
            Write(writes, node.NumberInList!, Variant.From(index), sample);
            Write(writes, node.CustomerOrderIdentifier!, Variant.From(job.CustomerOrderIdentifier), sample);
            Write(writes, node.OrderIdentifier!, Variant.From(job.OrderIdentifier), sample);
            Write(writes, node.RunsCompleted!, Variant.From(job.RunsCompleted), sample);
            Write(writes, node.RunsPlanned!, Variant.From(job.RunsPlanned), sample);
            Write(writes, Child(node.RunsPlanned!, BrowseNames.IsValid), Variant.From(job.RunsPlannedIsValid), sample);
            if (Has(MachineToolFeatures.ProductionParts))
            {
                Write(writes, node.PartsCompleted!, Variant.From(job.PartsCompleted), sample);
                Write(writes, node.PartsGood!, Variant.From(job.PartsGood), sample);
            }
            StageState(writes, node.State!, prior?.Phase, prior?.TransitionSequence ?? 0,
                job.Phase, job.TransitionSequence, sample);
        }

        /// <summary>
        /// Maintains the call order using HasOrderedComponent and sequential UInt16 indices.
        /// </summary>
        private void StagePrograms(ProductionBatch batch, JobNodes current, JobNodes? prior)
        {
            BaseObjectState parent = current.State.ProductionPrograms!;
            if (prior != null && OrderChanged(prior.Observation.Programs, current.Observation.Programs,
                static program => Key(program)))
            {
                batch.ChangedLists.Add(parent);
            }
            for (int i = 0; i < current.Observation.Programs.Count; i++)
            {
                MachineToolProgram program = current.Observation.Programs[i];
                string key = Key(program);
                ProgramNodes? old = null;
                prior?.Programs.TryGetValue(key, out old);
                ProductionProgramState node = old?.State ??
                    m_context.Context.CreateInstanceOfProductionProgramType(parent, InstanceName(key));
                if (old == null)
                {
                    node.ReferenceTypeId = ReferenceTypeIds.HasOrderedComponent;
                    node.AddState(m_context.Context);
                    PrepareState(node.State!, true);
                    Add(batch, parent, node, () => parent.AddChild(node), () => parent.RemoveChild(node));
                }
                current.Programs.Add(key, new ProgramNodes(node, program));
                Write(batch.Writes, node.Name!, Variant.From(program.Name), program.Sample);
                Write(batch.Writes, node.NumberInList!, Variant.From((ushort)i), program.Sample);
                StageState(batch.Writes, node.State!, old?.Observation.Phase, old?.Observation.TransitionSequence ?? 0,
                    program.Phase, program.TransitionSequence, program.Sample);
            }
            if (prior != null)
            {
                foreach ((string key, ProgramNodes old) in prior.Programs)
                {
                    if (!current.Programs.ContainsKey(key))
                    {
                        Remove(batch, old.State);
                    }
                }
            }
        }

        /// <summary>
        /// Stages optional part sets and generated part subtypes without adding nonstandard ordering properties.
        /// </summary>
        private async ValueTask StagePartSetsAsync(
            ProductionBatch batch, JobNodes current, JobNodes? prior, CancellationToken cancellationToken)
        {
            if (current.Observation.PartSets.Count == 0)
            {
                if (current.State.PartSets != null)
                {
                    Remove(batch, current.State.PartSets);
                }
                return;
            }
            BaseObjectState parent = current.State.PartSets ??
                m_context.Context.CreateInstanceOfBaseObjectType(current.State, ModelName(BrowseNames.PartSets));
            if (current.State.PartSets == null)
            {
                Add(batch, current.State, parent, () => current.State.PartSets = parent,
                    () => current.State.PartSets = null);
            }
            for (int setIndex = 0; setIndex < current.Observation.PartSets.Count; setIndex++)
            {
                MachineToolProductionPartSet set = current.Observation.PartSets[setIndex];
                PartSetNodes? old = null;
                prior?.PartSets.TryGetValue(set.Key, out old);
                ProductionPartSetState node = old?.State ??
                    m_context.Context.CreateInstanceOfProductionPartSetType(parent, InstanceName(set.Key));
                if (old == null)
                {
                    node.AddName(m_context.Context);
                    Add(batch, parent, node, () => parent.AddChild(node), () => parent.RemoveChild(node));
                }
                var next = new PartSetNodes(node, set);
                current.PartSets.Add(set.Key, next);
                Write(batch.Writes, node.Name!, Variant.From(set.Name), set.Sample);
                Write(batch.Writes, node.ContainsMixedParts!, Variant.From(set.ContainsMixedParts), set.Sample);
                Write(batch.Writes, node.PartsPlannedPerRun!, Variant.From(set.PartsPlannedPerRun), set.Sample);
                Write(batch.Writes, node.PartsCompletedPerRun!, Variant.From(set.PartsCompletedPerRun), set.Sample);
                await StagePartsAsync(batch, next, old, cancellationToken).ConfigureAwait(false);
            }
            if (prior != null)
            {
                foreach ((string key, PartSetNodes old) in prior.PartSets)
                {
                    if (!current.PartSets.ContainsKey(key))
                    {
                        Remove(batch, old.State);
                    }
                }
            }
        }

        /// <summary>
        /// Stages parts, preserving typed-factory ownership and current-run identity.
        /// </summary>
        private async ValueTask StagePartsAsync(
            ProductionBatch batch, PartSetNodes current, PartSetNodes? prior, CancellationToken cancellationToken)
        {
            if (current.Observation.Parts.Count == 0)
            {
                if (current.State.PartsPerRun != null)
                {
                    Remove(batch, current.State.PartsPerRun);
                }
                return;
            }
            BaseObjectState parent = current.State.PartsPerRun ??
                m_context.Context.CreateInstanceOfBaseObjectType(current.State, ModelName(BrowseNames.PartsPerRun));
            if (current.State.PartsPerRun == null)
            {
                Add(batch, current.State, parent, () => current.State.PartsPerRun = parent,
                    () => current.State.PartsPerRun = null);
            }
            for (int partIndex = 0; partIndex < current.Observation.Parts.Count; partIndex++)
            {
                MachineToolProductionPart part = current.Observation.Parts[partIndex];
                PartNodes? old = null;
                prior?.Parts.TryGetValue(part.Key, out old);
                ProductionPartState node;
                if (old == null)
                {
                    MachineToolProductionNode<ProductionPartState> created = m_options.CreatePartAsync == null
                        ? new(m_context.Context.CreateInstanceOfProductionPartType(parent, InstanceName(part.Key)))
                        : await m_options.CreatePartAsync(
                            m_context.Context, parent, InstanceName(part.Key), cancellationToken)
                            .ConfigureAwait(false);
                    node = created?.State ?? throw Invalid("The part factory returned no generated part.");
                    await ValidateFactoryNodeAsync(created, parent, part.Key, ObjectTypeIds.ProductionPartType)
                        .ConfigureAwait(false);
                    Add(batch, parent, node,
                        () => parent.AddChild(node), () => parent.RemoveChild(node), created.Resource);
                    node.AddIdentifier(m_context.Context);
                    node.AddCustomerOrderIdentifier(m_context.Context);
                    node.AddState(m_context.Context);
                    PrepareState(node.State!, true);
                }
                else
                {
                    node = old.State;
                }
                current.Parts.Add(part.Key, new PartNodes(node, part));
                Write(batch.Writes, node.Name!, Variant.From(part.Name), part.Sample);
                Write(batch.Writes, node.Identifier!, Variant.From(part.Identifier), part.Sample);
                Write(batch.Writes, node.CustomerOrderIdentifier!,
                    Variant.From(part.CustomerOrderIdentifier), part.Sample);
                Write(batch.Writes, node.PartQuality!, Variant.From(part.Quality), part.Sample);
                Write(batch.Writes, node.ProcessIrregularity!, Variant.From(part.Irregularity), part.Sample);
                StageState(batch.Writes, node.State!, old?.Observation.Phase, old?.Observation.TransitionSequence ?? 0,
                    part.Phase, part.TransitionSequence, part.Sample);
            }
            if (prior != null)
            {
                foreach ((string key, PartNodes old) in prior.Parts)
                {
                    if (!current.Parts.ContainsKey(key))
                    {
                        Remove(batch, old.State);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves ActiveProgram against the actual modeled job and call-order index.
        /// </summary>
        private void StageActiveProgram(ProductionBatch batch, MachineToolSnapshot snapshot)
        {
            ProductionActiveProgramState active = State.ActiveProgram!;
            ushort index = 0;
            NodeId jobId = NodeId.Null;
            foreach (JobNodes job in batch.Jobs.Values)
            {
                if (job.Observation.Identifier == snapshot.ActiveProgram.JobIdentifier)
                {
                    jobId = job.State.NodeId;
                    for (int i = 0; i < job.Observation.Programs.Count; i++)
                    {
                        if (Key(job.Observation.Programs[i]) == Key(snapshot.ActiveProgram))
                        {
                            index = (ushort)i;
                            batch.SelectedProgram = job.Programs[Key(snapshot.ActiveProgram)].State;
                            break;
                        }
                    }
                    break;
                }
            }
            if (Has(MachineToolFeatures.Production) && m_observation == null)
            {
                active.AddJobNodeId(m_context.Context);
                active.AddJobIdentifier(m_context.Context);
            }
            Write(batch.Writes, active.NumberInList!, Variant.From(index), snapshot.ActiveProgram.Sample);
            if (active.JobNodeId != null)
            {
                Write(batch.Writes, active.JobNodeId, Variant.From(jobId), snapshot.ActiveProgram.Sample);
                Write(batch.Writes, active.JobIdentifier!, Variant.From(snapshot.ActiveProgram.JobIdentifier),
                    snapshot.ActiveProgram.Sample);
            }
        }

        /// <summary>
        /// Mirrors the selected program's actual state history; switching selection is not a fictional transition.
        /// </summary>
        private void StageActiveState(ProductionBatch batch, MachineToolSnapshot snapshot)
        {
            FiniteStateMachineState active = State.ActiveProgram!.State!;
            if (batch.SelectedProgram != null)
            {
                FiniteStateMachineState source = batch.SelectedProgram.State!;
                Copy(source.CurrentState!, active.CurrentState!);
                Copy(source.CurrentState!.Id!, active.CurrentState!.Id!);
                Copy(source.CurrentState.Number!, active.CurrentState.Number!);
                Copy(source.LastTransition!, active.LastTransition!);
                Copy(source.LastTransition!.Id!, active.LastTransition!.Id!);
                Copy(source.LastTransition.Number!, active.LastTransition.Number!);
                Copy(source.LastTransition.TransitionTime!, active.LastTransition.TransitionTime!);
                return;
            }
            MachineToolProgram? prior = m_observation?.ActiveProgram;
            if (!SameSelection(prior, snapshot.ActiveProgram))
            {
                prior = null;
            }
            StageState(batch.Writes, active, prior?.Phase, prior?.TransitionSequence ?? 0,
                snapshot.ActiveProgram.Phase, snapshot.ActiveProgram.TransitionSequence, snapshot.ActiveProgram.Sample);

            void Copy(BaseVariableState source, BaseVariableState target)
            {
                for (int i = batch.Writes.Count - 1; i >= 0; i--)
                {
                    VariableWrite staged = batch.Writes[i];
                    if (ReferenceEquals(staged.Variable, source))
                    {
                        Write(batch.Writes, target, staged.Value, staged.Sample);
                        return;
                    }
                }
                Write(batch.Writes, target, source.WrappedValue,
                    new MachineToolSampleInfo(source.Timestamp, source.StatusCode));
            }
        }

        /// <summary>
        /// Compares application program identity across successive active-program observations.
        /// </summary>
        private static bool SameSelection(MachineToolProgram? previous, MachineToolProgram current) =>
            previous != null && Key(previous) == Key(current) && previous.JobIdentifier == current.JobIdentifier;

        /// <summary>
        /// Checks typed-factory adoption before accepting any application resource into the live graph.
        /// </summary>
        /// <typeparam name="TState">The generated job or part base type accepted by this factory.</typeparam>
        private async ValueTask ValidateFactoryNodeAsync<TState>(
            MachineToolProductionNode<TState> created, NodeState parent, string name, ExpandedNodeId type)
            where TState : BaseObjectState
        {
            TState node = created.State;
            NodeId expected = ExpandedNodeId.ToNodeId(type, m_context.Context.NamespaceUris);
            if (!ReferenceEquals(node.Parent, parent) || node.BrowseName != InstanceName(name) || node.NodeId.IsNull ||
                m_context.Manager.FindPredefinedNode(node.NodeId) != null || m_owned.ContainsKey(node.NodeId) ||
                !m_context.Context.TypeTable.IsTypeOf(node.TypeDefinitionId, expected))
            {
                bool resourceAlreadyOwned = false;
                foreach (NodeAddition owned in m_owned.Values)
                {
                    resourceAlreadyOwned |= ReferenceEquals(owned.Resource, created.Resource);
                }
                if (created.Resource != null && !resourceAlreadyOwned)
                {
                    await created.Resource.DisposeAsync().ConfigureAwait(false);
                }
                if (m_context.Manager.FindPredefinedNode(node.NodeId) == null && !m_owned.ContainsKey(node.NodeId))
                {
                    ReleaseReservations(node);
                }
                throw Invalid("A production factory must return a unique unpublished node of the declared type.");
            }
        }

        /// <summary>
        /// Registers detached additions before changing any existing observation.
        /// </summary>
        private async ValueTask RegisterAdditionsAsync(ProductionBatch batch, CancellationToken cancellationToken)
        {
            bool published = ReferenceEquals(m_context.Manager.FindPredefinedNode(m_machine.NodeId), m_machine);
            var newNodes = new HashSet<NodeState>();
            var identifiers = new HashSet<NodeId>();
            foreach (NodeAddition addition in batch.Additions)
            {
                ValidateNodeTree(addition.Node, newNodes, identifiers);
            }
            foreach (VariableWrite write in batch.Writes)
            {
                if (newNodes.Contains(write.Variable))
                {
                    write.Apply();
                }
            }
            foreach (NodeAddition addition in batch.Additions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_owned.ContainsKey(addition.Node.NodeId))
                {
                    throw Invalid("A production factory reused an owned NodeId.");
                }
                if (published)
                {
                    addition.Registered = true;
                    await m_context.Manager.AddPredefinedNodeAsync(addition.Node, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Removes only subtrees whose final observation delivery barrier has completed.
        /// </summary>
        private async ValueTask CompleteRemovalsAsync(ProductionBatch batch)
        {
            while (batch.RemovalsCompleted < batch.Removals.Count)
            {
                NodeAddition removed = batch.Removals[batch.RemovalsCompleted];
                removed.Detach();
                removed.Parent.RemoveReference(ReferenceTypeIds.HasNotifier, false, removed.Node.NodeId);
                removed.Parent.RemoveReference(ReferenceTypeIds.HasEventSource, false, removed.Node.NodeId);
                await ReleaseNodeAsync(removed).ConfigureAwait(false);
                batch.RemovalsCompleted++;
            }
        }

        /// <summary>
        /// Reverses staged registrations and resource transfers after a rejected update.
        /// </summary>
        private async ValueTask RollbackAsync(ProductionBatch batch)
        {
            var errors = new List<Exception>();
            for (int i = batch.Additions.Count - 1; i >= 0; i--)
            {
                try
                {
                    await ReleaseNodeAsync(batch.Additions[i]).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            ThrowCleanupErrors(errors);
        }

        /// <summary>
        /// Releases all module-owned resources while leaving the containing machine to its owner.
        /// </summary>
        private async ValueTask ReleaseOwnedNodesAsync()
        {
            var nodes = new List<NodeAddition>(m_owned.Values);
            var errors = new List<Exception>();
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                try
                {
                    nodes[i].Detach();
                    await ReleaseNodeAsync(nodes[i]).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            m_jobs.Clear();
            ThrowCleanupErrors(errors);
        }

        /// <summary>
        /// Deletes one owned subtree and disposes every companion resource attached below it exactly once.
        /// </summary>
        private async ValueTask ReleaseNodeAsync(NodeAddition addition)
        {
            NodeState node = addition.Node;
            var descendants = new List<NodeAddition>();
            foreach (NodeAddition owned in m_owned.Values)
            {
                NodeState? parent = owned.Node;
                while (parent is BaseInstanceState instance)
                {
                    if (ReferenceEquals(parent, node))
                    {
                        descendants.Add(owned);
                        break;
                    }
                    parent = instance.Parent;
                }
            }
            if (!descendants.Contains(addition))
            {
                descendants.Add(addition);
            }
            var reserved = new List<NodeState> { node };
            for (int i = 0; i < reserved.Count; i++)
            {
                var children = new List<BaseInstanceState>();
                reserved[i].GetChildren(m_context.Context, children);
                reserved.AddRange(children);
            }
            if ((addition.Registered || m_owned.ContainsKey(node.NodeId)) &&
                ReferenceEquals(m_context.Manager.FindPredefinedNode(node.NodeId), node))
            {
                await m_context.Manager.DeleteNodeAsync(
                    m_context.Manager.SystemContext, node.NodeId, CancellationToken.None).ConfigureAwait(false);
            }
            var errors = new List<Exception>();
            foreach (NodeAddition owned in descendants)
            {
                m_owned.Remove(owned.Node.NodeId);
                if (!owned.Disposed)
                {
                    owned.Disposed = true;
                    if (owned.Resource != null)
                    {
                        try
                        {
                            await owned.Resource.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            errors.Add(exception);
                        }
                    }
                }
            }
            if (m_context.Context.NodeIdFactory is Di.Server.ICompanionNodeIdReservationOwner reservations)
            {
                foreach (NodeState reservedNode in reserved)
                {
                    if (m_context.Manager.FindPredefinedNode(reservedNode.NodeId) == null)
                    {
                        reservations.ReleaseNodeIdReservation(reservedNode);
                    }
                }
            }
            ThrowCleanupErrors(errors);
        }

        /// <summary>
        /// Reports cleanup failures only after attempting every independently owned resource.
        /// </summary>
        private static void ThrowCleanupErrors(List<Exception> errors)
        {
            if (errors.Count != 0)
            {
                throw new AggregateException("Production resource cleanup failed.", errors);
            }
        }

        /// <summary>
        /// Stages a typed attachment and its matching rollback operation.
        /// </summary>
        private static void Add(
            ProductionBatch batch, BaseObjectState parent, BaseObjectState node,
            Action attach, Action detach, IAsyncDisposable? resource = null)
        {
            if (node.ReferenceTypeId.IsNull)
            {
                node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            }
            batch.Additions.Add(new NodeAddition(parent, node, attach, detach, resource));
        }

        /// <summary>
        /// Stages an existing node's removal.
        /// </summary>
        private void Remove(ProductionBatch batch, BaseObjectState node)
        {
            batch.Removals.Add(m_owned[node.NodeId]);
        }

        /// <summary>
        /// Configures read-only NodeVersion on an ordered list through the shared model-change service.
        /// </summary>
        private void TrackList(BaseObjectState node)
        {
            PropertyState<string> version = node.EnableModelChangeTracking(m_context.InstanceNamespaceIndex);
            version.AccessLevel = AccessLevels.CurrentRead;
            version.UserAccessLevel = AccessLevels.CurrentRead;
        }

        /// <summary>
        /// Keeps ordered browsing and event propagation aligned with the committed inventory.
        /// </summary>
        private void ReorderAndLink(ProductionBatch batch, ArrayOf<MachineToolProductionJob> order)
        {
            Link(State, State.ActiveProgram!);
            Link(State.ActiveProgram!, State.ActiveProgram!.State!);
            if (State.ProductionPlan != null)
            {
                Link(State, State.ProductionPlan);
                foreach (JobNodes old in m_jobs.Values)
                {
                    State.ProductionPlan.RemoveChild(old.State);
                }
                foreach (MachineToolProductionJob observation in order)
                {
                    JobNodes current = batch.Jobs[Key(observation)];
                    State.ProductionPlan.RemoveChild(current.State);
                    State.ProductionPlan.AddChild(current.State);
                    Link(State.ProductionPlan, current.State);
                    Link(current.State, current.State.State!);
                    Link(current.State, current.State.ProductionPrograms!);
                    if (m_jobs.TryGetValue(Key(current.Observation), out JobNodes? previous))
                    {
                        foreach (ProgramNodes old in previous.Programs.Values)
                        {
                            current.State.ProductionPrograms!.RemoveChild(old.State);
                        }
                    }
                    foreach (MachineToolProgram value in observation.Programs)
                    {
                        ProgramNodes program = current.Programs[Key(value)];
                        current.State.ProductionPrograms!.RemoveChild(program.State);
                        current.State.ProductionPrograms.AddChild(program.State);
                        Link(current.State.ProductionPrograms, program.State);
                        Link(program.State, program.State.State!);
                    }
                    foreach (PartSetNodes set in current.PartSets.Values)
                    {
                        Link(current.State, current.State.PartSets!);
                        Link(current.State.PartSets!, set.State);
                        foreach (PartNodes part in set.Parts.Values)
                        {
                            Link(set.State, set.State.PartsPerRun!);
                            Link(set.State.PartsPerRun!, part.State);
                            Link(part.State, part.State.State!);
                        }
                    }
                    foreach (ConditionNodes condition in current.Conditions.Values)
                    {
                        current.State.AddReferenceIfMissing(
                            ReferenceTypeIds.HasEventSource, false, condition.State.NodeId);
                        condition.State.AddReferenceIfMissing(
                            ReferenceTypeIds.HasEventSource, true, current.State.NodeId);
                    }
                }
            }
        }

        /// <summary>
        /// Links exactly one upward event path, independently of browsing and property relationships.
        /// </summary>
        private void Link(BaseObjectState parent, BaseObjectState node)
        {
            parent.EventNotifier |= EventNotifiers.SubscribeToEvents;
            node.EventNotifier |= EventNotifiers.SubscribeToEvents;
            // BaseInstanceState already propagates through Parent; AddNotifier would deliver every event twice.
            parent.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, false, node.NodeId);
            node.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, parent.NodeId);
        }

        /// <summary>
        /// Resolves a mandatory MachineTool subcomponent without reflection.
        /// </summary>
        private BaseVariableState Child(NodeState parent, string name) =>
            parent.FindChild(m_context.Context, ModelName(name)) as BaseVariableState ??
                throw Invalid($"The generated node is missing {name}.");

        /// <summary>
        /// Qualifies a declaration browse name using the loaded MachineTool namespace.
        /// </summary>
        private QualifiedName ModelName(string name) =>
            new(name, (ushort)m_context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool));

        /// <summary>
        /// Qualifies an application-owned list member.
        /// </summary>
        private QualifiedName InstanceName(string name) => new(name, m_context.InstanceNamespaceIndex);

        /// <summary>
        /// Rejects duplicate or already registered factory identities throughout the entire detached tree.
        /// </summary>
        private void ValidateNodeTree(NodeState node, HashSet<NodeState> nodes, HashSet<NodeId> identifiers)
        {
            if (!nodes.Add(node) || node.NodeId.IsNull || !identifiers.Add(node.NodeId) ||
                m_context.Manager.FindPredefinedNode(node.NodeId) != null)
            {
                throw Invalid("A production factory returned a duplicate or registered node identity.");
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context.Context, children);
            foreach (BaseInstanceState child in children)
            {
                ValidateNodeTree(child, nodes, identifiers);
            }
        }

        /// <summary>
        /// Gets a job slot's stable application identity.
        /// </summary>
        private static string Key(MachineToolProductionJob job) =>
            string.IsNullOrEmpty(job.Key) ? job.Identifier : job.Key;

        /// <summary>
        /// Gets a program slot's stable application identity.
        /// </summary>
        private static string Key(MachineToolProgram program) =>
            string.IsNullOrEmpty(program.Key) ? program.Name : program.Key;

        /// <summary>
        /// Detects changes to the provider's ordered identities.
        /// </summary>
        /// <typeparam name="T">The immutable job or program observation.</typeparam>
        private static bool OrderChanged<T>(ArrayOf<T> previous, ArrayOf<T> current, Func<T, string> key)
        {
            if (previous.Count != current.Count)
            {
                return true;
            }
            for (int i = 0; i < current.Count; i++)
            {
                if (key(previous[i]) != key(current[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One fully staged observation and its reversible topology changes.
        /// </summary>
        private sealed class ProductionBatch
        {
            /// <summary>
            /// Gets the staged scalar writes.
            /// </summary>
            public List<VariableWrite> Writes { get; } = [];

            /// <summary>
            /// Gets detached new objects.
            /// </summary>
            public List<NodeAddition> Additions { get; } = [];

            /// <summary>
            /// Gets objects to remove after final publication.
            /// </summary>
            public List<NodeAddition> Removals { get; } = [];

            /// <summary>
            /// Gets the next job inventory.
            /// </summary>
            public Dictionary<string, JobNodes> Jobs { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets or sets the actual modeled program mirrored by ActiveProgram.
            /// </summary>
            public ProductionProgramState? SelectedProgram { get; set; }

            /// <summary>
            /// Gets changes to module-owned condition observations applied with the scalar writes.
            /// </summary>
            public List<Action> CommitActions { get; } = [];

            /// <summary>
            /// Gets detached transition events or retained condition snapshots to report after commit.
            /// </summary>
            public List<EventReport> Events { get; } = [];

            /// <summary>
            /// Gets ordered lists whose reference order changed without necessarily adding or deleting nodes.
            /// </summary>
            public HashSet<BaseObjectState> ChangedLists { get; } = [];

            /// <summary>
            /// Gets or sets the number of events whose publication completed.
            /// </summary>
            public int EventsCompleted { get; set; }

            /// <summary>
            /// Gets or sets the number of completed removals.
            /// </summary>
            public int RemovalsCompleted { get; set; }

            /// <summary>
            /// Gets or sets the staged model-change event, retained unchanged on a failed publication.
            /// </summary>
            public GeneralModelChangeEventState? ModelEvent { get; set; }

            /// <summary>
            /// Gets whether the operation was an initial unpublished-tree setup.
            /// </summary>
            public bool Initializing { get; set; }

            /// <summary>
            /// Gets or sets whether this is an identical re-read of an already published observation.
            /// </summary>
            public bool Skipped { get; set; }

            /// <summary>
            /// Gets or sets whether rejection rollback is no longer possible.
            /// </summary>
            public bool Committed { get; set; }
        }

        /// <summary>
        /// An owned generated object, its attachment operations and optional companion resource.
        /// </summary>
        private sealed record NodeAddition(
            BaseObjectState Parent, BaseObjectState Node, Action Attach, Action Detach, IAsyncDisposable? Resource)
        {
            /// <summary>
            /// Gets or sets whether registration was completed.
            /// </summary>
            public bool Registered { get; set; }

            /// <summary>
            /// Gets or sets whether the owned resource was released.
            /// </summary>
            public bool Disposed { get; set; }
        }

        /// <summary>
        /// A generated job and its ordered inventories.
        /// </summary>
        private sealed record JobNodes(ProductionJobState State, MachineToolProductionJob Observation)
        {
            /// <summary>
            /// Gets the program slots.
            /// </summary>
            public Dictionary<string, ProgramNodes> Programs { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets the current-run part sets.
            /// </summary>
            public Dictionary<string, PartSetNodes> PartSets { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets the retained standard interruption conditions.
            /// </summary>
            public Dictionary<string, ConditionNodes> Conditions { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// A generated program slot and its observation.
        /// </summary>
        private sealed record ProgramNodes(ProductionProgramState State, MachineToolProgram Observation);

        /// <summary>
        /// A generated part set and its current-run parts.
        /// </summary>
        private sealed record PartSetNodes(ProductionPartSetState State, MachineToolProductionPartSet Observation)
        {
            /// <summary>
            /// Gets the part slots.
            /// </summary>
            public Dictionary<string, PartNodes> Parts { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// A generated part slot and its observation.
        /// </summary>
        private sealed record PartNodes(ProductionPartState State, MachineToolProductionPart Observation);

        /// <summary>The retained generated job inventory.</summary>
        private Dictionary<string, JobNodes> m_jobs = new(StringComparer.Ordinal);

        /// <summary>Objects and resources owned by this module, never by another MachineTool.</summary>
        private readonly Dictionary<NodeId, NodeAddition> m_owned = [];
    }
}
