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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Implements OPC 40084-2 job groups, strand-aware jobs, configuration defaults and exact production events.
    /// </summary>
    /// <remarks>
    /// Hardware and durable acceptance remain injectable. The controller never counts units, advances
    /// lots or simulates production. Providers publish absolute committed production and explicit
    /// completion facts; replaying a commit cannot duplicate its counters or events.
    /// </remarks>
    public sealed class ExtrusionJobController : IAsyncDisposable
    {
        /// <summary>
        /// Binds the selected generated line interface without creating a Machinery job manager.
        /// </summary>
        internal ExtrusionJobController(
            ExtrusionRuntime runtime,
            ExtrusionLine_InterfaceState line,
            ExtrusionDeviceBinding binding,
            IExtrusionJobProvider? provider,
            ExtrusionNodeMaterializer materializer)
        {
            m_runtime = runtime;
            m_line = line;
            m_binding = binding;
            m_provider = provider;
            m_materializer = materializer;
            Node = line.JobGroups!;
            m_events = new ExtrusionEventEmitter(runtime, Node);
            BindGroupMethods();
        }

        /// <summary>
        /// Gets the real generated standard job-group container.
        /// </summary>
        public JobGroupsState Node { get; }

        /// <summary>
        /// Gets currently published groups without exposing mutable controller collections.
        /// </summary>
        public ArrayOf<JobGroupState> Groups
        {
            get
            {
                lock (m_sync)
                {
                    return m_groups.Values.Select(group => group.Node).ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets whether active strand production currently forbids an overall good-quality indication.
        /// </summary>
        internal bool HasBadActiveProduction => Volatile.Read(ref m_badActiveProduction);

        /// <summary>
        /// Adds a group using the same authorization and protocol path as the generated UA method.
        /// </summary>
        public async ValueTask<JobGroupState> AddGroupAsync(
            ISystemContext requestContext,
            ExtrusionJobGroupDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ValidateGroup(definition);
            using var pending = Link(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_groups.ContainsKey(definition.Id))
                {
                    throw Error(StatusCodes.BadEntryExists, "The job-group identifier already exists.");
                }
                int index = NextIndex(Node, "JobGroup_");
                var node = m_runtime.SystemContext.CreateInstanceOfJobGroupType(
                    Node, new QualifiedName("JobGroup_" + index.ToString("D3", CultureInfo.InvariantCulture),
                        Node.BrowseName.NamespaceIndex));
                m_materializer.MaterializeChild(node, "JobGroups/JobGroup_<Nr>",
                    "Extrusion/Jobs/" + Uri.EscapeDataString(m_line.NodeId.IdentifierAsString) +
                    "/" + Uri.EscapeDataString(definition.Id));
                var request = new ExtrusionJobRequest(requestContext, ExtrusionJobOperation.AddGroup,
                    Node.NodeId, definition, null);
                ExtrusionJobResult result = await CommitAsync(request, pending.Token).ConfigureAwait(false);
                ArrayOf<ConfigurationParameterType> parameters = result.ConfigurationParameters.IsEmpty
                    ? m_line.ConfigurationParameters!.Value : result.ConfigurationParameters;
                ValidateParameters(parameters);
                PopulateGroup(node, definition, parameters, result.CommittedStatus);
                var entry = new GroupEntry(node, definition, parameters);
                BindJobMethods(entry);
                try
                {
                    Node.AddChild(node);
                    await m_runtime.RegisterAsync(node, pending.Token).ConfigureAwait(false);
                    m_binding.BindVariables(node, initializeUnknown: false);
                    lock (m_sync)
                    {
                        m_groups.Add(definition.Id, entry);
                    }
                }
                catch (Exception failure)
                {
                    await RollbackAsync(node, failure).ConfigureAwait(false);
                    throw;
                }
                UpdateVersion(Node.NodeVersion!);
                await m_events.ModelChangedAsync(node, true).ConfigureAwait(false);
                return node;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Adds a job with group-scoped identifiers and validated strand/sequence/configuration semantics.
        /// </summary>
        public async ValueTask<JobState> AddJobAsync(
            ISystemContext requestContext,
            string groupId,
            ExtrusionJobDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ValidateJob(definition);
            using var pending = Link(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                GroupEntry group = RequireGroup(groupId);
                if (group.Jobs.ContainsKey(definition.Id))
                {
                    throw Error(StatusCodes.BadEntryExists, "The job identifier already exists in this group.");
                }
                if (group.Jobs.Values.Any(job => job.Definition.Strand == definition.Strand &&
                    job.Definition.Sequence == definition.Sequence))
                {
                    throw Error(StatusCodes.BadInvalidArgument, "A strand already has a job at that sequence.");
                }
                ArrayOf<ParameterSettingType> effective = ResolveParameters(
                    group.Parameters, definition.ParameterSetting);
                definition = definition with { ParameterSetting = effective };
                int index = NextIndex(group.Node, "Job_");
                var node = m_runtime.SystemContext.CreateInstanceOfJobType(group.Node,
                    new QualifiedName("Job_" + index.ToString("D3", CultureInfo.InvariantCulture),
                        Node.BrowseName.NamespaceIndex));
                m_materializer.MaterializeChild(node, "JobGroups/JobGroup_<Nr>/Job_<Nr>",
                    group.Node.NodeId.IdentifierAsString + "/Jobs/" + Uri.EscapeDataString(definition.Id));
                var request = new ExtrusionJobRequest(requestContext, ExtrusionJobOperation.AddJob,
                    group.Node.NodeId, group.Definition, definition);
                ExtrusionJobResult result = await CommitAsync(request, pending.Token).ConfigureAwait(false);
                PopulateJob(node, definition, result.CommittedStatus);
                var entry = new JobEntry(node, definition);
                try
                {
                    group.Node.AddChild(node);
                    await m_runtime.RegisterAsync(node, pending.Token).ConfigureAwait(false);
                    m_binding.BindVariables(node, initializeUnknown: false);
                    group.Jobs.Add(definition.Id, entry);
                }
                catch (Exception failure)
                {
                    await RollbackAsync(node, failure).ConfigureAwait(false);
                    throw;
                }
                UpdateVersion(group.Node.NodeVersion!);
                await m_events.ModelChangedAsync(node, true).ConfigureAwait(false);
                return node;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Performs a group- or job-specific start, interrupt, finish or remove operation.
        /// </summary>
        public async ValueTask ExecuteAsync(
            ISystemContext requestContext,
            ExtrusionJobOperation operation,
            string groupId,
            string? jobId = null,
            CancellationToken cancellationToken = default)
        {
            if (operation is ExtrusionJobOperation.AddGroup or ExtrusionJobOperation.AddJob)
            {
                throw new ArgumentException("Use the typed add operation to supply its required metadata.",
                    nameof(operation));
            }
            using var pending = Link(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                GroupEntry group = RequireGroup(groupId);
                bool jobOperation = operation is ExtrusionJobOperation.RemoveJob or ExtrusionJobOperation.StartJob or
                    ExtrusionJobOperation.InterruptJob or ExtrusionJobOperation.FinishJob;
                JobEntry? job = jobOperation ? RequireJob(group, jobId) : null;
                ValidateOperation(operation, group, job);
                var request = new ExtrusionJobRequest(requestContext, operation,
                    jobOperation ? group.Node.NodeId : Node.NodeId, group.Definition, job?.Definition);
                ExtrusionJobResult result = await CommitAsync(request, pending.Token).ConfigureAwait(false);
                if (operation == ExtrusionJobOperation.RemoveGroup)
                {
                    await m_runtime.RemoveAsync(group.Node).ConfigureAwait(false);
                    m_binding.UnbindVariables(group.Node);
                    lock (m_sync)
                    {
                        m_groups.Remove(groupId);
                    }
                    RefreshActiveProductionQuality(DateTimeUtc.Now);
                    UpdateVersion(Node.NodeVersion!);
                    await m_events.ModelChangedAsync(group.Node, false).ConfigureAwait(false);
                }
                else if (operation == ExtrusionJobOperation.RemoveJob)
                {
                    await m_runtime.RemoveAsync(job!.Node).ConfigureAwait(false);
                    m_binding.UnbindVariables(job.Node);
                    group.Jobs.Remove(job.Definition.Id);
                    RefreshActiveProductionQuality(DateTimeUtc.Now);
                    UpdateVersion(group.Node.NodeVersion!);
                    await m_events.ModelChangedAsync(job.Node, false).ConfigureAwait(false);
                }
                else
                {
                    ValidateReportedStatus(operation, result.CommittedStatus);
                    if (job == null)
                    {
                        await SetGroupStatusAsync(group, result.CommittedStatus, DateTimeUtc.Now).ConfigureAwait(false);
                    }
                    else
                    {
                        await SetJobStatusAsync(group, job, result.CommittedStatus,
                            DateTimeUtc.Now).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Publishes absolute production and exact completion events, with per-job replay protection.
        /// </summary>
        public async ValueTask ApplyProductionAsync(
            ExtrusionJobProductionSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ValidateProduction(snapshot);
            using var pending = Link(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                GroupEntry group = RequireGroup(snapshot.GroupId);
                JobEntry job = RequireJob(group, snapshot.JobId);
                if (job.LastCommit != null && snapshot.Revision <= job.LastCommit.Revision)
                {
                    if (snapshot.Equals(job.LastCommit))
                    {
                        return;
                    }
                    throw Error(StatusCodes.BadInvalidState,
                        "A job commit is stale or conflicts with an applied revision.");
                }
                ValidateCompletionFacts(job, snapshot);
                JobStatusEnumeration previousStatus = job.Node.Status!.Value;
                bool previousQuality = job.Node.GoodProduct!.Value;
                Set(job.Node.Status!, Variant.From(snapshot.Status), snapshot.SourceTimestamp);
                Set(job.Node.ActualLot!, Variant.From(snapshot.ActualLot), snapshot.SourceTimestamp);
                Set(job.Node.ActualOutput!, Variant.From(snapshot.ActualOutput), snapshot.SourceTimestamp);
                SetOptional(job.Node.ActualGoodOutput, snapshot.ActualGoodOutput, snapshot.SourceTimestamp);
                SetOptional(job.Node.ActualBadOutput, snapshot.ActualBadOutput, snapshot.SourceTimestamp);
                SetOptional(job.Node.ActualSampleOutput, snapshot.ActualSampleOutput, snapshot.SourceTimestamp);
                Set(job.Node.ActualOutputRate!, Variant.From(snapshot.ActualOutputRate), snapshot.SourceTimestamp);
                Set(job.Node.GoodProduct!, Variant.From(snapshot.GoodProduct), snapshot.SourceTimestamp);
                job.LastCommit = snapshot;
                RefreshActiveProductionQuality(snapshot.SourceTimestamp);
                job.Node.ClearChangeMasks(m_runtime.SystemContext, true);
                if (previousStatus != snapshot.Status)
                {
                    await EmitJobStatusAsync(group, job, previousStatus, snapshot.Status, snapshot.SourceTimestamp)
                        .ConfigureAwait(false);
                }
                if (previousQuality != snapshot.GoodProduct)
                {
                    await m_binding.ReportProductQualityAsync(job.Node, snapshot.GoodProduct, snapshot.SourceTimestamp)
                        .ConfigureAwait(false);
                }
                for (int index = 0; index < snapshot.FinishedUnits.Count; index++)
                {
                    ExtrusionFinishedUnit unit = snapshot.FinishedUnits[index];
                    UnitFinishedEventState notification =
                        m_runtime.SystemContext.CreateInstanceOfUnitFinishedEventType();
                    notification.JobGroupId!.Value = group.Definition.Id;
                    notification.JobId!.Value = job.Definition.Id;
                    notification.Unit!.Value = unit.Unit;
                    notification.GoodProduct!.Value = unit.GoodProduct;
                    await m_events.ReportAsync(notification, m_unitType, job.Node,
                        snapshot.SourceTimestamp, "Extrusion product unit finished.").ConfigureAwait(false);
                }
                for (int index = 0; index < snapshot.FinishedLots.Count; index++)
                {
                    uint lot = snapshot.FinishedLots[index];
                    LotFinishedEventState notification =
                        m_runtime.SystemContext.CreateInstanceOfLotFinishedEventType();
                    notification.JobGroupId!.Value = group.Definition.Id;
                    notification.JobId!.Value = job.Definition.Id;
                    notification.Lot!.Value = lot;
                    await m_events.ReportAsync(notification, m_lotType, job.Node,
                        snapshot.SourceTimestamp, "Extrusion production lot finished.").ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Requests material using the generated standard event, without adding an invented material record.
        /// </summary>
        public async ValueTask RequestMaterialAsync(
            string materialId,
            DateTimeUtc timestamp,
            CancellationToken cancellationToken = default)
        {
            using ExtrusionOperationLifetime.Operation pending = Link(cancellationToken);
            await m_binding.LineControl!.RequestMaterialAsync(materialId, timestamp,
                pending.Token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        await m_lifetime.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_gate.Dispose();
                    }
                    m_disposed.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposed.TrySetException(failure);
                }
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Registers legal concrete event types and the prescribed single job-event notifier.
        /// </summary>
        internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            using ExtrusionOperationLifetime.Operation pending = Link(cancellationToken);
            Node.EventNotifier = EventNotifiers.SubscribeToEvents;
            m_line.AddNotifier(m_runtime.SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, Node);
            Node.AddNotifier(m_runtime.SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, m_line);
            m_groupType = await m_events.EnsureTypeAsync(Line.ObjectTypeIds.JobGroupStatusChangedEventType,
                "JobGroupStatusChanged", pending.Token).ConfigureAwait(false);
            m_jobType = await m_events.EnsureTypeAsync(Line.ObjectTypeIds.JobStatusChangedEventType,
                "JobStatusChanged", pending.Token).ConfigureAwait(false);
            m_unitType = await m_events.EnsureTypeAsync(Line.ObjectTypeIds.UnitFinishedEventType,
                "UnitFinished", pending.Token).ConfigureAwait(false);
            m_lotType = await m_events.EnsureTypeAsync(Line.ObjectTypeIds.LotFinishedEventType,
                "LotFinished", pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Wires the typed AddJobGroup handler and the identifier-based standard group methods.
        /// </summary>
        private void BindGroupMethods()
        {
            if (Node.AddJobGroup != null)
            {
                Node.AddJobGroup.OnCallAsync = async (
                    context, _, objectId, id, description, equipment, dataset, materials, priority,
                    start, productionTime, setupTime, end, ct) =>
                {
                    if (objectId != Node.NodeId)
                    {
                        return new AddJobGroupMethodStateResult { ServiceResult = StatusCodes.BadNodeIdInvalid };
                    }
                    JobGroupState group = await AddGroupAsync(context, new ExtrusionJobGroupDefinition(
                        id, description, equipment, dataset, materials, priority, start, productionTime, setupTime,
                            end), ct)
                        .ConfigureAwait(false);
                    return new AddJobGroupMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        JobGroupNodeId = group.NodeId
                    };
                };
            }
            BindIdentifier(Node.StartJobGroupById, Node, ExtrusionJobOperation.StartGroup, null);
            BindIdentifier(Node.InterruptJobGroupById, Node, ExtrusionJobOperation.InterruptGroup, null);
            BindIdentifier(Node.FinishJobGroupById, Node, ExtrusionJobOperation.FinishGroup, null);
            BindIdentifier(Node.RemoveJobGroupById, Node, ExtrusionJobOperation.RemoveGroup, null);
        }

        /// <summary>
        /// Wires generated methods on a newly created real group.
        /// </summary>
        private void BindJobMethods(GroupEntry group)
        {
            if (group.Node.AddJob != null)
            {
                group.Node.AddJob.OnCallAsync = async (
                    context, _, objectId, id, description, customer, product, productDescription,
                    strand, sequence, parameters, output, lot, ct) =>
                {
                    if (objectId != group.Node.NodeId)
                    {
                        return new AddJobMethodStateResult { ServiceResult = StatusCodes.BadNodeIdInvalid };
                    }
                    JobState job = await AddJobAsync(context, group.Definition.Id,
                        new ExtrusionJobDefinition(id, description, customer, product, productDescription,
                            strand, sequence, parameters, output, lot), ct).ConfigureAwait(false);
                    return new AddJobMethodStateResult { ServiceResult = ServiceResult.Good, JobNodeId = job.NodeId };
                };
            }
            BindIdentifier(group.Node.StartJobById, group.Node, ExtrusionJobOperation.StartJob, group.Definition.Id);
            BindIdentifier(group.Node.InterruptJobById, group.Node, ExtrusionJobOperation.InterruptJob,
                group.Definition.Id);
            BindIdentifier(group.Node.FinishJobById, group.Node, ExtrusionJobOperation.FinishJob, group.Definition.Id);
            BindIdentifier(group.Node.RemoveJobById, group.Node, ExtrusionJobOperation.RemoveJob, group.Definition.Id);
        }

        /// <summary>
        /// Binds the common single-string shape while retaining each method's exact extrusion operation.
        /// </summary>
        private void BindIdentifier(
            MethodState? method,
            NodeState owner,
            ExtrusionJobOperation operation,
            string? groupId)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2Async = async (context, _, objectId, input, _, ct) =>
            {
                if (objectId != owner.NodeId)
                {
                    return StatusCodes.BadNodeIdInvalid;
                }
                if (input.Count != 1 || !input[0].TryGetValue(out string? id) || string.IsNullOrEmpty(id))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                await ExecuteAsync(context, operation, groupId ?? id, groupId == null ? null : id, ct)
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Enforces authorization-before-execution and never treats missing providers as successful methods.
        /// </summary>
        private async ValueTask<ExtrusionJobResult> CommitAsync(
            ExtrusionJobRequest request,
            CancellationToken cancellationToken)
        {
            if (m_provider == null)
            {
                throw Error(StatusCodes.BadNotSupported, "No authoritative extrusion job provider was supplied.");
            }
            StatusCode permission = await m_provider.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(permission))
            {
                throw new ServiceResultException(permission);
            }
            ExtrusionJobResult result = await m_provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            ValidateStatus(result.CommittedStatus);
            return result;
        }

        /// <summary>
        /// Updates a real group status and emits the required before/after event.
        /// </summary>
        private async ValueTask SetGroupStatusAsync(GroupEntry group, JobStatusEnumeration status,
            DateTimeUtc timestamp)
        {
            JobStatusEnumeration previous = group.Node.Status!.Value;
            Set(group.Node.Status, Variant.From(status), timestamp);
            group.Node.ClearChangeMasks(m_runtime.SystemContext, true);
            if (previous != status)
            {
                JobGroupStatusChangedEventState notification =
                    m_runtime.SystemContext.CreateInstanceOfJobGroupStatusChangedEventType();
                notification.Id!.Value = group.Definition.Id;
                notification.LastStatus!.Value = previous;
                notification.ActiveStatus!.Value = status;
                await m_events.ReportAsync(notification, m_groupType, group.Node,
                    timestamp, "Extrusion job-group status changed.").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates a real job status without changing production quantities.
        /// </summary>
        private async ValueTask SetJobStatusAsync(
            GroupEntry group,
            JobEntry job,
            JobStatusEnumeration status,
            DateTimeUtc timestamp)
        {
            JobStatusEnumeration previous = job.Node.Status!.Value;
            Set(job.Node.Status, Variant.From(status), timestamp);
            RefreshActiveProductionQuality(timestamp);
            job.Node.ClearChangeMasks(m_runtime.SystemContext, true);
            if (previous != status)
            {
                await EmitJobStatusAsync(group, job, previous, status, timestamp).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Recomputes the quality veto after committed job status, value or topology changes.
        /// </summary>
        private void RefreshActiveProductionQuality(DateTimeUtc timestamp)
        {
            bool anyBad = m_groups.Values.SelectMany(entry => entry.Jobs.Values)
                .Any(entry => IsActive(entry.Node.Status!.Value) && !entry.Node.GoodProduct!.Value);
            Volatile.Write(ref m_badActiveProduction, anyBad);
            if (anyBad && m_line.ProductionParameters?.GoodProduct != null)
            {
                Set(m_line.ProductionParameters.GoodProduct, Variant.From(false), timestamp);
                m_line.ProductionParameters.ClearChangeMasks(m_runtime.SystemContext, true);
            }
        }

        /// <summary>
        /// Emits job events from JobGroups while retaining the actual job as SourceNode.
        /// </summary>
        private ValueTask EmitJobStatusAsync(
            GroupEntry group,
            JobEntry job,
            JobStatusEnumeration previous,
            JobStatusEnumeration current,
            DateTimeUtc timestamp)
        {
            JobStatusChangedEventState notification =
                m_runtime.SystemContext.CreateInstanceOfJobStatusChangedEventType();
            notification.JobGroupId!.Value = group.Definition.Id;
            notification.JobId!.Value = job.Definition.Id;
            notification.LastStatus!.Value = previous;
            notification.ActiveStatus!.Value = current;
            return m_events.ReportAsync(notification, m_jobType, job.Node, timestamp, "Extrusion job status changed.");
        }

        /// <summary>
        /// Fills all mandatory group fields from the committed typed definition.
        /// </summary>
        private static void PopulateGroup(
            JobGroupState node,
            ExtrusionJobGroupDefinition definition,
            ArrayOf<ConfigurationParameterType> parameters,
            JobStatusEnumeration status)
        {
            node.Id!.Value = definition.Id;
            node.Description!.Value = definition.Description;
            node.EquipmentDescription!.Value = definition.EquipmentDescription;
            node.ProductionDatasetName!.Value = definition.ProductionDatasetName;
            node.MaterialMapping!.Value = definition.MaterialMapping;
            node.ConfigurationParameters!.Value = parameters;
            node.Priority!.Value = definition.Priority;
            node.PlannedStart!.Value = definition.PlannedStart;
            node.PlannedProductionTime!.Value = definition.PlannedProductionTime;
            node.PlannedSetUpTime!.Value = definition.PlannedSetUpTime;
            node.LatestEnd!.Value = definition.LatestEnd;
            node.Status!.Value = status;
            node.NodeVersion!.Value = "1";
        }

        /// <summary>
        /// Fills all mandatory job fields, retaining exact generated argument/variable spellings.
        /// </summary>
        private static void PopulateJob(JobState node, ExtrusionJobDefinition definition, JobStatusEnumeration status)
        {
            node.Id!.Value = definition.Id;
            node.Description!.Value = definition.Description;
            node.CustomerName!.Value = definition.CustomerName;
            node.ProductId!.Value = definition.ProductId;
            node.ProductDescription!.Value = definition.ProductDescription;
            node.Strand!.Value = definition.Strand;
            node.Sequence!.Value = definition.Sequence;
            node.ParameterSetting!.Value = definition.ParameterSetting;
            node.SetOutput!.Value = definition.SetOutput;
            node.LotSize!.Value = definition.LotSize;
            node.Status!.Value = status;
            node.GoodProduct!.Value = true;
            node.ActualOutput!.Value = 0;
            node.ActualLot!.Value = 0;
            node.ActualOutputRate!.Value = 0;
        }

        /// <summary>
        /// Checks group metadata before provider or address-space side effects.
        /// </summary>
        private static void ValidateGroup(ExtrusionJobGroupDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            RequireText(definition.Id, nameof(definition.Id));
            if (definition.Priority == 0 || !IsNonnegative(definition.PlannedProductionTime) ||
                !IsNonnegative(definition.PlannedSetUpTime) ||
                (definition.PlannedStart != DateTimeUtc.MinValue && definition.LatestEnd != DateTimeUtc.MinValue &&
                    definition.LatestEnd < definition.PlannedStart))
            {
                throw Error(StatusCodes.BadInvalidArgument,
                    "Invalid job-group priority, duration or planned interval.");
            }
            foreach (MaterialMappingType material in definition.MaterialMapping)
            {
                RequireText(material.MaterialId, nameof(material.MaterialId));
            }
        }

        /// <summary>
        /// Checks one-based strand/sequence and finite, nonnegative output quantities.
        /// </summary>
        private static void ValidateJob(ExtrusionJobDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            RequireText(definition.Id, nameof(definition.Id));
            if (definition.Strand == 0 || definition.Sequence == 0 ||
                !IsNonnegative(definition.SetOutput) || !IsNonnegative(definition.LotSize))
            {
                throw Error(StatusCodes.BadInvalidArgument, "Invalid job strand, sequence, set output or lot size.");
            }
        }

        /// <summary>
        /// Enforces OPC 40084-2 standard parameter identifiers and preserves vendor identifiers at or above 100.
        /// </summary>
        private static void ValidateParameters(ArrayOf<ConfigurationParameterType> parameters)
        {
            var seen = new HashSet<uint>();
            foreach (ConfigurationParameterType parameter in parameters)
            {
                if (!seen.Add(parameter.Id) || parameter.Id == 0 ||
                    (parameter.Id > 7 && parameter.Id < 100) ||
                    (parameter.Id <= 7 && !parameter.DefaultValue.TryGetValue(out double _)))
                {
                    throw Error(StatusCodes.BadInvalidArgument,
                        "Invalid, duplicated or reserved configuration parameter.");
                }
            }
        }

        /// <summary>
        /// Applies offered defaults and validates every explicitly supplied configuration value.
        /// </summary>
        private static ArrayOf<ParameterSettingType> ResolveParameters(
            ArrayOf<ConfigurationParameterType> parameters,
            ArrayOf<ParameterSettingType> requested)
        {
            var selected = new Dictionary<uint, Variant>();
            foreach (ParameterSettingType setting in requested)
            {
                if (!selected.TryAdd(setting.Id, setting.Value))
                {
                    throw Error(StatusCodes.BadInvalidArgument, "A configuration parameter was supplied twice.");
                }
            }
            var result = new List<ParameterSettingType>();
            foreach (ConfigurationParameterType parameter in parameters)
            {
                Variant value = selected.TryGetValue(parameter.Id, out Variant supplied)
                    ? supplied : parameter.DefaultValue;
                if (!value.TypeInfo.Equals(parameter.DefaultValue.TypeInfo))
                {
                    throw Error(StatusCodes.BadTypeMismatch,
                        "A parameter setting has the wrong declared type or rank.");
                }
                result.Add(new ParameterSettingType { Id = parameter.Id, Value = value });
                selected.Remove(parameter.Id);
            }
            if (selected.Count != 0)
            {
                throw Error(StatusCodes.BadInvalidArgument, "A parameter is not offered by this job group.");
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Guards invalid state changes and conflicting production on the same strand.
        /// </summary>
        private void ValidateOperation(ExtrusionJobOperation operation, GroupEntry group, JobEntry? job)
        {
            JobStatusEnumeration status = job?.Node.Status!.Value ?? group.Node.Status!.Value;
            if (operation is ExtrusionJobOperation.RemoveGroup or ExtrusionJobOperation.RemoveJob)
            {
                if (IsActive(status) || (job == null && group.Jobs.Values.Any(entry
                    => IsActive(entry.Node.Status!.Value))))
                {
                    throw Error(StatusCodes.BadInvalidState, "Active production cannot be removed.");
                }
            }
            else if (operation is ExtrusionJobOperation.InterruptGroup or ExtrusionJobOperation.InterruptJob)
            {
                if (!IsActive(status))
                {
                    throw Error(StatusCodes.BadInvalidState, "Only an active job or group can be interrupted.");
                }
            }
            else if (operation is ExtrusionJobOperation.StartGroup or ExtrusionJobOperation.StartJob)
            {
                if (IsActive(status) || IsFinished(status))
                {
                    throw Error(StatusCodes.BadInvalidState,
                        "The job or group cannot be started in its current state.");
                }
                if (job != null)
                {
                    if (!IsActive(group.Node.Status!.Value) ||
                        group.Jobs.Values.Any(entry => entry != job && IsActive(entry.Node.Status!.Value) &&
                            entry.Definition.Strand == job.Definition.Strand))
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "The group is inactive or its strand is already occupied.");
                    }
                }
                else if (m_groups.Values.Any(entry => entry != group && IsActive(entry.Node.Status!.Value)))
                {
                    throw Error(StatusCodes.BadInvalidState, "Another job group is already active.");
                }
            }
            else if (operation is not (ExtrusionJobOperation.FinishGroup or ExtrusionJobOperation.FinishJob))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        /// <summary>
        /// Refuses a success result that does not represent the requested operation.
        /// </summary>
        private static void ValidateReportedStatus(ExtrusionJobOperation operation, JobStatusEnumeration status)
        {
            bool valid = operation switch
            {
                ExtrusionJobOperation.StartGroup or ExtrusionJobOperation.StartJob => IsActive(status),
                ExtrusionJobOperation.InterruptGroup or ExtrusionJobOperation.InterruptJob => status is
                    JobStatusEnumeration.SET_UP_INTERRUPTED or JobStatusEnumeration.JOB_INTERRUPTED or
                    JobStatusEnumeration.TEAR_DOWN_INTERRUPTED,
                ExtrusionJobOperation.FinishGroup or ExtrusionJobOperation.FinishJob => IsFinished(status) ||
                    status == JobStatusEnumeration.TEAR_DOWN_ACTIVE,
                _ => true
            };
            if (!valid)
            {
                throw Error(StatusCodes.BadInvalidState, "The provider did not commit the requested job operation.");
            }
        }

        /// <summary>
        /// Validates all quantities before any counter or event is published.
        /// </summary>
        private static void ValidateProduction(ExtrusionJobProductionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            ValidateStatus(snapshot.Status);
            if (!IsNonnegative(snapshot.ActualOutput) || !IsNonnegative(snapshot.ActualGoodOutput) ||
                !IsNonnegative(snapshot.ActualBadOutput) || !IsNonnegative(snapshot.ActualSampleOutput) ||
                !IsNonnegative(snapshot.ActualOutputRate) ||
                snapshot.ActualGoodOutput + snapshot.ActualBadOutput + snapshot.ActualSampleOutput >
                    snapshot.ActualOutput ||
                snapshot.SourceTimestamp == DateTimeUtc.MinValue ||
                snapshot.FinishedUnits.Count + snapshot.FinishedLots.Count > 1000)
            {
                throw Error(StatusCodes.BadInvalidArgument,
                    "Invalid absolute production snapshot or completion batch.");
            }
        }

        /// <summary>
        /// Ensures explicit completion facts are ordered, nonduplicated and consistent with absolute totals.
        /// </summary>
        private static void ValidateCompletionFacts(JobEntry job, ExtrusionJobProductionSnapshot snapshot)
        {
            double previousOutput = job.LastCommit?.ActualOutput ?? 0;
            ArrayOf<uint> previousLots = job.LastCommit?.FinishedLots ?? [];
            uint previousLot = previousLots.IsEmpty ? 0 : previousLots[previousLots.Count - 1];
            foreach (ExtrusionFinishedUnit unit in snapshot.FinishedUnits)
            {
                if (unit.Unit <= previousOutput || unit.Unit > snapshot.ActualOutput)
                {
                    throw Error(StatusCodes.BadInvalidArgument,
                        "Unit completion facts are duplicated or exceed output.");
                }
                previousOutput = unit.Unit;
            }
            foreach (uint lot in snapshot.FinishedLots)
            {
                if (lot <= previousLot || lot > snapshot.ActualLot)
                {
                    throw Error(StatusCodes.BadInvalidArgument,
                        "Lot completion facts are duplicated or exceed the actual lot.");
                }
                previousLot = lot;
            }
        }

        /// <summary>
        /// Recognizes active setup, startup, production and teardown without pretending they are interchangeable.
        /// </summary>
        private static bool IsActive(JobStatusEnumeration status)
        {
            return status is JobStatusEnumeration.SET_UP_ACTIVE or JobStatusEnumeration.START_UP_ACTIVE or
                JobStatusEnumeration.JOB_IN_PRODUCTION or JobStatusEnumeration.TEAR_DOWN_ACTIVE;
        }

        /// <summary>
        /// Recognizes terminal production and teardown states.
        /// </summary>
        private static bool IsFinished(JobStatusEnumeration status)
        {
            return status is JobStatusEnumeration.JOB_FINISHED or JobStatusEnumeration.TEAR_DOWN_FINISHED;
        }

        /// <summary>
        /// Rejects unknown enumeration values rather than publishing undeclared wire values.
        /// </summary>
        private static void ValidateStatus(JobStatusEnumeration status)
        {
            if (status is not (JobStatusEnumeration.OTHER or JobStatusEnumeration.TRANSFERRED_ASSIGNED or
                JobStatusEnumeration.SET_UP_ACTIVE or JobStatusEnumeration.SET_UP_INTERRUPTED or
                JobStatusEnumeration.SET_UP_FINISHED or JobStatusEnumeration.START_UP_ACTIVE or
                JobStatusEnumeration.JOB_IN_PRODUCTION or JobStatusEnumeration.JOB_INTERRUPTED or
                JobStatusEnumeration.JOB_FINISHED or JobStatusEnumeration.TEAR_DOWN_ACTIVE or
                JobStatusEnumeration.TEAR_DOWN_INTERRUPTED or JobStatusEnumeration.TEAR_DOWN_FINISHED))
            {
                throw Error(StatusCodes.BadOutOfRange, "Unknown OPC 40083 job state.");
            }
        }

        /// <summary>
        /// Resolves a group without confusing identifiers from different scopes.
        /// </summary>
        private GroupEntry RequireGroup(string id)
        {
            return m_groups.TryGetValue(id, out GroupEntry? group)
                ? group : throw Error(StatusCodes.BadNotFound, "The job group does not exist.");
        }

        /// <summary>
        /// Resolves a job only within its containing group.
        /// </summary>
        private static JobEntry RequireJob(GroupEntry group, string? id)
        {
            return id != null && group.Jobs.TryGetValue(id, out JobEntry? job)
                ? job : throw Error(StatusCodes.BadNotFound, "The job does not exist in this group.");
        }

        /// <summary>
        /// Chooses the first available normative three-digit placeholder index.
        /// </summary>
        private int NextIndex(NodeState parent, string prefix)
        {
            var occupied = ExtrusionNodeAccess.Children(m_runtime.SystemContext, parent)
                .Select(node => node.BrowseName.Name).ToHashSet(StringComparer.Ordinal);
            for (int index = 1; index <= 999; index++)
            {
                if (!occupied.Contains(prefix + index.ToString("D3", CultureInfo.InvariantCulture)))
                {
                    return index;
                }
            }
            throw Error(StatusCodes.BadTooManyOperations, "The three-digit job container is full.");
        }

        /// <summary>
        /// Publishes the standard structural version after a successful add/remove.
        /// </summary>
        private void UpdateVersion(BaseVariableState variable)
        {
            Set(variable, Variant.From((++m_modelVersion).ToString(CultureInfo.InvariantCulture)), DateTimeUtc.Now);
            variable.ClearChangeMasks(m_runtime.SystemContext, false);
        }

        /// <summary>
        /// Publishes a known protocol value through the same authoritative read store.
        /// </summary>
        private void Set(BaseVariableState variable, Variant value, DateTimeUtc timestamp)
        {
            m_binding.SetProtocolValue(variable, new DataValue(value, StatusCodes.Good, timestamp));
        }

        /// <summary>
        /// Publishes an optional counter only when that standard feature was selected.
        /// </summary>
        private void SetOptional(BaseVariableState? variable, double value, DateTimeUtc timestamp)
        {
            if (variable != null)
            {
                Set(variable, Variant.From(value), timestamp);
            }
        }

        /// <summary>
        /// Removes only the newly created child and preserves both errors when cleanup also fails.
        /// </summary>
        private async ValueTask RollbackAsync(BaseInstanceState node, Exception original)
        {
            m_binding.UnbindVariables(node);
            try
            {
                await m_runtime.RemoveAsync(node).ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Job publication and rollback both failed.", original, cleanup);
            }
        }

        /// <summary>
        /// Checks a required protocol identifier.
        /// </summary>
        private static void RequireText(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Error(StatusCodes.BadInvalidArgument, "A nonempty " + name + " is required.");
            }
        }

        /// <summary>
        /// Rejects NaN, infinities and negative quantities.
        /// </summary>
        private static bool IsNonnegative(double value)
        {
            return value >= 0 && !double.IsInfinity(value);
        }

        /// <summary>
        /// Registers each active or queued operation with this controller's cancellation and drain.
        /// </summary>
        private ExtrusionOperationLifetime.Operation Link(CancellationToken cancellationToken)
        {
            return m_lifetime.Begin(cancellationToken);
        }

        /// <summary>
        /// Rejects work after shutdown begins.
        /// </summary>
        private void ThrowIfDisposed()
        {
            m_lifetime.ThrowIfStopped();
        }

        /// <summary>
        /// Reports a precise UA protocol error.
        /// </summary>
        private static ServiceResultException Error(StatusCode code, string message)
        {
            return ServiceResultException.Create(code, message);
        }

        /// <summary>
        /// Owns a real group and its independently scoped jobs.
        /// </summary>
        private sealed record GroupEntry(
            JobGroupState Node,
            ExtrusionJobGroupDefinition Definition,
            ArrayOf<ConfigurationParameterType> Parameters)
        {
            /// <summary>
            /// Gets jobs keyed by group-local identifier.
            /// </summary>
            internal Dictionary<string, JobEntry> Jobs { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Owns a real job and its last fully published external commit.
        /// </summary>
        private sealed record JobEntry(JobState Node, ExtrusionJobDefinition Definition)
        {
            /// <summary>
            /// Gets or sets the idempotence receipt for absolute production.
            /// </summary>
            internal ExtrusionJobProductionSnapshot? LastCommit { get; set; }
        }

        /// <summary>The hosting owner and model context.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The generated extrusion-line sibling type.</summary>
        private readonly ExtrusionLine_InterfaceState m_line;

        /// <summary>The parent authoritative projection.</summary>
        private readonly ExtrusionDeviceBinding m_binding;

        /// <summary>The explicitly injected hardware/persistence adapter.</summary>
        private readonly IExtrusionJobProvider? m_provider;

        /// <summary>The exact compiled-declaration materializer.</summary>
        private readonly ExtrusionNodeMaterializer m_materializer;

        /// <summary>The single prescribed job-event notifier.</summary>
        private readonly ExtrusionEventEmitter m_events;

        /// <summary>The real job groups.</summary>
        private readonly Dictionary<string, GroupEntry> m_groups = new(StringComparer.Ordinal);

        /// <summary>Protects the public group-list snapshot.</summary>
        private readonly Lock m_sync = new();

        /// <summary>Serializes job protocol operations.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Cancels and drains provider operations, including calls queued at the job gate.</summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionJobController));

        /// <summary>The application-owned concrete group event type.</summary>
        private NodeId m_groupType;

        /// <summary>The application-owned concrete job event type.</summary>
        private NodeId m_jobType;

        /// <summary>The application-owned concrete unit event type.</summary>
        private NodeId m_unitType;

        /// <summary>The application-owned concrete lot event type.</summary>
        private NodeId m_lotType;

        /// <summary>A structural version, not a production counter.</summary>
        private ulong m_modelVersion = 1;

        /// <summary>Claims the single terminal cleanup.</summary>
        private int m_disposalStarted;

        /// <summary>Shares completion after registered calls and gate cleanup have finished.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The aggregate active-strand quality constraint.</summary>
        private bool m_badActiveProduction;
    }
}
