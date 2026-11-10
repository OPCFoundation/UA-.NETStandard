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
            LadsStoredResultData snapshot,
            List<IFileTypeBinding> bindings)
        {
            Node = node;
            Snapshot = snapshot;
            RunId = snapshot.RunId;
            TemplateId = snapshot.Template.Id;
            Outcome = snapshot.Outcome;
            Started = snapshot.Started;
            Stopped = snapshot.Stopped;
            Files = snapshot.Files;
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
        /// Gets the completed payload detached from the executor's mutable run.
        /// </summary>
        internal LadsStoredResultData Snapshot { get; }

        /// <summary>
        /// Releases the file bindings of the result.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (IFileTypeBinding binding in m_bindings)
            {
                try
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            m_bindings.Clear();
            if (failures.Count > 0)
            {
                throw new AggregateException("The LADS result file cleanup failed.", failures);
            }
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
        public LadsProgramManager(
            LadsFunctionalUnit unit, ProgramManagerState node, LadsTemplateOperations operations)
        {
            m_unit = unit;
            Node = node;
            TemplateOperations = operations;
            ISystemContext context = Runtime.Context;
            if ((operations & LadsTemplateOperations.Download) != 0)
            {
                node.AddDownload(context);
                Runtime.Bind(node.Download, OnDownloadAsync);
            }
            if ((operations & LadsTemplateOperations.Upload) != 0)
            {
                node.AddUpload(context);
                Runtime.Bind(node.Upload, OnUploadAsync);
            }
            if ((operations & LadsTemplateOperations.Remove) != 0)
            {
                node.AddRemove(context);
                Runtime.Bind(node.Remove, OnRemoveAsync);
            }
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
            ResetActiveProgram();
            active.CurrentRuntime!.OnSimpleReadValue = ReadCurrentRuntime;
            active.CurrentStepRuntime!.OnSimpleReadValue = ReadCurrentStepRuntime;
            active.CurrentPauseTime!.OnSimpleReadValue = ReadCurrentPauseTime;
        }

        /// <inheritdoc/>
        public ILadsFunctionalUnit FunctionalUnit => m_unit;

        /// <summary>
        /// Gets the node of the program manager.
        /// </summary>
        public ProgramManagerState Node { get; }

        /// <summary>
        /// Gets the optional client template-transfer operations selected during construction.
        /// </summary>
        internal LadsTemplateOperations TemplateOperations { get; }

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

        /// <inheritdoc/>
        public string? InterruptedRunId => m_lastInterruptedRunId;

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
                m_unit.Device.RequireStorageHealthy();
                ProgramTemplateState node = CreateTemplateNode(template);
                lock (m_lock)
                {
                    if (m_templates.TryGetValue(template.Id, out _))
                    {
                        throw new ArgumentException("The program template identifier is already registered.",
                            nameof(template));
                    }
                }
                LadsStoredFunctionalUnitData stored = CaptureStoredData();
                List<LadsStoredTemplateData> templates = stored.Templates.ToList();
                templates.Add(CaptureTemplate(template, node));
                stored.Templates = templates.ToArray();
                List<string> removed = stored.RemovedTemplateIds.ToList();
                removed.Remove(template.Id);
                stored.RemovedTemplateIds = removed.ToArray();
                await m_unit.Device.CommitProgramDataAsync(stored, false, TimeSpan.Zero, () =>
                {
                    lock (m_lock)
                    {
                        m_templates.Add(template.Id, new TemplateEntry(template, node));
                        m_removedTemplates.Remove(template.Id);
                    }
                }, cancellationToken,
                    () => AddProgramNodeAsync(Node.ProgramTemplateSet!, node),
                    () => Runtime.RemoveNodeAsync(Node.ProgramTemplateSet!, node, CancellationToken.None))
                    .ConfigureAwait(false);
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
                m_unit.Device.RequireStorageHealthy();
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
                }
                LadsStoredFunctionalUnitData stored = CaptureStoredData();
                List<LadsStoredTemplateData> templates = stored.Templates.ToList();
                templates.RemoveAll(template => template.Id == templateId);
                stored.Templates = templates.ToArray();
                List<string> removed = stored.RemovedTemplateIds.ToList();
                removed.Add(templateId);
                stored.RemovedTemplateIds = removed.ToArray();
                await m_unit.Device.CommitProgramDataAsync(stored, false, TimeSpan.Zero, () =>
                {
                    lock (m_lock)
                    {
                        m_templates.Remove(templateId);
                        m_removedTemplates.Add(templateId);
                    }
                }, cancellationToken,
                    () => Runtime.RemoveNodeAsync(Node.ProgramTemplateSet!, entry.Node, CancellationToken.None),
                    () => AddProgramNodeAsync(Node.ProgramTemplateSet!, entry.Node))
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
            bool stateAdmissionGateHeld = false;
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
                    template = CopyTemplate(entry.Template, entry.Node);
                }
                await m_unit.Device.ConfirmReadinessAsync(cancellationToken).ConfigureAwait(false);
                await m_unit.Device.AcquireStateAdmissionAsync(cancellationToken).ConfigureAwait(false);
                stateAdmissionGateHeld = true;
                m_unit.Device.RegisterAdmittedStart(m_unit);
                await m_unit.Device.EnterFunctionalUnitWithAdmissionHeldAsync(m_unit, cancellationToken)
                    .ConfigureAwait(false);
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
                    (number, name) => OnStep(self, number, name),
                    (number, name, notificationToken) =>
                        OnStepAsync(self, number, name, notificationToken));
                self = run;
                await m_unit.TransitionAsync(
                    m_unit.Running,
                    CancellationToken.None,
                    LadsRunningStateValue.Starting,
                    LadsRunningStateValue.Execute).ConfigureAwait(false);
                var active = new ActiveRunEntry(
                    run, CancellationTokenSource.CreateLinkedTokenSource(Runtime.Stopping));
                LadsStoredFunctionalUnitData stored = CaptureStoredData();
                stored.InterruptedRunId = runId;
                bool executionStarted = false;
                try
                {
                    await m_unit.Device.CommitProgramDataAsync(stored, false, TimeSpan.Zero, () =>
                    {
                        lock (m_lock)
                        {
                            m_storedInterruptedRunId = runId;
                        }
                    }, cancellationToken, quarantineOwner: m_unit, stateAdmissionGateHeld: true)
                        .ConfigureAwait(false);
                    m_unit.Device.StartProgram(this, active);
                    await Node.ActiveProgram!.ClearChangeMasksAsync(
                        Runtime.Context, true, CancellationToken.None).ConfigureAwait(false);
                    m_unit.Device.RequireStorageHealthy();
                    Runtime.Logger.ProgramStarted(m_unit.Name, runId, template.Id);
                    _ = m_unit.Device.ScheduleProgramExecution(async () =>
                    {
                        try
                        {
                            await ExecuteAsync(active).ConfigureAwait(false);
                            active.Complete();
                        }
                        catch (Exception failure)
                        {
                            active.Complete(failure);
                        }
                    });
                    executionStarted = true;
                    await m_unit.UpdateRunningExecutabilityAsync(CancellationToken.None).ConfigureAwait(false);
                    m_unit.Device.RequireStorageHealthy();
                }
                catch (Exception startFailure)
                {
                    var failures = new List<Exception> { startFailure };
                    try
                    {
                        if (!executionStarted)
                        {
                            TaskCompletionSource<ILadsResult>? waiter;
                            bool activeProgramPublished;
                            lock (m_lock)
                            {
                                activeProgramPublished = ReferenceEquals(m_active, active);
                                if (activeProgramPublished)
                                {
                                    m_active = null;
                                }
                                m_waiters.TryGetValue(runId, out waiter);
                                m_waiters.Remove(runId);
                            }
                            waiter?.TrySetException(startFailure);
                            active.Cancellation.Dispose();
                            if (activeProgramPublished)
                            {
                                await ResetActiveProgramAsync().ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            active.RequestEnd(LadsProgramOutcome.Aborted);
                        }
                    }
                    catch (Exception cleanupFailure)
                    {
                        failures.Add(cleanupFailure);
                    }
                    try
                    {
                        await m_unit.Device.RecordExecutionFailureAsync(m_unit).ConfigureAwait(false);
                    }
                    catch (Exception stateFailure)
                    {
                        failures.Add(stateFailure);
                    }
                    if (!executionStarted)
                    {
                        active.Complete(failures.Count > 1
                            ? new AggregateException("The LADS program start and cleanup failed.", failures)
                            : startFailure);
                    }
                    if (failures.Count > 1)
                    {
                        throw new AggregateException("The LADS program start and cleanup failed.", failures);
                    }
                    throw;
                }
                return runId;
            }
            finally
            {
                if (stateAdmissionGateHeld)
                {
                    m_unit.Device.ReleaseStateAdmission(m_unit);
                }
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
                m_unit.Device.RequireStorageHealthy();
                LadsResult? result;
                lock (m_lock)
                {
                    result = m_results.Find(r => r.RunId == runId);
                    if (result == null)
                    {
                        return false;
                    }
                }
                LadsStoredFunctionalUnitData stored = CaptureStoredData();
                List<LadsStoredResultData> results = stored.Results.ToList();
                results.RemoveAll(candidate => candidate.RunId == runId);
                stored.Results = results.ToArray();
                await m_unit.Device.CommitProgramDataAsync(stored, false, TimeSpan.Zero, () =>
                {
                    lock (m_lock)
                    {
                        m_results.Remove(result);
                    }
                }, cancellationToken,
                    () => Runtime.RemoveNodeAsync(Node.ResultSet!, result.Node, CancellationToken.None),
                    () => AddProgramNodeAsync(Node.ResultSet!, result.Node)).ConfigureAwait(false);
                await ReleaseResultsAsync([result]).ConfigureAwait(false);
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
        /// Captures completed results and their file bytes as one device-store snapshot.
        /// </summary>
        internal LadsStoredFunctionalUnitData CaptureStoredData(LadsResult? pending = null)
        {
            lock (m_lock)
            {
                var templates = new List<LadsStoredTemplateData>(m_templates.Count);
                foreach (TemplateEntry entry in m_templates.Values)
                {
                    templates.Add(CaptureTemplate(entry.Template, entry.Node));
                }
                var retained = new List<LadsResult>(m_results);
                if (pending != null)
                {
                    retained.Add(pending);
                    int excess = retained.Count - Runtime.Options.MaxRetainedResults;
                    if (excess > 0)
                    {
                        retained.RemoveRange(0, excess);
                    }
                }
                var results = new LadsStoredResultData[retained.Count];
                for (int index = 0; index < results.Length; index++)
                {
                    LadsResult result = retained[index];
                    results[index] = result.Snapshot;
                }
                return new LadsStoredFunctionalUnitData
                {
                    Name = m_unit.Name,
                    Templates = templates.ToArray(),
                    Results = results,
                    RemovedTemplateIds = new List<string>(m_removedTemplates).ToArray(),
                    InterruptedRunId = pending == null ? m_storedInterruptedRunId : null,
                    LastInterruptedRunId = m_lastInterruptedRunId
                };
            }
        }

        /// <summary>
        /// Restores dynamic nodes before registration without starting the executor.
        /// Configured template content wins conflicts; persisted explicit removals remain removed.
        /// </summary>
        internal async ValueTask RestoreStoredDataAsync(
            LadsStoredFunctionalUnitData data,
            CancellationToken cancellationToken)
        {
            ArrayOf<string> removed = data.RemovedTemplateIds;
            for (int index = 0; index < removed.Count; index++)
            {
                string id = removed[index];
                if (m_templates.TryGetValue(id, out TemplateEntry? entry))
                {
                    Node.ProgramTemplateSet!.RemoveChild(entry.Node);
                    m_templates.Remove(id);
                }
                m_removedTemplates.Add(id);
            }
            ArrayOf<LadsStoredTemplateData> templates = data.Templates;
            for (int index = 0; index < templates.Count; index++)
            {
                LadsStoredTemplateData stored = templates[index];
                if (m_templates.TryGetValue(stored.Id, out TemplateEntry? configured))
                {
                    if (configured.Template.Created == DateTimeUtc.MinValue)
                    {
                        configured.Template.Created = stored.Created;
                        configured.Node.Created!.Value = stored.Created;
                    }
                    if (configured.Template.Modified == DateTimeUtc.MinValue &&
                        configured.Template.ToContent().Equals(stored.Content))
                    {
                        configured.Template.Modified = stored.Modified;
                        configured.Node.Modified!.Value = stored.Modified;
                    }
                }
                else if (!m_removedTemplates.Contains(stored.Id))
                {
                    AddTemplateNode(RestoreTemplate(stored));
                }
            }
            ArrayOf<LadsStoredResultData> results = data.Results;
            int first = Math.Max(0, results.Count - Runtime.Options.MaxRetainedResults);
            for (int index = first; index < results.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LadsStoredResultData stored = results[index];
                var run = new LadsProgramRun(stored.RunId, m_unit, RestoreTemplate(stored.Template),
                    stored.Properties, stored.SupervisoryJobId, stored.SupervisoryTaskId, stored.Samples,
                    stored.Started, Runtime.TimeProvider, static (_, _) => { },
                    static (_, _, _) => default);
                ArrayOf<LadsResultFile> files = stored.Files;
                for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
                {
                    LadsResultFile file = files[fileIndex];
                    if (file.Content.Length > Runtime.Options.MaxResultFileBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    run.AddResultFile(file.Name, file.MimeType, file.Content);
                }
                ArrayOf<KeyValueType> variables = stored.Variables;
                for (int variableIndex = 0; variableIndex < variables.Count; variableIndex++)
                {
                    KeyValueType variable = variables[variableIndex];
                    run.SetResultVariable(variable.Key ?? string.Empty, variable.Value ?? string.Empty);
                }
                run.ExecutionFinished();
                LadsResult result = await CreateResultAsync(run, stored.Outcome, stored.Stopped, stored)
                    .ConfigureAwait(false);
                Node.ResultSet!.AddChild(result.Node);
                m_results.Add(result);
            }
            m_storedInterruptedRunId = data.InterruptedRunId;
            m_lastInterruptedRunId = data.InterruptedRunId ?? data.LastInterruptedRunId;
        }

        /// <summary>
        /// Clears the recovered pending marker after the device's readiness acknowledgement has been saved.
        /// </summary>
        internal void AcknowledgeInterruptedExecution()
        {
            lock (m_lock)
            {
                m_storedInterruptedRunId = null;
            }
        }

        /// <summary>
        /// Copies the recipe and published metadata without a text-format round trip.
        /// </summary>
        private static LadsProgramTemplate CopyTemplate(LadsProgramTemplate template, ProgramTemplateState node)
        {
            var copy = new LadsProgramTemplate(template.Id)
            {
                Author = template.Author,
                Version = template.Version,
                Description = template.Description,
                SupervisoryTemplateId = template.SupervisoryTemplateId,
                Created = node.Created!.Value,
                Modified = node.Modified!.Value
            };
            foreach (LadsProgramStep step in template.Steps)
            {
                var setpoints = new LadsSetpoint[step.Setpoints.Count];
                for (int index = 0; index < setpoints.Length; index++)
                {
                    LadsSetpoint setpoint = step.Setpoints[index];
                    setpoints[index] = new LadsSetpoint(setpoint.FunctionName, setpoint.Value);
                }
                copy.Step(new LadsProgramStep(step.Name, step.Duration, setpoints));
            }
            return copy;
        }

        /// <summary>
        /// Captures template content and the metadata actually published on its node.
        /// </summary>
        private static LadsStoredTemplateData CaptureTemplate(
            LadsProgramTemplate template, ProgramTemplateState node)
        {
            return new LadsStoredTemplateData
            {
                Id = template.Id,
                Content = template.ToContent(),
                Author = template.Author,
                Version = template.Version,
                Description = template.Description,
                SupervisoryTemplateId = template.SupervisoryTemplateId,
                Created = node.Created!.Value,
                Modified = node.Modified!.Value
            };
        }

        /// <summary>
        /// Reconstructs a template without applying any setpoints.
        /// </summary>
        private static LadsProgramTemplate RestoreTemplate(LadsStoredTemplateData stored)
        {
            LadsProgramTemplate template = LadsProgramTemplate.Parse(stored.Id, stored.Content);
            template.Author = stored.Author;
            template.Version = stored.Version;
            template.Description = stored.Description;
            template.SupervisoryTemplateId = stored.SupervisoryTemplateId;
            template.Created = stored.Created;
            template.Modified = stored.Modified;
            return template;
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
            bool accepted = active.RequestEnd(outcome);
            Task completion = active.Completion;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(completion, cancelled.Task).ConfigureAwait(false);
                if (!ReferenceEquals(completed, completion))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            await completion.ConfigureAwait(false);
            return accepted;
        }

        /// <summary>
        /// Requests abort when device storage is quarantined and returns the run lifetime for
        /// callers that must drain an admitted start before publishing device shutdown.
        /// </summary>
        internal (Task? Completion, Exception? Failure) RequestQuarantineAbort()
        {
            ActiveRunEntry? active;
            lock (m_lock)
            {
                active = m_active;
            }
            if (active == null)
            {
                return (null, null);
            }
            try
            {
                active.RequestEnd(LadsProgramOutcome.Aborted);
                return (active.Completion, null);
            }
            catch (Exception failure)
            {
                return (active.Completion, failure);
            }
        }

        /// <summary>
        /// Applies execution control with cancellation linked to Stop, Abort and server shutdown.
        /// </summary>
        internal async ValueTask<ServiceResult> ChangeExecutionAsync(
            LadsProgramRun run,
            LadsRunningStateValue target,
            CancellationToken cancellationToken)
        {
            ActiveRunEntry active;
            lock (m_lock)
            {
                active = m_active ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            if (!ReferenceEquals(active.Run, run) || Executor is not ILadsControlledProgramExecutor executor)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, active.Cancellation.Token);
            try
            {
                ServiceResult acknowledgment = await executor.ChangeExecutionAsync(run, target, linked.Token)
                    .ConfigureAwait(false);
                if (target == LadsRunningStateValue.Completing && ServiceResult.IsBad(acknowledgment))
                {
                    active.RecordExecutionFailure();
                }
                return acknowledgment;
            }
            catch (OperationCanceledException failure) when (linked.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The LADS execution-control request was cancelled.", failure, linked.Token);
            }
            catch
            {
                if (target == LadsRunningStateValue.Completing)
                {
                    active.RecordExecutionFailure();
                }
                throw;
            }
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
            var failures = new List<Exception>();
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
                try
                {
                    active.RequestEnd(LadsProgramOutcome.Aborted);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
                try
                {
                    await active.Completion.ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            LadsResult[] results;
            lock (m_lock)
            {
                results = [.. m_results];
                m_results.Clear();
            }
            try
            {
                await ReleaseResultsAsync(results).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("The LADS program manager cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Executes the run and publishes its result.
        /// </summary>
        private async Task ExecuteAsync(ActiveRunEntry active)
        {
            LadsProgramOutcome outcome;
            bool executionFailed = false;
            try
            {
                m_unit.Device.RequireStorageHealthy();
                await Executor.ExecuteAsync(active.Run, active.Cancellation.Token).ConfigureAwait(false);
                if (active.Cancellation.IsCancellationRequested)
                {
                    outcome = active.RequestedOutcome;
                }
                else
                {
                    active.Run.ExecutionFinished();
                    bool acknowledged = await m_unit.WaitForCompletionAcknowledgmentAsync(active.Cancellation.Token)
                        .ConfigureAwait(false);
                    executionFailed = !acknowledged;
                    outcome = active.GetCompletionOutcome(acknowledged);
                }
            }
            catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
            {
                outcome = active.RequestedOutcome;
            }
            catch (Exception ex)
            {
                Runtime.Logger.ProgramFailed(active.Run.RunId, ex);
                outcome = LadsProgramOutcome.Aborted;
                executionFailed = true;
            }
            active.Run.ExecutionFinished();
            if (outcome == LadsProgramOutcome.Completed &&
                m_unit.Running.Current is not (LadsRunningStateValue.Execute or LadsRunningStateValue.Completing))
            {
                outcome = LadsProgramOutcome.Aborted;
                executionFailed = true;
            }
            try
            {
                await FinishAsync(active, outcome, executionFailed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Runtime.Logger.ProgramFailed(active.Run.RunId, ex);
                TaskCompletionSource<ILadsResult>? waiter;
                lock (m_lock)
                {
                    if (ReferenceEquals(m_active, active))
                    {
                        m_active = null;
                    }
                    m_waiters.TryGetValue(active.Run.RunId, out waiter);
                }
                waiter?.TrySetException(ex);
                throw;
            }
            finally
            {
                active.Cancellation.Dispose();
            }
        }

        /// <summary>
        /// Moves the state machines to the end state of the run and publishes the result.
        /// </summary>
        private async Task FinishAsync(ActiveRunEntry active, LadsProgramOutcome outcome, bool executionFailed)
        {
            LadsProgramRun run = active.Run;
            await m_unit.Gate.WaitAsync().ConfigureAwait(false);
            LadsResult? result = null;
            LadsResult[] pruned = [];
            Exception? failure = null;
            try
            {
                outcome = active.BeginFinalization(outcome, executionFailed);
                if (outcome != LadsProgramOutcome.Completed)
                {
                    m_unit.CancelExecutionControl();
                }
                DateTimeUtc stopped = Runtime.TimeProvider.GetUtcNow();
                switch (outcome)
                {
                    case LadsProgramOutcome.Completed:
                        if (!m_unit.CompleteLifecycle)
                        {
                            await ForceAsync(
                                m_unit.Running,
                                LadsRunningStateValue.Completing,
                                LadsRunningStateValue.Complete).ConfigureAwait(false);
                        }
                        else if (m_unit.Running.Current != LadsRunningStateValue.Completing)
                        {
                            await m_unit.TransitionAsync(
                                m_unit.Running, CancellationToken.None, LadsRunningStateValue.Completing)
                                .ConfigureAwait(false);
                        }
                        if (m_unit.CompleteLifecycle)
                        {
                            await m_unit.TransitionAsync(
                                m_unit.Running, CancellationToken.None, LadsRunningStateValue.Complete)
                                .ConfigureAwait(false);
                        }
                        if (!m_unit.CompleteLifecycle)
                        {
                            await ForceAsync(
                                m_unit.Functional,
                                LadsFunctionalStateValue.Stopping,
                                LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                        }
                        break;
                    case LadsProgramOutcome.Stopped:
                        await ForceAsync(
                            m_unit.Functional,
                            LadsFunctionalStateValue.Stopping,
                            LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                        await m_unit.Running.SetStateAsync(
                            LadsRunningStateValue.Idle, CancellationToken.None).ConfigureAwait(false);
                        break;
                    default:
                        await ForceAsync(
                            m_unit.Functional,
                            LadsFunctionalStateValue.Aborting,
                            LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
                        await m_unit.Running.SetStateAsync(
                            LadsRunningStateValue.Idle, CancellationToken.None).ConfigureAwait(false);
                        break;
                }
                await UpdateRuntimeAsync(run).ConfigureAwait(false);
                if (!Runtime.Stopping.IsCancellationRequested)
                {
                    result = await CreateResultAsync(run, outcome, stopped).ConfigureAwait(false);
                    bool committed = false;
                    try
                    {
                        lock (m_lock)
                        {
                            int excess = m_results.Count + 1 - Runtime.Options.MaxRetainedResults;
                            if (excess > 0)
                            {
                                pruned = [.. m_results.GetRange(0, excess)];
                            }
                        }
                        await m_unit.Device.CommitProgramDataAsync(
                            CaptureStoredData(result),
                            outcome == LadsProgramOutcome.Completed,
                            run.CurrentRuntime + run.TotalPauseTime,
                            () =>
                            {
                                lock (m_lock)
                                {
                                    m_results.Add(result);
                                    m_storedInterruptedRunId = null;
                                    if (pruned.Length > 0)
                                    {
                                        m_results.RemoveRange(0, pruned.Length);
                                    }
                                }
                            },
                            CancellationToken.None,
                            async () =>
                            {
                                await AddProgramNodeAsync(Node.ResultSet!, result.Node).ConfigureAwait(false);
                                foreach (LadsResult old in pruned)
                                {
                                    await Runtime.RemoveNodeAsync(
                                        Node.ResultSet!, old.Node, CancellationToken.None).ConfigureAwait(false);
                                }
                            },
                            () => RestoreResultNodesAsync(result, pruned),
                            quarantineOwner: m_unit).ConfigureAwait(false);
                        committed = true;
                    }
                    catch (Exception publicationFailure)
                    {
                        if (!committed)
                        {
                            try
                            {
                                await result.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception cleanupFailure)
                            {
                                throw new AggregateException(publicationFailure, cleanupFailure);
                            }
                        }
                        throw;
                    }
                    await ReleaseResultsAsync(pruned).ConfigureAwait(false);
                    Runtime.BumpVersion(Node.ResultSet);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                try
                {
                    await m_unit.Device.RecordExecutionFailureAsync(m_unit).ConfigureAwait(false);
                }
                catch (Exception stateFailure)
                {
                    var combined = new AggregateException(ex, stateFailure);
                    failure = combined;
                    throw combined;
                }
                throw;
            }
            finally
            {
                TaskCompletionSource<ILadsResult>? waiter;
                lock (m_lock)
                {
                    m_active = null;
                    m_waiters.TryGetValue(run.RunId, out waiter);
                    if (failure == null)
                    {
                        m_waiters.Remove(run.RunId);
                    }
                    else
                    {
                        if (m_lastFailedRunId != null)
                        {
                            m_waiters.Remove(m_lastFailedRunId);
                        }
                        m_lastFailedRunId = run.RunId;
                    }
                }
                m_unit.UpdateRunningExecutability();
                m_unit.Gate.Release();
                Runtime.Logger.ProgramEnded(
                    m_unit.Name, run.RunId, failure == null ? outcome : LadsProgramOutcome.Aborted);
                if (failure != null)
                {
                    waiter?.TrySetException(failure);
                }
                else if (result != null)
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
                await machine.SetStateAsync(states[states.Length - 1], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates the node of a result, including its files.
        /// </summary>
        private async ValueTask<LadsResult> CreateResultAsync(
            LadsProgramRun run,
            LadsProgramOutcome outcome,
            DateTimeUtc stopped,
            LadsStoredResultData? restored = null)
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
            var samples = new SampleInfoType[run.Samples.Count];
            for (int index = 0; index < samples.Length; index++)
            {
                SampleInfoType sample = run.Samples[index];
                samples[index] = new SampleInfoType
                {
                    ContainerId = sample.ContainerId,
                    SampleId = sample.SampleId,
                    Position = sample.Position,
                    CustomData = sample.CustomData
                };
            }
            node.Samples!.Value = samples;
            node.Started!.Value = run.Started;
            node.Stopped!.Value = stopped;
            node.User!.Value = string.Empty;
            var properties = new KeyValueType[run.Properties.Count];
            for (int index = 0; index < properties.Length; index++)
            {
                KeyValueType property = run.Properties[index];
                properties[index] = new KeyValueType { Key = property.Key, Value = property.Value };
            }
            node.Properties!.Value = properties;
            node.DeviceProgramRunId!.Value = run.RunId;
            node.TotalRuntime!.Value = restored?.TotalRuntimeMilliseconds ??
                (run.CurrentRuntime + run.TotalPauseTime).TotalMilliseconds;
            node.TotalPauseTime!.Value = restored?.TotalPauseTimeMilliseconds ?? run.TotalPauseTime.TotalMilliseconds;
            node.EstimatedRuntime!.Value = run.Template.EstimatedRuntime.TotalMilliseconds;
            FillTemplate(node.ProgramTemplate!, run.Template);
            VariableSetState variables = node.VariableSet!;
            ArrayOf<KeyValueType> resultVariables = run.Variables;
            foreach (KeyValueType variable in resultVariables)
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
            var snapshot = new LadsStoredResultData
            {
                RunId = run.RunId,
                Template = CaptureTemplate(run.Template, node.ProgramTemplate!),
                Outcome = outcome,
                Started = run.Started,
                Stopped = stopped,
                Properties = properties,
                Samples = samples,
                SupervisoryJobId = run.SupervisoryJobId,
                SupervisoryTaskId = run.SupervisoryTaskId,
                Variables = resultVariables,
                TotalRuntimeMilliseconds = node.TotalRuntime.Value,
                TotalPauseTimeMilliseconds = node.TotalPauseTime.Value
            };
            try
            {
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
                snapshot.Files = files.ToArray();
                return new LadsResult(node, snapshot, bindings);
            }
            catch (Exception creationFailure)
            {
                try
                {
                    await new LadsResult(node, snapshot, bindings).DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(creationFailure, cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>
        /// Publishes a dynamic node, or attaches it while the device is being composed.
        /// </summary>
        private ValueTask AddProgramNodeAsync(BaseInstanceState parent, BaseInstanceState node)
        {
            if (m_unit.Device.IsPublished)
            {
                return Runtime.AddNodeAsync(parent, node, CancellationToken.None);
            }
            parent.RemoveChild(node);
            parent.AddChild(node);
            return default;
        }

        /// <summary>
        /// Attempts every node compensation without abandoning later retained results.
        /// </summary>
        private async ValueTask RestoreResultNodesAsync(LadsResult result, LadsResult[] pruned)
        {
            var failures = new List<Exception>();
            try
            {
                await Runtime.RemoveNodeAsync(Node.ResultSet!, result.Node, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            foreach (LadsResult old in pruned)
            {
                try
                {
                    await AddProgramNodeAsync(Node.ResultSet!, old.Node).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException(failures);
            }
        }

        /// <summary>
        /// Releases every removed result after its durable and address-space removal committed.
        /// </summary>
        private async ValueTask ReleaseResultsAsync(LadsResult[] results)
        {
            var failures = new List<Exception>();
            foreach (LadsResult result in results)
            {
                try
                {
                    await result.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
                Runtime.Logger.ResultRemoved(m_unit.Name, result.Name);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException(failures);
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
        /// Assigns the active program and its waiter without dispatching observable notifications.
        /// </summary>
        internal void AssignActiveProgram(ActiveRunEntry entry)
        {
            LadsProgramRun run = entry.Run;
            ActiveProgramState active = Node.ActiveProgram!;
            NodeId templateNode = default;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(LadsProgramManager));
                }
                if (m_templates.TryGetValue(run.Template.Id, out TemplateEntry? template))
                {
                    templateNode = template.Node.NodeId;
                }
                m_waiters[run.RunId] = new TaskCompletionSource<ILadsResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_active = entry;
            }
            LadsRuntime.AssignValue(active.DeviceProgramRunId, Variant.From(run.RunId));
            LadsRuntime.AssignValue(
                active.CurrentProgramTemplate, Variant.FromStructure(new Opc.Ua.AMB.NameNodeIdDataType
                {
                    Name = new LocalizedText(run.Template.Id),
                    NodeId = templateNode
                }));
            LadsRuntime.AssignValue(
                active.EstimatedRuntime, Variant.From(run.Template.EstimatedRuntime.TotalMilliseconds));
            LadsRuntime.AssignValue(active.EstimatedStepNumbers, Variant.From((uint)run.Template.Steps.Count));
            LadsRuntime.AssignValue(active.CurrentStepNumber, Variant.From(0u));
            LadsRuntime.AssignValue(active.CurrentStepName, Variant.From(LocalizedText.Null));
            LadsRuntime.AssignValue(active.CurrentRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.CurrentStepRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.EstimatedStepRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.CurrentPauseTime, Variant.From(0.0));
        }

        /// <summary>
        /// Clears the active program.
        /// </summary>
        private void ResetActiveProgram()
        {
            m_unit.Device.ResetActiveProgram(this);
            Node.ActiveProgram!.ClearChangeMasks(Runtime.Context, true);
        }

        private async ValueTask ResetActiveProgramAsync()
        {
            m_unit.Device.ResetActiveProgram(this);
            await Node.ActiveProgram!.ClearChangeMasksAsync(Runtime.Context, true, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Clears active-program attributes without dispatching observable notifications.
        /// </summary>
        internal void AssignActiveProgramReset()
        {
            ActiveProgramState active = Node.ActiveProgram!;
            LadsRuntime.AssignValue(active.DeviceProgramRunId, Variant.From(string.Empty));
            LadsRuntime.AssignValue(active.CurrentProgramTemplate,
                Variant.FromStructure(new Opc.Ua.AMB.NameNodeIdDataType()));
            LadsRuntime.AssignValue(active.EstimatedRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.EstimatedStepNumbers, Variant.From(0u));
            LadsRuntime.AssignValue(active.CurrentStepNumber, Variant.From(0u));
            LadsRuntime.AssignValue(active.CurrentStepName, Variant.From(LocalizedText.Null));
            LadsRuntime.AssignValue(active.CurrentRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.CurrentStepRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.EstimatedStepRuntime, Variant.From(0.0));
            LadsRuntime.AssignValue(active.CurrentPauseTime, Variant.From(0.0));
        }

        /// <summary>
        /// Publishes a step reported by the executor.
        /// </summary>
        private void OnStep(LadsProgramRun? run, uint number, string name)
        {
            double runtime = run?.CurrentRuntime.TotalMilliseconds ?? 0;
            m_unit.Device.SetProgramStep(this, run, number, name, runtime);
            Node.ActiveProgram!.ClearChangeMasks(Runtime.Context, true);
        }

        private async ValueTask OnStepAsync(
            LadsProgramRun? run,
            uint number,
            string name,
            CancellationToken cancellationToken)
        {
            double runtime = run?.CurrentRuntime.TotalMilliseconds ?? 0;
            m_unit.Device.SetProgramStep(this, run, number, name, runtime);
            await Node.ActiveProgram!.ClearChangeMasksAsync(Runtime.Context, true, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns step attributes without dispatching observable notifications.
        /// </summary>
        internal void AssignStep(
            LadsProgramRun? run, uint number, string name, double runtimeMilliseconds)
        {
            ActiveProgramState active = Node.ActiveProgram!;
            LadsRuntime.AssignValue(active.CurrentStepNumber, Variant.From(number));
            LadsRuntime.AssignValue(active.CurrentStepName, Variant.From(new LocalizedText(name)));
            LadsRuntime.AssignValue(active.CurrentStepRuntime, Variant.From(0.0));
            if (run != null)
            {
                int index = (int)number - 1;
                ArrayOf<LadsProgramStep> steps = run.Template.Steps;
                if (index >= 0 && index < steps.Count)
                {
                    LadsRuntime.AssignValue(
                        active.EstimatedStepRuntime, Variant.From(steps[index].Duration.TotalMilliseconds));
                }
                LadsRuntime.AssignValue(active.CurrentRuntime, Variant.From(runtimeMilliseconds));
            }
        }

        /// <summary>
        /// Publishes the final runtime of a run.
        /// </summary>
        internal void UpdateRuntime(LadsProgramRun run)
        {
            m_unit.Device.SetProgramRuntime(
                this, run.CurrentRuntime.TotalMilliseconds, run.CurrentPauseTime.TotalMilliseconds,
                run.CurrentStepRuntime.TotalMilliseconds);
            Node.ActiveProgram!.ClearChangeMasks(Runtime.Context, true);
        }

        /// <summary>
        /// Publishes the final runtime of a run and awaits its state notifications.
        /// </summary>
        internal async ValueTask UpdateRuntimeAsync(LadsProgramRun run)
        {
            m_unit.Device.SetProgramRuntime(
                this, run.CurrentRuntime.TotalMilliseconds, run.CurrentPauseTime.TotalMilliseconds,
                run.CurrentStepRuntime.TotalMilliseconds);
            await Node.ActiveProgram!.ClearChangeMasksAsync(Runtime.Context, true, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns measured program durations without dispatching observable notifications.
        /// </summary>
        internal void AssignRuntime(double runtime, double pause, double stepRuntime)
        {
            LadsRuntime.AssignValue(Node.ActiveProgram!.CurrentRuntime, Variant.From(runtime));
            LadsRuntime.AssignValue(Node.ActiveProgram.CurrentPauseTime, Variant.From(pause));
            LadsRuntime.AssignValue(Node.ActiveProgram.CurrentStepRuntime, Variant.From(stepRuntime));
        }

        /// <summary>
        /// Computes processing time on read without a polling task.
        /// </summary>
        private ServiceResult ReadCurrentRuntime(ISystemContext context, NodeState node, ref Variant value)
        {
            if (ActiveRun is LadsProgramRun run)
            {
                value = Variant.From(run.CurrentRuntime.TotalMilliseconds);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Computes step processing time on read.
        /// </summary>
        private ServiceResult ReadCurrentStepRuntime(ISystemContext context, NodeState node, ref Variant value)
        {
            if (ActiveRun is LadsProgramRun run)
            {
                value = Variant.From(run.CurrentStepRuntime.TotalMilliseconds);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Computes accumulated pause time on read.
        /// </summary>
        private ServiceResult ReadCurrentPauseTime(ISystemContext context, NodeState node, ref Variant value)
        {
            if (ActiveRun is LadsProgramRun run)
            {
                value = Variant.From(run.CurrentPauseTime.TotalMilliseconds);
            }
            return ServiceResult.Good;
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
        internal sealed class ActiveRunEntry
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
            /// Gets the lifetime task, available before the active entry is published.
            /// </summary>
            public Task Completion => m_completion.Task;

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
            /// Resolves completion without losing an end request when its cancellation completes
            /// the acknowledgment with false before the acknowledgment wait observes cancellation.
            /// </summary>
            public LadsProgramOutcome GetCompletionOutcome(bool acknowledged)
            {
                lock (m_lock)
                {
                    return m_requested ??
                        (acknowledged ? LadsProgramOutcome.Completed : LadsProgramOutcome.Aborted);
                }
            }

            /// <summary>
            /// Freezes the result outcome before changing state or publishing stored data.
            /// Later end requests must wait for publication and use the standalone unit transition.
            /// Execution failures take precedence over accepted end requests.
            /// </summary>
            public LadsProgramOutcome BeginFinalization(LadsProgramOutcome outcome, bool executionFailed = false)
            {
                lock (m_lock)
                {
                    m_finalizing = true;
                    return executionFailed || m_executionFailed
                        ? LadsProgramOutcome.Aborted : m_requested ?? outcome;
                }
            }

            /// <summary>
            /// Retains a provider failure before cancellation can interrupt its acknowledgment publication.
            /// </summary>
            public void RecordExecutionFailure()
            {
                lock (m_lock)
                {
                    m_executionFailed = true;
                }
            }

            /// <summary>
            /// Completes the lifetime after execution or startup cleanup has finished.
            /// </summary>
            public void Complete(Exception? failure = null)
            {
                if (failure == null)
                {
                    m_completion.TrySetResult(true);
                }
                else
                {
                    m_completion.TrySetException(failure);
                }
            }

            /// <summary>
            /// Requests the end of the run. The first accepted request determines the outcome.
            /// Returns false once result finalization has started, including for stale active entries.
            /// </summary>
            public bool RequestEnd(LadsProgramOutcome outcome)
            {
                lock (m_lock)
                {
                    if (m_finalizing)
                    {
                        return false;
                    }
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
                return true;
            }

            private readonly Lock m_lock = new();
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private LadsProgramOutcome? m_requested;
            private bool m_finalizing;
            private bool m_executionFailed;
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
        private readonly HashSet<string> m_removedTemplates = new(StringComparer.Ordinal);
        private string? m_storedInterruptedRunId;
        private string? m_lastInterruptedRunId;
        private string? m_lastFailedRunId;
    }
}
