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
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The result of a program run published in the result set of a program manager.
    /// </summary>
    internal sealed class LadsResult : ILadsResult, IAsyncDisposable
    {
        /// <summary>
        /// Creates the result handle.
        /// </summary>
        public LadsResult(
            ResultState node,
            LadsProgramRun run,
            LadsProgramOutcome outcome,
            DateTimeUtc stopped,
            ArrayOf<LadsResultFile> files,
            List<IFileTypeBinding> bindings)
        {
            Node = node;
            RunId = run.RunId;
            TemplateId = run.Template.Id;
            Outcome = outcome;
            Started = run.Started;
            Stopped = stopped;
            Files = files;
            m_bindings = bindings;
        }

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        public string RunId { get; }

        /// <inheritdoc/>
        public string TemplateId { get; }

        /// <inheritdoc/>
        public LadsProgramOutcome Outcome { get; }

        /// <inheritdoc/>
        public DateTimeUtc Started { get; }

        /// <inheritdoc/>
        public DateTimeUtc Stopped { get; }

        /// <inheritdoc/>
        public ArrayOf<LadsResultFile> Files { get; }

        /// <summary>
        /// Gets the node of the result.
        /// </summary>
        public ResultState Node { get; }

        /// <summary>
        /// Releases the file bindings of the result.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            foreach (IFileTypeBinding binding in m_bindings)
            {
                await binding.DisposeAsync().ConfigureAwait(false);
            }
            m_bindings.Clear();
        }

        private readonly List<IFileTypeBinding> m_bindings;
    }

    /// <summary>
    /// The program manager of a functional unit: owns the templates, coordinates the program runs and
    /// publishes their results.
    /// </summary>
    internal sealed class LadsProgramManager : ILadsProgramManager, IAsyncDisposable
    {
        /// <summary>
        /// Creates the program manager and binds its methods.
        /// </summary>
        public LadsProgramManager(LadsFunctionalUnit unit, ProgramManagerState node)
        {
            m_unit = unit;
            Node = node;
            ISystemContext context = Runtime.Context;
            node.AddDownload(context).AddUpload(context).AddRemove(context);
            node.ProgramTemplateSet!.AddNodeVersion(context);
            node.ResultSet!.AddNodeVersion(context);
            ActiveProgramState active = node.ActiveProgram!;
            active.AddCurrentPauseTime(context)
                .AddCurrentRuntime(context)
                .AddCurrentStepName(context)
                .AddCurrentStepNumber(context)
                .AddCurrentStepRuntime(context)
                .AddEstimatedRuntime(context)
                .AddEstimatedStepNumbers(context)
                .AddEstimatedStepRuntime(context)
                .AddDeviceProgramRunId(context)
                .AddCurrentProgramTemplate(context);
            Runtime.Bind(node.Download, OnDownloadAsync);
            Runtime.Bind(node.Upload, OnUploadAsync);
            Runtime.Bind(node.Remove, OnRemoveAsync);
            ResetActiveProgram();
        }

        /// <inheritdoc/>
        public ILadsFunctionalUnit FunctionalUnit => m_unit;

        /// <summary>
        /// Gets the node of the program manager.
        /// </summary>
        public ProgramManagerState Node { get; }

        /// <summary>
        /// Gets or sets the executor of the program runs.
        /// </summary>
        public ILadsProgramExecutor Executor
        {
            get => m_executor ?? Runtime.ProgramExecutor;
            set => m_executor = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <inheritdoc/>
        public ArrayOf<LadsProgramTemplate> Templates
        {
            get
            {
                lock (m_lock)
                {
                    var templates = new List<LadsProgramTemplate>(m_templates.Count);
                    foreach (TemplateEntry entry in m_templates.Values)
                    {
                        templates.Add(entry.Template);
                    }
                    return templates.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<ILadsResult> Results
        {
            get
            {
                lock (m_lock)
                {
                    return m_results.ConvertAll(result => (ILadsResult)result).ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public LadsProgramRun? ActiveRun
        {
            get
            {
                lock (m_lock)
                {
                    return m_active?.Run;
                }
            }
        }

        /// <summary>
        /// Gets whether a program runs.
        /// </summary>
        public bool HasActiveRun
        {
            get
            {
                lock (m_lock)
                {
                    return m_active != null;
                }
            }
        }

        /// <inheritdoc/>
        public event Action<ILadsResult>? ResultAdded;

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        private LadsRuntime Runtime => m_unit.Runtime;

        /// <inheritdoc/>
        public async ValueTask AddTemplateAsync(
            LadsProgramTemplate template,
            CancellationToken cancellationToken = default)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            await m_unit.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ProgramTemplateState node = CreateTemplateNode(template);
                if (m_unit.Device.IsPublished)
                {
                    await Runtime.AddNodeAsync(Node.ProgramTemplateSet!, node, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Node.ProgramTemplateSet!.AddChild(node);
                }
                lock (m_lock)
                {
                    m_templates.Add(template.Id, new TemplateEntry(template, node));
                }
                Runtime.BumpVersion(Node.ProgramTemplateSet);
            }
            finally
            {
                m_unit.Gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveTemplateAsync(
            string templateId,
            CancellationToken cancellationToken = default)
        {
            await m_unit.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TemplateEntry? entry;
                lock (m_lock)
                {
                    if (templateId == null || !m_templates.TryGetValue(templateId, out entry))
                    {
                        return false;
                    }
                    if (m_active != null && m_active.Run.Template.Id == templateId)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "The program template '{0}' is executing.",
                            templateId);
                    }
                    m_templates.Remove(templateId);
                }
                await Runtime.RemoveNodeAsync(Node.ProgramTemplateSet!, entry.Node, cancellationToken)
                    .ConfigureAwait(false);
                Runtime.BumpVersion(Node.ProgramTemplateSet);
                return true;
            }
            finally
            {
                m_unit.Gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<string> StartProgramAsync(
            string templateId,
            ArrayOf<KeyValueType> properties = default,
            string? supervisoryJobId = null,
            string? supervisoryTaskId = null,
            ArrayOf<SampleInfoType> samples = default,
            CancellationToken cancellationToken = default)
        {
            await m_unit.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                m_unit.Device.RequireOperate();
                LadsProgramTemplate template;
                lock (m_lock)
                {
                    if (m_active != null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "The functional unit '{0}' already executes a program.",
                            m_unit.Name);
                    }
                    if (templateId == null || !m_templates.TryGetValue(templateId, out TemplateEntry? entry))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotFound,
                            "The program template '{0}' does not exist.",
                            templateId ?? string.Empty);
                    }
                    template = entry.Template;
                }
                m_unit.Functional.Require(LadsFunctionalStateValue.Stopped);
                string runId = Guid.NewGuid().ToString("N");
                LadsProgramRun? self = null;
                var run = new LadsProgramRun(
                    runId,
                    m_unit,
                    template,
                    properties,
                    supervisoryJobId ?? string.Empty,
                    supervisoryTaskId ?? string.Empty,
                    samples,
                    Runtime.TimeProvider.GetUtcNow(),
                    Runtime.TimeProvider,
                    (number, name) => OnStep(self, number, name));
                self = run;
                await m_unit.TransitionAsync(m_unit.Functional, cancellationToken, LadsFunctionalStateValue.Running)
                    .ConfigureAwait(false);
                m_unit.Running.SetState(LadsRunningStateValue.Idle);
                await m_unit.Running.TransitionThroughAsync(
                    CancellationToken.None,
                    LadsRunningStateValue.Starting,
                    LadsRunningStateValue.Execute).ConfigureAwait(false);
                PublishActiveProgram(run);
                var active = new ActiveRunEntry(run, CancellationTokenSource.CreateLinkedTokenSource(Runtime.Stopping));
                lock (m_lock)
                {
                    m_waiters[runId] = new TaskCompletionSource<ILadsResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Runtime.Logger.ProgramStarted(m_unit.Name, runId, template.Id);
                active.Completion = Task.Run(() => ExecuteAsync(active), CancellationToken.None);
                lock (m_lock)
                {
                    m_active = active;
                }
                return runId;
            }
            finally
            {
                m_unit.Gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ILadsResult> WaitForResultAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<ILadsResult>? waiter;
            lock (m_lock)
            {
                LadsResult? result = m_results.Find(r => r.RunId == runId);
                if (result != null)
                {
                    return result;
                }
                if (runId == null || !m_waiters.TryGetValue(runId, out waiter))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotFound,
                        "The program run '{0}' is unknown.",
                        runId ?? string.Empty);
                }
            }
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(waiter.Task, cancelled.Task).ConfigureAwait(false);
                if (!ReferenceEquals(completed, waiter.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await waiter.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveResultAsync(string runId, CancellationToken cancellationToken = default)
        {
            await m_unit.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LadsResult? result;
                lock (m_lock)
                {
                    result = m_results.Find(r => r.RunId == runId);
                    if (result == null)
                    {
                        return false;
                    }
                    m_results.Remove(result);
                }
                await ReleaseResultAsync(result, cancellationToken).ConfigureAwait(false);
                Runtime.BumpVersion(Node.ResultSet);
                return true;
            }
            finally
            {
                m_unit.Gate.Release();
            }
        }

        /// <summary>
        /// Adds a template while the device is built.
        /// </summary>
        internal void AddTemplateNode(LadsProgramTemplate template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            ProgramTemplateState node = CreateTemplateNode(template);
            Node.ProgramTemplateSet!.AddChild(node);
            lock (m_lock)
            {
                m_templates.Add(template.Id, new TemplateEntry(template, node));
            }
        }

        /// <summary>
        /// Ends the active run with an outcome and waits until its result is published. Returns false
        /// when no program runs.
        /// </summary>
        internal async ValueTask<bool> EndActiveRunAsync(
            LadsProgramOutcome outcome,
            CancellationToken cancellationToken)
        {
            ActiveRunEntry? active;
            lock (m_lock)
            {
                active = m_active;
            }
            if (active == null)
            {
                return false;
            }
            active.RequestEnd(outcome);
            Task? completion = active.Completion;
            if (completion != null)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    Task completed = await Task.WhenAny(completion, cancelled.Task).ConfigureAwait(false);
                    if (!ReferenceEquals(completed, completion))
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Handles the StartProgram method of the functional unit state machine.
        /// </summary>
        internal async ValueTask<ServiceResult> OnStartProgramAsync(
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 5 || !inputs[0].TryGetValue(out string templateId))
            {
                return StatusCodes.BadArgumentsMissing;
            }
            inputs[1].TryGetStructure(out ArrayOf<KeyValueType> properties);
            inputs[2].TryGetValue(out string jobId);
            inputs[3].TryGetValue(out string taskId);
            inputs[4].TryGetStructure(out ArrayOf<SampleInfoType> samples);
            string runId = await StartProgramAsync(
                templateId,
                properties,
                jobId,
                taskId,
                samples,
                cancellationToken).ConfigureAwait(false);
            outputs.Add(Variant.From(runId));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Cancels the active run and releases the results.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            ActiveRunEntry? active;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                active = m_active;
            }
            if (active != null)
            {
                active.RequestEnd(LadsProgramOutcome.Aborted);
                if (active.Completion != null)
                {
                    await active.Completion.ConfigureAwait(false);
                }
            }
            LadsResult[] results;
            lock (m_lock)
            {
                results = [.. m_results];
                m_results.Clear();
            }
            foreach (LadsResult result in results)
            {
                await result.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Executes the run and publishes its result.
        /// </summary>
        private async Task ExecuteAsync(ActiveRunEntry active)
        {
            LadsProgramOutcome outcome;
            try
            {
                await Executor.ExecuteAsync(active.Run, active.Cancellation.Token).ConfigureAwait(false);
                outcome = LadsProgramOutcome.Completed;
            }
            catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
            {
                outcome = active.RequestedOutcome;
            }
            catch (Exception ex)
            {
                Runtime.Logger.ProgramFailed(active.Run.RunId, ex);
                outcome = LadsProgramOutcome.Aborted;
            }
            try
            {
                await FinishAsync(active, outcome).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Runtime.Logger.ProgramFailed(active.Run.RunId, ex);
                TaskCompletionSource<ILadsResult>? waiter;
                lock (m_lock)
                {
                    m_active = null;
                    m_waiters.TryGetValue(active.Run.RunId, out waiter);
                    m_waiters.Remove(active.Run.RunId);
                }
                waiter?.TrySetException(ex);
            }
            finally
            {
                active.Cancellation.Dispose();
            }
        }

        /// <summary>
        /// Moves the state machines to the end state of the run and publishes the result.
        /// </summary>
        private async Task FinishAsync(ActiveRunEntry active, LadsProgramOutcome outcome)
        {
            LadsProgramRun run = active.Run;
            await m_unit.Gate.WaitAsync().ConfigureAwait(false);
            LadsResult? result = null;
            LadsResult[] pruned = [];
            try
            {
                DateTimeUtc stopped = Runtime.TimeProvider.GetUtcNow();
                switch (outcome)
                {
                    case LadsProgramOutcome.Completed:
                        await ForceAsync(
                            m_unit.Running,
                            LadsRunningStateValue.Completing,
                            LadsRunningStateValue.Complete).ConfigureAwait(false);
                        await ForceAsync(
                            m_unit.Functional,
                            LadsFunctionalStateValue.Stopping,
                            LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                        m_unit.Device.IncrementOperationCycles();
                        break;
                    case LadsProgramOutcome.Stopped:
                        await ForceAsync(
                            m_unit.Functional,
                            LadsFunctionalStateValue.Stopping,
                            LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                        m_unit.Running.SetState(LadsRunningStateValue.Idle);
                        break;
                    default:
                        await ForceAsync(
                            m_unit.Functional,
                            LadsFunctionalStateValue.Aborting,
                            LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
                        m_unit.Running.SetState(LadsRunningStateValue.Idle);
                        break;
                }
                m_unit.Device.AddOperationDuration(stopped - run.Started);
                UpdateRuntime(run);
                if (!Runtime.Stopping.IsCancellationRequested)
                {
                    result = CreateResult(run, outcome, stopped);
                    try
                    {
                        await Runtime.AddNodeAsync(Node.ResultSet!, result.Node, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        await result.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                    lock (m_lock)
                    {
                        m_results.Add(result);
                        int excess = m_results.Count - Runtime.Options.MaxRetainedResults;
                        if (excess > 0)
                        {
                            pruned = [.. m_results.GetRange(0, excess)];
                            m_results.RemoveRange(0, excess);
                        }
                    }
                    foreach (LadsResult old in pruned)
                    {
                        await ReleaseResultAsync(old, CancellationToken.None).ConfigureAwait(false);
                    }
                    Runtime.BumpVersion(Node.ResultSet);
                }
            }
            finally
            {
                TaskCompletionSource<ILadsResult>? waiter;
                lock (m_lock)
                {
                    m_active = null;
                    m_waiters.TryGetValue(run.RunId, out waiter);
                    m_waiters.Remove(run.RunId);
                }
                m_unit.Gate.Release();
                Runtime.Logger.ProgramEnded(m_unit.Name, run.RunId, outcome);
                if (result != null)
                {
                    waiter?.TrySetResult(result);
                    ResultAdded?.Invoke(result);
                }
                else
                {
                    waiter?.TrySetCanceled();
                }
            }
        }

        /// <summary>
        /// Applies transitions, forcing the final state when a provider vetoes them.
        /// </summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        private static async ValueTask ForceAsync<TEnum>(
            StateMachines.LadsStateMachine<TEnum> machine,
            params TEnum[] states)
            where TEnum : struct, Enum
        {
            bool applied = false;
            try
            {
                applied = await machine.TransitionThroughAsync(CancellationToken.None, states)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                applied = false;
            }
            if (!applied)
            {
                machine.SetState(states[states.Length - 1]);
            }
        }

        /// <summary>
        /// Creates the node of a result, including its files.
        /// </summary>
        private LadsResult CreateResult(LadsProgramRun run, LadsProgramOutcome outcome, DateTimeUtc stopped)
        {
            ISystemContext context = Runtime.Context;
            NodeState set = Node.ResultSet!;
            ResultState node = context.CreateInstanceOfResultType(set, Runtime.InstanceName(run.RunId));
            node.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            node.AddDeviceProgramRunId(context)
                .AddTotalRuntime(context)
                .AddTotalPauseTime(context)
                .AddEstimatedRuntime(context);
            node.ApplicationUri!.Value = Runtime.Manager.Server.ServerUris.GetString(0) ?? string.Empty;
            node.Description!.Value = new LocalizedText(run.Template.Id + " (" + outcome + ")");
            node.SupervisoryJobId!.Value = run.SupervisoryJobId;
            node.SupervisoryTaskId!.Value = run.SupervisoryTaskId;
            node.Samples!.Value = run.Samples;
            node.Started!.Value = run.Started;
            node.Stopped!.Value = stopped;
            node.User!.Value = string.Empty;
            node.Properties!.Value = run.Properties;
            node.DeviceProgramRunId!.Value = run.RunId;
            node.TotalRuntime!.Value = ((DateTime)stopped - (DateTime)run.Started).TotalMilliseconds;
            node.TotalPauseTime!.Value = 0.0;
            node.EstimatedRuntime!.Value = run.Template.EstimatedRuntime.TotalMilliseconds;
            FillTemplate(node.ProgramTemplate!, run.Template);
            VariableSetState variables = node.VariableSet!;
            foreach (KeyValueType variable in run.Variables)
            {
                if (string.IsNullOrEmpty(variable.Key))
                {
                    continue;
                }
                BaseDataVariableState<string> state = BaseDataVariableState<string>.With<VariantBuilder>(variables);
                state.SymbolicName = variable.Key;
                state.BrowseName = Runtime.InstanceName(variable.Key!);
                state.DisplayName = new LocalizedText(variable.Key);
                state.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                state.TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType;
                state.DataType = global::Opc.Ua.DataTypeIds.String;
                state.ValueRank = ValueRanks.Scalar;
                state.AccessLevel = AccessLevels.CurrentRead;
                state.UserAccessLevel = AccessLevels.CurrentRead;
                state.Value = variable.Value ?? string.Empty;
                variables.AddChild(state);
            }
            var bindings = new List<IFileTypeBinding>();
            var files = new List<LadsResultFile>();
            ResultFileSetState fileSet = node.FileSet!;
            foreach (LadsResultFile file in run.Files)
            {
                if (file.Content.Length > Runtime.Options.MaxResultFileBytes)
                {
                    Runtime.Logger.ResultFileSkipped(m_unit.Name, file.Name);
                    continue;
                }
                ResultFileState fileNode = context.CreateInstanceOfResultFileType(
                    fileSet,
                    Runtime.InstanceName(file.Name));
                fileNode.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                fileNode.AddFile(context);
                fileNode.MimeType!.Value = file.MimeType;
                fileNode.Name!.Value = file.Name;
                fileSet.AddChild(fileNode);
                bindings.Add(Runtime.FileBinder.Bind(
                    fileNode.File!,
                    context,
                    file.Content,
                    maximumBytes: Runtime.Options.MaxResultFileBytes));
                files.Add(file);
            }
            return new LadsResult(node, run, outcome, stopped, files.ToArray(), bindings);
        }

        /// <summary>
        /// Removes the node of a result and releases its files.
        /// </summary>
        private async ValueTask ReleaseResultAsync(LadsResult result, CancellationToken cancellationToken)
        {
            try
            {
                await result.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await Runtime.RemoveNodeAsync(Node.ResultSet!, result.Node, cancellationToken).ConfigureAwait(false);
                Runtime.Logger.ResultRemoved(m_unit.Name, result.Name);
            }
        }

        /// <summary>
        /// Creates the node of a template.
        /// </summary>
        private ProgramTemplateState CreateTemplateNode(LadsProgramTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template.Id) || template.Id.Contains('/', StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "The program template id '{0}' is invalid.",
                    template.Id);
            }
            lock (m_lock)
            {
                if (m_templates.ContainsKey(template.Id))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "The program template '{0}' already exists.",
                        template.Id);
                }
            }
            ISystemContext context = Runtime.Context;
            NodeState set = Node.ProgramTemplateSet!;
            ProgramTemplateState node = context.CreateInstanceOfProgramTemplateType(
                set,
                Runtime.InstanceName(template.Id));
            node.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            FillTemplate(node, template);
            return node;
        }

        /// <summary>
        /// Writes the properties of a template node.
        /// </summary>
        private void FillTemplate(ProgramTemplateState node, LadsProgramTemplate template)
        {
            DateTimeUtc now = Runtime.TimeProvider.GetUtcNow();
            node.Author!.Value = template.Author;
            node.Created!.Value = template.Created == DateTimeUtc.MinValue ? now : template.Created;
            node.Modified!.Value = template.Modified == DateTimeUtc.MinValue ? now : template.Modified;
            node.Description!.Value = template.Description;
            node.DeviceTemplateId!.Value = template.Id;
            node.Version!.Value = template.Version;
            if (template.SupervisoryTemplateId != null)
            {
                node.AddSupervisoryTemplateId(Runtime.Context);
                node.SupervisoryTemplateId!.Value = template.SupervisoryTemplateId;
            }
        }

        /// <summary>
        /// Publishes the active program of a starting run.
        /// </summary>
        private void PublishActiveProgram(LadsProgramRun run)
        {
            ActiveProgramState active = Node.ActiveProgram!;
            NodeId templateNode = default;
            lock (m_lock)
            {
                if (m_templates.TryGetValue(run.Template.Id, out TemplateEntry? entry))
                {
                    templateNode = entry.Node.NodeId;
                }
            }
            Runtime.Update(active.DeviceProgramRunId, Variant.From(run.RunId));
            Runtime.Update(active.CurrentProgramTemplate, Variant.FromStructure(new Opc.Ua.AMB.NameNodeIdDataType
            {
                Name = new LocalizedText(run.Template.Id),
                NodeId = templateNode
            }));
            Runtime.Update(active.EstimatedRuntime, Variant.From(run.Template.EstimatedRuntime.TotalMilliseconds));
            Runtime.Update(active.EstimatedStepNumbers, Variant.From((uint)run.Template.Steps.Count));
            Runtime.Update(active.CurrentStepNumber, Variant.From(0u));
            Runtime.Update(active.CurrentStepName, Variant.From(LocalizedText.Null));
            Runtime.Update(active.CurrentRuntime, Variant.From(0.0));
            Runtime.Update(active.CurrentStepRuntime, Variant.From(0.0));
            Runtime.Update(active.EstimatedStepRuntime, Variant.From(0.0));
            Runtime.Update(active.CurrentPauseTime, Variant.From(0.0));
        }

        /// <summary>
        /// Clears the active program.
        /// </summary>
        private void ResetActiveProgram()
        {
            ActiveProgramState active = Node.ActiveProgram!;
            Runtime.Update(active.DeviceProgramRunId, Variant.From(string.Empty));
            Runtime.Update(active.CurrentProgramTemplate, Variant.FromStructure(new Opc.Ua.AMB.NameNodeIdDataType()));
            Runtime.Update(active.EstimatedRuntime, Variant.From(0.0));
            Runtime.Update(active.EstimatedStepNumbers, Variant.From(0u));
            Runtime.Update(active.CurrentStepNumber, Variant.From(0u));
            Runtime.Update(active.CurrentStepName, Variant.From(LocalizedText.Null));
            Runtime.Update(active.CurrentRuntime, Variant.From(0.0));
            Runtime.Update(active.CurrentStepRuntime, Variant.From(0.0));
            Runtime.Update(active.EstimatedStepRuntime, Variant.From(0.0));
            Runtime.Update(active.CurrentPauseTime, Variant.From(0.0));
        }

        /// <summary>
        /// Publishes a step reported by the executor.
        /// </summary>
        private void OnStep(LadsProgramRun? run, uint number, string name)
        {
            ActiveProgramState active = Node.ActiveProgram!;
            DateTimeUtc now = Runtime.TimeProvider.GetUtcNow();
            Runtime.Update(active.CurrentStepNumber, Variant.From(number));
            Runtime.Update(active.CurrentStepName, Variant.From(new LocalizedText(name)));
            Runtime.Update(active.CurrentStepRuntime, Variant.From(0.0));
            if (run != null)
            {
                int index = (int)number - 1;
                ArrayOf<LadsProgramStep> steps = run.Template.Steps;
                if (index >= 0 && index < steps.Count)
                {
                    Runtime.Update(active.EstimatedStepRuntime, Variant.From(steps[index].Duration.TotalMilliseconds));
                }
                Runtime.Update(
                    active.CurrentRuntime,
                    Variant.From(((DateTime)now - (DateTime)run.Started).TotalMilliseconds));
            }
        }

        /// <summary>
        /// Publishes the final runtime of a run.
        /// </summary>
        private void UpdateRuntime(LadsProgramRun run)
        {
            DateTimeUtc now = Runtime.TimeProvider.GetUtcNow();
            Runtime.Update(
                Node.ActiveProgram!.CurrentRuntime,
                Variant.From(((DateTime)now - (DateTime)run.Started).TotalMilliseconds));
        }

        /// <summary>
        /// Handles the Download method: returns the content of a template.
        /// </summary>
        private ValueTask<ServiceResult> OnDownloadAsync(
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 1 || !inputs[0].TryGetValue(out string templateId))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadArgumentsMissing);
            }
            LadsProgramTemplate? template = null;
            lock (m_lock)
            {
                if (templateId != null && m_templates.TryGetValue(templateId, out TemplateEntry? entry))
                {
                    template = entry.Template;
                }
            }
            if (template == null)
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadNotFound);
            }
            outputs.Add(Variant.FromStructure(ArrayOf<KeyValueType>.Empty));
            outputs.Add(Variant.From(template.ToContent()));
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Handles the Upload method: adds a template from its content.
        /// </summary>
        private async ValueTask<ServiceResult> OnUploadAsync(
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 2 || !inputs[1].TryGetValue(out ByteString data))
            {
                return StatusCodes.BadArgumentsMissing;
            }
            inputs[0].TryGetStructure(out ArrayOf<KeyValueType> parameters);
            string? templateId = null;
            foreach (KeyValueType parameter in parameters)
            {
                if (parameter != null && string.Equals(parameter.Key, "TemplateId", StringComparison.Ordinal))
                {
                    templateId = parameter.Value;
                }
            }
            if (string.IsNullOrWhiteSpace(templateId))
            {
                templateId = Guid.NewGuid().ToString("N");
            }
            LadsProgramTemplate template;
            try
            {
                template = LadsProgramTemplate.Parse(templateId!, data);
            }
            catch (FormatException ex)
            {
                return new ServiceResult(StatusCodes.BadDecodingError, new LocalizedText(ex.Message));
            }
            await AddTemplateAsync(template, cancellationToken).ConfigureAwait(false);
            outputs.Add(Variant.From(template.Id));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the Remove method.
        /// </summary>
        private async ValueTask<ServiceResult> OnRemoveAsync(
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count < 1 || !inputs[0].TryGetValue(out string templateId))
            {
                return StatusCodes.BadArgumentsMissing;
            }
            return await RemoveTemplateAsync(templateId, cancellationToken).ConfigureAwait(false)
                ? ServiceResult.Good
                : new ServiceResult(StatusCodes.BadNotFound);
        }

        /// <summary>
        /// Rejects use after the manager was released.
        /// </summary>
        private void ThrowIfDisposed()
        {
            lock (m_lock)
            {
                if (m_disposed || Runtime.Stopping.IsCancellationRequested)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadShutdown,
                        "The functional unit '{0}' is shutting down.",
                        m_unit.Name);
                }
            }
        }

        /// <summary>
        /// A template and its node.
        /// </summary>
        private sealed class TemplateEntry
        {
            /// <summary>
            /// Creates the entry.
            /// </summary>
            public TemplateEntry(LadsProgramTemplate template, ProgramTemplateState node)
            {
                Template = template;
                Node = node;
            }

            /// <summary>
            /// Gets the template.
            /// </summary>
            public LadsProgramTemplate Template { get; }

            /// <summary>
            /// Gets the node.
            /// </summary>
            public ProgramTemplateState Node { get; }
        }

        /// <summary>
        /// The state of the active run.
        /// </summary>
        private sealed class ActiveRunEntry
        {
            /// <summary>
            /// Creates the entry.
            /// </summary>
            public ActiveRunEntry(LadsProgramRun run, CancellationTokenSource cancellation)
            {
                Run = run;
                Cancellation = cancellation;
            }

            /// <summary>
            /// Gets the run.
            /// </summary>
            public LadsProgramRun Run { get; }

            /// <summary>
            /// Gets the cancellation of the run.
            /// </summary>
            public CancellationTokenSource Cancellation { get; }

            /// <summary>
            /// Gets or sets the task that completes after the result was published.
            /// </summary>
            public Task? Completion { get; set; }

            /// <summary>
            /// Gets the outcome requested by a stop or abort.
            /// </summary>
            public LadsProgramOutcome RequestedOutcome
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_requested ?? LadsProgramOutcome.Aborted;
                    }
                }
            }

            /// <summary>
            /// Requests the end of the run. The first request determines the outcome.
            /// </summary>
            public void RequestEnd(LadsProgramOutcome outcome)
            {
                lock (m_lock)
                {
                    m_requested ??= outcome;
                }
                try
                {
                    Cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The run already ended.
                }
            }

            private readonly Lock m_lock = new();
            private LadsProgramOutcome? m_requested;
        }

        private readonly Lock m_lock = new();
        private readonly LadsFunctionalUnit m_unit;
        private readonly Dictionary<string, TemplateEntry> m_templates = new(StringComparer.Ordinal);
        private readonly List<LadsResult> m_results = [];
        private readonly Dictionary<string, TaskCompletionSource<ILadsResult>> m_waiters =
            new(StringComparer.Ordinal);
        private ILadsProgramExecutor? m_executor;
        private ActiveRunEntry? m_active;
        private bool m_disposed;
    }
}
