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
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolProductionController
    {
        /// <summary>
        /// Derives concrete application-owned event types from the specification's abstract declarations.
        /// </summary>
        private async ValueTask EnsureEventTypesAsync(CancellationToken cancellationToken)
        {
            if (!Has(MachineToolFeatures.Production) || m_eventTypes.Count != 0)
            {
                return;
            }
            ExpandedNodeId[] abstractTypes =
            [
                ObjectTypeIds.ProductionJobTransitionEventType,
                ObjectTypeIds.ProductionProgramTransitionEventType,
                ObjectTypeIds.ProductionPartTransitionEventType,
                ObjectTypeIds.InterruptionConditionType
            ];
            foreach (ExpandedNodeId id in abstractTypes)
            {
                NodeId baseId = ExpandedNodeId.ToNodeId(id, m_context.Context.NamespaceUris);
                var concrete = new BaseObjectTypeState
                {
                    NodeId = new NodeId(Guid.NewGuid(), m_context.InstanceNamespaceIndex),
                    BrowseName = InstanceName("ProductionEvent" + m_eventTypes.Count),
                    DisplayName = new LocalizedText("Concrete production event"),
                    SuperTypeId = baseId,
                    IsAbstract = false,
                    IsPartOfTypeHierarchy = true
                };
                m_eventTypes.Add(baseId, concrete);
                await m_context.Manager.AddPredefinedNodeAsync(concrete, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stages an immutable event snapshot for every reported state-machine transition.
        /// </summary>
        private void StageEvents(ProductionBatch batch, MachineToolSnapshot snapshot)
        {
            if (!Has(MachineToolFeatures.Production))
            {
                return;
            }
            foreach ((string key, JobNodes current) in batch.Jobs)
            {
                m_jobs.TryGetValue(key, out JobNodes? prior);
                MachineToolProductionJob job = current.Observation;
                if (Changed(prior?.Observation.Phase, prior?.Observation.TransitionSequence,
                    job.Phase, job.TransitionSequence))
                {
                    ProductionJobTransitionEventState e =
                        m_context.Context.CreateInstanceOfProductionJobTransitionEventType();
                    InitializeTransition(e, current.State.State!, prior!.Observation.Phase, job.Phase,
                        job.Sample, ObjectTypeIds.ProductionJobTransitionEventType);
                    e.AddCustomerOrderIdentifier(m_context.Context);
                    e.AddOrderIdentifier(m_context.Context);
                    e.Identifier!.Value = job.Identifier;
                    e.CustomerOrderIdentifier!.Value = job.CustomerOrderIdentifier;
                    e.OrderIdentifier!.Value = job.OrderIdentifier;
                    e.RunsCompleted!.Value = job.RunsCompleted;
                    e.RunsPlanned!.Value = job.RunsPlanned;
                    Child(e.RunsPlanned, BrowseNames.IsValid).Value = Variant.From(job.RunsPlannedIsValid);
                    batch.Events.Add(new EventReport(current.State.State!, e));
                }
                foreach ((string programKey, ProgramNodes program) in current.Programs)
                {
                    ProgramNodes? old = null;
                    prior?.Programs.TryGetValue(programKey, out old);
                    StageProgramEvent(
                        batch, program.State.State!, old?.Observation, program.Observation, job.Identifier);
                }
                foreach ((string setKey, PartSetNodes set) in current.PartSets)
                {
                    PartSetNodes? oldSet = null;
                    prior?.PartSets.TryGetValue(setKey, out oldSet);
                    foreach ((string partKey, PartNodes part) in set.Parts)
                    {
                        PartNodes? old = null;
                        oldSet?.Parts.TryGetValue(partKey, out old);
                        if (!Changed(old?.Observation.Phase, old?.Observation.TransitionSequence,
                            part.Observation.Phase, part.Observation.TransitionSequence))
                        {
                            continue;
                        }
                        MachineToolProductionPart value = part.Observation;
                        ProductionPartTransitionEventState e =
                            m_context.Context.CreateInstanceOfProductionPartTransitionEventType();
                        InitializeTransition(e, part.State.State!, old!.Observation.Phase, value.Phase,
                            value.Sample, ObjectTypeIds.ProductionPartTransitionEventType);
                        e.AddIdentifier(m_context.Context);
                        e.AddCustomerOrderIdentifier(m_context.Context);
                        e.JobIdentifier!.Value = job.Identifier;
                        e.Name!.Value = value.Name;
                        e.Identifier!.Value = value.Identifier;
                        e.CustomerOrderIdentifier!.Value = value.CustomerOrderIdentifier;
                        e.PartQuality!.Value = value.Quality;
                        e.ProcessIrregularity!.Value = value.Irregularity;
                        batch.Events.Add(new EventReport(part.State.State!, e));
                    }
                }
            }
            if (SameSelection(m_observation?.ActiveProgram, snapshot.ActiveProgram))
            {
                StageProgramEvent(batch, State.ActiveProgram!.State!, m_observation!.ActiveProgram,
                    snapshot.ActiveProgram, snapshot.ActiveProgram.JobIdentifier);
            }
        }

        /// <summary>
        /// Stages a generated program transition event, including the matching job identifier.
        /// </summary>
        private void StageProgramEvent(
            ProductionBatch batch, FiniteStateMachineState state,
            MachineToolProgram? prior, MachineToolProgram program, string jobIdentifier)
        {
            if (!Changed(prior?.Phase, prior?.TransitionSequence, program.Phase, program.TransitionSequence))
            {
                return;
            }
            ProductionProgramTransitionEventState e =
                m_context.Context.CreateInstanceOfProductionProgramTransitionEventType();
            InitializeTransition(e, state, prior!.Phase, program.Phase, program.Sample,
                ObjectTypeIds.ProductionProgramTransitionEventType);
            e.Name!.Value = program.Name;
            e.JobIdentifier!.Value = jobIdentifier;
            batch.Events.Add(new EventReport(state, e));
        }

        /// <summary>
        /// Initializes canonical Part 16 transition fields and the equipment's event timestamp.
        /// </summary>
        private void InitializeTransition(
            TransitionEventState e, FiniteStateMachineState source,
            MachineToolProductionPhase from, MachineToolProductionPhase to,
            MachineToolSampleInfo sample, ExpandedNodeId baseType)
        {
            StateSchema schema = GetSchema(source.TypeDefinitionId);
            int transition = Array.IndexOf(s_transitions, $"{from}To{to}");
            // Keep the generated property nodes: BaseEventState.Initialize replaces them with transient fields.
            e.EventId!.Value = Uuid.NewUuid().ToByteString();
            e.Severity!.Value = (ushort)EventSeverity.Low;
            e.Message!.Value = new LocalizedText($"{from} to {to}");
            e.TypeDefinitionId = ConcreteType(baseType);
            e.EventType!.Value = e.TypeDefinitionId;
            e.Time!.Value = sample.SourceTimestamp;
            e.ReceiveTime!.Value = m_timeProvider.GetUtcNow().UtcDateTime;
            e.SourceNode!.Value = source.NodeId;
            e.SourceName!.Value = source.BrowseName.Name ?? string.Empty;
            e.FromState!.AddNumber(m_context.Context);
            e.ToState!.AddNumber(m_context.Context);
            e.Transition!.AddNumber(m_context.Context);
            e.Transition.AddTransitionTime(m_context.Context);
            e.FromState.Value = new LocalizedText(schema.States[(int)from].Name);
            e.FromState.Id!.WrappedValue = Variant.From(new NodeId(schema.States[(int)from].Id, schema.NamespaceIndex));
            e.FromState.Number!.Value = schema.States[(int)from].Number;
            e.ToState.Value = new LocalizedText(schema.States[(int)to].Name);
            e.ToState.Id!.WrappedValue = Variant.From(new NodeId(schema.States[(int)to].Id, schema.NamespaceIndex));
            e.ToState.Number!.Value = schema.States[(int)to].Number;
            FiniteStateMachineEntry entry = schema.Transitions[transition];
            e.Transition.Value = new LocalizedText(entry.Name);
            e.Transition.Id!.WrappedValue = Variant.From(new NodeId(entry.Id, schema.NamespaceIndex));
            e.Transition.Number!.Value = entry.Number;
            e.Transition.TransitionTime!.Value = sample.SourceTimestamp;
        }

        /// <summary>
        /// Applies the standard condition lifecycle while keeping the interruption's equipment state external.
        /// </summary>
        private void StageInterruptions(ProductionBatch batch, JobNodes current, JobNodes? prior)
        {
            foreach (MachineToolProductionInterruption observation in current.Observation.Interruptions)
            {
                ConditionNodes? old = null;
                prior?.Conditions.TryGetValue(observation.Identifier, out old);
                ConditionNodes binding;
                if (old == null)
                {
                    InterruptionConditionState created = m_context.Context.CreateInstanceOfInterruptionConditionType(
                        current.State, InstanceName("Interruption:" + observation.Identifier));
                    created.TypeDefinitionId = ConcreteType(ObjectTypeIds.InterruptionConditionType);
                    created.EventType!.Value = created.TypeDefinitionId;
                    created.SourceNode!.Value = current.State.NodeId;
                    created.SourceName!.Value = current.Observation.Identifier;
                    created.EventId!.Value = Uuid.NewUuid().ToByteString();
                    binding = new ConditionNodes(created, observation);
                    created.RetainStateProvider = _ => binding.Observation.IsActive;
                    created.SetEnableState(m_context.Context, true);
                    created.AutoReportStateChanges = true;
                    created.ConditionName!.Value = observation.Identifier;
                    Add(batch, current.State, created, () =>
                        {
                            current.State.AddChild(created);
                            current.State.AddReferenceIfMissing(ReferenceTypeIds.HasCondition, false, created.NodeId);
                        },
                        () =>
                        {
                            current.State.RemoveChild(created);
                            current.State.RemoveReference(ReferenceTypeIds.HasCondition, false, created.NodeId);
                        });
                }
                else
                {
                    binding = old;
                }
                current.Conditions.Add(observation.Identifier, binding);
                InterruptionConditionState condition = binding.State;
                batch.CommitActions.Add(() => binding.Observation = observation);
                Write(batch.Writes, condition.ConditionClassId!,
                    Variant.From(observation.ConditionClassId), observation.Sample);
                Write(batch.Writes, condition.ConditionClassName!,
                    Variant.From(m_context.Manager.FindPredefinedNode(observation.ConditionClassId)!.DisplayName),
                    observation.Sample);
                Write(batch.Writes, condition.IsAutomated!, Variant.From(observation.IsAutomated), observation.Sample);
                Write(batch.Writes, condition.Message!, Variant.From(observation.Message), observation.Sample);
                Write(batch.Writes, condition.Severity!, Variant.From(observation.Severity), observation.Sample);
                Write(batch.Writes, condition.Quality!,
                    Variant.From(observation.Sample.StatusCode), observation.Sample);
                Write(batch.Writes, condition.Retain!,
                    Variant.From(condition.EnabledState!.Id!.Value && observation.IsActive), observation.Sample);
                if (old == null || observation != old.Observation)
                {
                    Write(batch.Writes, condition.EventId!,
                        Variant.From(Uuid.NewUuid().ToByteString()), observation.Sample);
                    Write(batch.Writes, condition.Time!,
                        Variant.From(observation.Sample.SourceTimestamp), observation.Sample);
                    Write(batch.Writes, condition.ReceiveTime!,
                        Variant.From((DateTimeUtc)m_timeProvider.GetUtcNow().UtcDateTime), observation.Sample);
                    batch.Events.Add(new EventReport(condition, condition));
                }
            }
            if (prior != null)
            {
                foreach ((string key, ConditionNodes old) in prior.Conditions)
                {
                    if (!current.Conditions.ContainsKey(key))
                    {
                        Remove(batch, old.State);
                    }
                }
            }
        }

        /// <summary>
        /// Publishes snapshots outside all lifetime gates; standard condition reporting retains event identities.
        /// </summary>
        private async ValueTask PublishEventsAsync(ProductionBatch batch)
        {
            while (batch.EventsCompleted < batch.Events.Count)
            {
                EventReport report = batch.Events[batch.EventsCompleted];
                IFilterTarget e = report.Event;
                if (e is ConditionState condition)
                {
                    if (!condition.EnabledState!.Id!.Value)
                    {
                        batch.EventsCompleted++;
                        continue;
                    }
                    var snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(m_context.Context, condition);
                    e = snapshot;
                }
                await report.Source.ReportEventAsync(m_context.Context, e, CancellationToken.None)
                    .ConfigureAwait(false);
                batch.EventsCompleted++;
            }
        }

        /// <summary>
        /// Aggregates dynamic structure changes using the shared model-change helpers and the nested plan notifier.
        /// </summary>
        private async ValueTask PublishModelChangesAsync(ProductionBatch batch)
        {
            if (State.ProductionPlan == null ||
                (batch.Additions.Count == 0 && batch.Removals.Count == 0 && batch.ChangedLists.Count == 0))
            {
                return;
            }
            if (batch.ModelEvent != null)
            {
                await State.ProductionPlan.ReportEventAsync(m_context.Context, batch.ModelEvent, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
            var changes = new ModelChangeAggregator();
            var lists = new HashSet<BaseObjectState> { State.ProductionPlan };
            foreach (BaseObjectState list in batch.ChangedLists)
            {
                lists.Add(list);
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = list.NodeId, AffectedType = list.TypeDefinitionId,
                    Verb = (byte)(ModelChangeVerbs.ReferenceAdded | ModelChangeVerbs.ReferenceDeleted)
                });
            }
            foreach (NodeAddition addition in batch.Additions)
            {
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = addition.Node.NodeId, AffectedType = addition.Node.TypeDefinitionId,
                    Verb = (byte)ModelChangeVerbs.NodeAdded
                });
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = addition.Parent.NodeId, AffectedType = addition.Parent.TypeDefinitionId,
                    Verb = (byte)ModelChangeVerbs.ReferenceAdded
                });
                if (addition.Parent.HasNodeVersion())
                {
                    lists.Add(addition.Parent);
                }
            }
            foreach (NodeAddition removal in batch.Removals)
            {
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = removal.Node.NodeId, AffectedType = removal.Node.TypeDefinitionId,
                    Verb = (byte)ModelChangeVerbs.NodeDeleted
                });
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = removal.Parent.NodeId, AffectedType = removal.Parent.TypeDefinitionId,
                    Verb = (byte)ModelChangeVerbs.ReferenceDeleted
                });
                if (removal.Parent.HasNodeVersion())
                {
                    lists.Add(removal.Parent);
                }
            }
            foreach (BaseObjectState list in lists)
            {
                list.BumpNodeVersion(m_context.Context);
            }
            var e = new GeneralModelChangeEventState(null);
            e.Initialize(m_context.Context, State.ProductionPlan, EventSeverity.Low,
                new LocalizedText("Production plan changed."));
            e.Time!.Value = m_timeProvider.GetUtcNow().UtcDateTime;
            e.ReceiveTime!.Value = e.Time.Value;
            e.CreateOrReplaceChanges(m_context.Context, null);
            e.Changes!.Value = changes.Drain();
            batch.ModelEvent = e;
            await State.ProductionPlan.ReportEventAsync(m_context.Context, e, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Completes the accepted observation without holding a callback-visible gate.
        /// A callback failure retains the exact committed snapshot and event IDs for an explicit retry.
        /// </summary>
        private async ValueTask CompletePublicationAsync(ProductionBatch batch)
        {
            if (batch.Skipped)
            {
                return;
            }
            MachineToolProductionController? previous = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                await State.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!batch.Initializing)
                {
                    await PublishEventsAsync(batch).ConfigureAwait(false);
                    await CompleteRemovalsAsync(batch).ConfigureAwait(false);
                    await PublishModelChangesAsync(batch).ConfigureAwait(false);
                }
                m_pendingPublication = null;
                ReleaseEventReservations(batch);
            }
            finally
            {
                s_callbackOwner.Value = previous;
            }
        }

        /// <summary>
        /// Releases transient factory identities, never the identities of retained live conditions.
        /// </summary>
        private void ReleaseEventReservations(ProductionBatch batch)
        {
            foreach (EventReport report in batch.Events)
            {
                if (report.Event is NodeState node and not ConditionState)
                {
                    ReleaseReservations(node);
                }
            }
            if (batch.ModelEvent != null)
            {
                ReleaseReservations(batch.ModelEvent);
            }
        }

        /// <summary>
        /// Releases application-owned event type nodes after all publication and condition resources have drained.
        /// </summary>
        private async ValueTask ReleaseEventTypesAsync()
        {
            var errors = new List<Exception>();
            foreach (BaseObjectTypeState type in m_eventTypes.Values)
            {
                if (ReferenceEquals(m_context.Manager.FindPredefinedNode(type.NodeId), type))
                {
                    try
                    {
                        await m_context.Manager.DeleteNodeAsync(
                            m_context.Manager.SystemContext, type.NodeId, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }
            }
            m_eventTypes.Clear();
            ThrowCleanupErrors(errors);
        }

        /// <summary>
        /// Resolves an already-registered concrete event type.
        /// </summary>
        private NodeId ConcreteType(ExpandedNodeId baseType) =>
            m_eventTypes[ExpandedNodeId.ToNodeId(baseType, m_context.Context.NamespaceUris)].NodeId;

        /// <summary>
        /// Tests whether a known prior state was followed by a reported transition.
        /// </summary>
        private static bool Changed(
            MachineToolProductionPhase? previous, ulong? previousSequence,
            MachineToolProductionPhase phase, ulong sequence) =>
            previous != null && (previous != phase || previousSequence != sequence);

        /// <summary>
        /// A publication's source and independent event snapshot.
        /// </summary>
        private sealed record EventReport(NodeState Source, IFilterTarget Event);

        /// <summary>
        /// A retained standard condition with a provider-backed retention predicate.
        /// </summary>
        private sealed class ConditionNodes
        {
            /// <summary>
            /// Creates the condition binding.
            /// </summary>
            public ConditionNodes(InterruptionConditionState state, MachineToolProductionInterruption observation)
            {
                State = state;
                Observation = observation;
            }

            /// <summary>
            /// Gets the concrete generated condition.
            /// </summary>
            public InterruptionConditionState State { get; }

            /// <summary>
            /// Gets or sets the latest committed equipment observation.
            /// </summary>
            public MachineToolProductionInterruption Observation { get; set; }
        }

        /// <summary>Concrete event types owned by this production module.</summary>
        private readonly Dictionary<NodeId, BaseObjectTypeState> m_eventTypes = [];
    }
}
