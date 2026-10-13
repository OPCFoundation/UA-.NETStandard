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
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using static Opc.Ua.PlasticsRubber.Server.ProductionDatasetValidation;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Binds the complete OPC 40083 chapter 20 protocol to an existing generated management instance.
    /// </summary>
    /// <remarks>
    /// The owner retains the management node and provider lifetime. This binding owns only its
    /// temporary files, request-event subtypes, asynchronous handlers and cancellation resources.
    /// The provider must be instance-specific and implement the selected optional features.
    /// </remarks>
    public sealed partial class ProductionDatasetBinding : IAsyncDisposable
    {
        /// <summary>
        /// Captures explicit dependencies without accessing the device with a server identity.
        /// </summary>
        private ProductionDatasetBinding(
            AsyncCustomNodeManager owner,
            ProductionDatasetManagementState node,
            IProductionDatasetProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            ProductionDatasetOptions options,
            IFileTypeBinder fileBinder,
            TimeProvider timeProvider)
        {
            m_owner = owner;
            Node = node;
            m_provider = provider;
            m_accessPolicy = accessPolicy;
            m_options = options.PreserveExistingSelection
                ? options with { Features = DiscoverFeatures(node) } : options;
            m_fileBinder = fileBinder;
            m_clock = timeProvider;
        }

        /// <summary>
        /// Gets the generated instance; values are updated only from committed provider snapshots.
        /// </summary>
        public ProductionDatasetManagementState Node { get; }

        /// <summary>
        /// Creates an injectable binding, materializing its selected optional nodes before registration.
        /// </summary>
        /// <remarks>
        /// No provider read is made during creation: the first service read retains its real caller.
        /// Disabled optional members must not already exist on the supplied instance.
        /// </remarks>
        public static async ValueTask<ProductionDatasetBinding> CreateAsync(
            AsyncCustomNodeManager owner,
            ProductionDatasetManagementState node,
            IProductionDatasetProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            ProductionDatasetOptions? options = null,
            IFileTypeBinder? fileBinder = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (accessPolicy == null)
            {
                throw new ArgumentNullException(nameof(accessPolicy));
            }
            options ??= new ProductionDatasetOptions();
            options.Validate();
            var binding = new ProductionDatasetBinding(
                owner, node, provider, accessPolicy, options,
                fileBinder ?? new FileTypeBinder(), timeProvider ?? TimeProvider.System);
            try
            {
                binding.SelectMembers();
                await owner.AddPredefinedNodeAsync(node, cancellationToken).ConfigureAwait(false);
                binding.BindStatus(node.ActiveProductionDatasetStatus!, StorageEnumeration.PRODUCTION);
                if (node.ProductionDatasetInPreparationStatus != null)
                {
                    binding.BindStatus(node.ProductionDatasetInPreparationStatus, StorageEnumeration.PREPARATION);
                }
                binding.BindListsAndInformation();
                binding.BindTransfer();
                await binding.CreateRequestEventTypesAsync(cancellationToken).ConfigureAwait(false);
                node.EventNotifier |= EventNotifiers.SubscribeToEvents;
                owner.AddRootNotifier(node);
                binding.m_maintenance = binding.MaintainTransfersAsync();
                return binding;
            }
            catch
            {
                await binding.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Refreshes selected status nodes with an explicitly supplied caller and provider snapshots.
        /// </summary>
        public async ValueTask RefreshAsync(ISystemContext context, CancellationToken cancellationToken = default)
        {
            ServiceResult result = await InvokeAsync(
                context, Node.NodeId, Node, Node, async (request, ct) =>
                {
                    await ReadAndPublishAsync(request, StorageEnumeration.PRODUCTION, ct).ConfigureAwait(false);
                    if (Has(ProductionDatasetFeatures.Preparation))
                    {
                        await ReadAndPublishAsync(request, StorageEnumeration.PREPARATION, ct).ConfigureAwait(false);
                    }
                }, cancellationToken, publish: true).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <summary>
        /// Reclaims inactive or session-closed transfers immediately; periodic reclamation is also automatic.
        /// </summary>
        public ValueTask ReclaimExpiredAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync(_ => ExpireCoreAsync(), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            bool start;
            lock (m_lifetimeLock)
            {
                start = !m_disposing;
                m_disposing = true;
                if (m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            return start ? new ValueTask(DisposeCoreAsync()) : new ValueTask(m_disposed.Task);
        }

        /// <summary>
        /// Selects optional generated members without replacing already-installed behavior.
        /// </summary>
        private void SelectMembers()
        {
            ISystemContext context = m_owner.SystemContext;
            if (Node.ActiveProductionDatasetStatus?.Information == null ||
                Node.ProductionDatasetTransfer?.GenerateFileForRead == null ||
                Node.ProductionDatasetTransfer.GenerateFileForWrite == null ||
                Node.ProductionDatasetTransfer.CloseAndCommit == null ||
                Node.ProductionDatasetTransfer.ClientProcessingTimeout == null)
            {
                throw new ArgumentException("Create the mandatory generated management subtree before binding.");
            }
            if (m_options.PreserveExistingSelection)
            {
                if (Node.ProductionDatasetLists is { } lists &&
                    (lists.GetProductionDatasetList == null || lists.SendProductionDatasetList == null))
                {
                    throw new ArgumentException("The supplied list object is missing mandatory methods.");
                }
                InitializeStatus(Node.ActiveProductionDatasetStatus);
                if (Node.ProductionDatasetInPreparationStatus != null)
                {
                    InitializeStatus(Node.ProductionDatasetInPreparationStatus);
                }
                return;
            }
            if (Has(ProductionDatasetFeatures.Preparation))
            {
                Node.AddProductionDatasetInPreparationStatus(context);
            }
            else if (Node.ProductionDatasetInPreparationStatus != null)
            {
                throw new ArgumentException("The supplied node advertises unselected preparation support.");
            }
            if (Has(ProductionDatasetFeatures.Lists))
            {
                Node.AddProductionDatasetLists(context);
            }
            else if (Node.ProductionDatasetLists != null)
            {
                throw new ArgumentException("The supplied node advertises unselected dataset lists.");
            }
            if (Has(ProductionDatasetFeatures.Information))
            {
                Node.AddGetProductionDatasetInformation(context);
                Node.AddSendProductionDatasetInformation(context);
            }
            else if (Node.GetProductionDatasetInformation != null || Node.SendProductionDatasetInformation != null)
            {
                throw new ArgumentException("The supplied node advertises unselected information exchange.");
            }
            SelectStatusMembers(Node.ActiveProductionDatasetStatus);
            if (Node.ProductionDatasetInPreparationStatus != null)
            {
                SelectStatusMembers(Node.ProductionDatasetInPreparationStatus);
            }
        }

        /// <summary>
        /// Selects control-layer options and leaves unread values explicitly unavailable.
        /// </summary>
        private void SelectStatusMembers(ProductionDatasetStatusState status)
        {
            ISystemContext context = m_owner.SystemContext;
            if (Has(ProductionDatasetFeatures.Frozen))
            {
                status.AddFrozen(context);
            }
            else if (status.Frozen != null)
            {
                throw new ArgumentException("The supplied node advertises an unselected Frozen property.");
            }
            if (Has(ProductionDatasetFeatures.Modified))
            {
                status.AddModified(context);
            }
            else if (status.Modified != null)
            {
                throw new ArgumentException("The supplied node advertises an unselected Modified property.");
            }
            if (Has(ProductionDatasetFeatures.LoadSave))
            {
                status.AddLoad(context);
                status.AddSave(context);
            }
            else if (status.Load != null || status.Save != null)
            {
                throw new ArgumentException("The supplied node advertises unselected Load or Save methods.");
            }
            InitializeStatus(status);
        }

        /// <summary>
        /// Marks actual selected status fields unavailable until an authenticated provider read completes.
        /// </summary>
        private static void InitializeStatus(ProductionDatasetStatusState status)
        {
            if (status.Information == null)
            {
                throw new ArgumentException("Every selected status object requires Information.", nameof(status));
            }
            status.Information.StatusCode = StatusCodes.BadWaitingForInitialData;
            status.Modified?.StatusCode = StatusCodes.BadWaitingForInitialData;
            status.Frozen?.StatusCode = StatusCodes.BadWaitingForInitialData;
        }

        /// <summary>
        /// Derives grouped guards from actual nodes without modifying a materializer's exact selection.
        /// </summary>
        private static ProductionDatasetFeatures DiscoverFeatures(ProductionDatasetManagementState node)
        {
            ProductionDatasetFeatures features = ProductionDatasetFeatures.None;
            if (node.ProductionDatasetInPreparationStatus != null)
            {
                features |= ProductionDatasetFeatures.Preparation;
            }
            if (node.ProductionDatasetLists != null)
            {
                features |= ProductionDatasetFeatures.Lists;
            }
            if (node.GetProductionDatasetInformation != null || node.SendProductionDatasetInformation != null)
            {
                features |= ProductionDatasetFeatures.Information;
            }
            ProductionDatasetStatusState?[] statuses =
                [node.ActiveProductionDatasetStatus, node.ProductionDatasetInPreparationStatus];
            foreach (ProductionDatasetStatusState? status in statuses)
            {
                if (status?.Frozen != null)
                {
                    features |= ProductionDatasetFeatures.Frozen;
                }
                if (status?.Modified != null)
                {
                    features |= ProductionDatasetFeatures.Modified;
                }
                if (status?.Load != null || status?.Save != null)
                {
                    features |= ProductionDatasetFeatures.LoadSave;
                }
            }
            return features;
        }

        /// <summary>
        /// Checks one method before taking ownership of its typed callback.
        /// </summary>
        private void Own(MethodState method, bool typedHandlerPresent)
        {
            if (typedHandlerPresent || method.OnCallMethod != null || method.OnCallMethod2 != null ||
                method.OnCallMethod2Async != null)
            {
                throw new InvalidOperationException("A production-dataset method already has a handler.");
            }
            method.Executable = true;
            method.UserExecutable = true;
            m_methods.Add(method);
        }

        /// <summary>
        /// Authorizes and executes one operation, converting failures to their real service status.
        /// </summary>
        private async ValueTask<ServiceResult> InvokeAsync(
            ISystemContext context,
            NodeId targetId,
            NodeState expectedTarget,
            NodeState member,
            Func<PlasticsRubberRequestContext, CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken,
            bool publish = false,
            bool serialize = true)
        {
            try
            {
                await RunAsync(async ct =>
                {
                    if (targetId != expectedTarget.NodeId)
                    {
                        throw Error(StatusCodes.BadNodeIdInvalid, "The method target is not the bound instance.");
                    }
                    var request = new PlasticsRubberRequestContext(context, targetId);
                    await m_accessPolicy.AuthorizeAsync(request, member.NodeId, ct).ConfigureAwait(false);
                    await operation(request, ct).ConfigureAwait(false);
                }, cancellationToken, serialize,
                    publish ? token => Node.ClearChangeMasksAsync(context, true, token) : null).ConfigureAwait(false);
                return ServiceResult.Good;
            }
            catch (ServiceResultException exception)
            {
                return exception.Result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return m_disposing ? StatusCodes.BadShutdown : StatusCodes.BadTimeout;
            }
        }

        /// <summary>
        /// Tracks queued and active operations so disposal cannot race a semaphore or token-source release.
        /// </summary>
        private async ValueTask RunAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken,
            bool serialize = true,
            Func<CancellationToken, ValueTask>? after = null)
        {
            lock (m_lifetimeLock)
            {
                if (m_disposing)
                {
                    throw Error(StatusCodes.BadShutdown, "The dataset binding is stopping.");
                }
                m_operations++;
            }
            bool entered = false;
            try
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_stopping.Token);
                lifetime.CancelAfter(m_options.OperationTimeout);
                if (serialize)
                {
                    await m_gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    entered = true;
                }
                lifetime.Token.ThrowIfCancellationRequested();
                if (m_maintenance.IsFaulted)
                {
                    throw Error(StatusCodes.BadInternalError, "Temporary-file reclamation failed.");
                }
                await operation(lifetime.Token).ConfigureAwait(false);
                if (after != null)
                {
                    if (entered)
                    {
                        m_gate.Release();
                        entered = false;
                    }
                    await after(m_stopping.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                if (entered)
                {
                    m_gate.Release();
                }
                lock (m_lifetimeLock)
                {
                    m_operations--;
                    if (m_disposing && m_operations == 0)
                    {
                        m_drained.TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Reclaims abandoned files without asynchronous-void callbacks or unobserved cleanup failures.
        /// </summary>
        private async Task MaintainTransfersAsync()
        {
            TimeSpan interval = m_options.TransferTimeout < TimeSpan.FromSeconds(1)
                ? m_options.TransferTimeout : TimeSpan.FromSeconds(1);
            try
            {
                while (true)
                {
                    await Task.Delay(interval, m_stopping.Token).ConfigureAwait(false);
                    await ReclaimExpiredAsync(m_stopping.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (m_stopping.IsCancellationRequested)
            {
                // Shutdown cancels the maintenance loop before disposal waits for outstanding operations.
            }
            catch (ServiceResultException exception) when (
                m_stopping.IsCancellationRequested && exception.StatusCode == StatusCodes.BadShutdown)
            {
                // Disposal may win the lifetime registration race immediately after the timer completes.
            }
        }

        /// <summary>
        /// Cancels providers first, drains every queued operation, then removes only owned resources.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            var errors = new List<Exception>();
            try
            {
                try
                {
                    await m_stopping.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                try
                {
                    await m_maintenance.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                await m_drained.Task.ConfigureAwait(false);
                foreach (ProductionDatasetTransferSlot slot in
                    new List<ProductionDatasetTransferSlot>(m_transfers.Values))
                {
                    try
                    {
                        await RetireAsync(slot).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }
                foreach (NodeId typeId in m_eventTypes)
                {
                    try
                    {
                        await m_owner.DeleteNodeAsync(m_owner.SystemContext, typeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }
                foreach (MethodState method in m_methods)
                {
                    method.Executable = false;
                    method.UserExecutable = false;
                }
                foreach (BaseVariableState variable in m_variables)
                {
                    variable.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                    variable.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                    variable.StatusCode = StatusCodes.BadShutdown;
                }
                await Node.ClearChangeMasksAsync(m_owner.SystemContext, true, CancellationToken.None)
                    .ConfigureAwait(false);
                if (errors.Count != 0)
                {
                    throw new AggregateException("Dataset binding cleanup failed.", errors);
                }
                m_disposed.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_disposed.TrySetException(exception);
                throw;
            }
            finally
            {
                m_gate.Dispose();
                m_stopping.Dispose();
            }
        }

        /// <summary>
        /// Tests selected capabilities.
        /// </summary>
        private bool Has(ProductionDatasetFeatures feature)
        {
            return (m_options.Features & feature) != 0;
        }

        /// <summary>
        /// Propagates a service result to a direct application caller.
        /// </summary>
        private static void ThrowIfBad(ServiceResult result)
        {
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// The owning address-space manager.
        /// </summary>
        private readonly AsyncCustomNodeManager m_owner;

        /// <summary>
        /// The device's storage and activation authority.
        /// </summary>
        private readonly IProductionDatasetProvider m_provider;

        /// <summary>
        /// The shared common-model authorization policy.
        /// </summary>
        private readonly IPlasticsRubberAccessPolicy m_accessPolicy;

        /// <summary>
        /// The immutable bounds and optional feature selection.
        /// </summary>
        private readonly ProductionDatasetOptions m_options;

        /// <summary>
        /// The reusable standard FileType implementation.
        /// </summary>
        private readonly IFileTypeBinder m_fileBinder;

        /// <summary>
        /// The clock used for dataset timestamps and inactivity deadlines.
        /// </summary>
        private readonly TimeProvider m_clock;

        /// <summary>
        /// Serializes provider operations and temporary-file lifecycle transitions.
        /// </summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>
        /// Protects only operation registration and disposal coordination.
        /// </summary>
        private readonly Lock m_lifetimeLock = new();

        /// <summary>
        /// Cancels active providers before resources are released.
        /// </summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>
        /// Signals that all queued and active operations have exited.
        /// </summary>
        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Shares one disposal outcome across concurrent callers.
        /// </summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// The automatically observed maintenance loop.
        /// </summary>
        private Task m_maintenance = Task.CompletedTask;

        /// <summary>
        /// The generated methods whose handlers this binding owns.
        /// </summary>
        private readonly List<MethodState> m_methods = [];

        /// <summary>
        /// The variables whose value callbacks this binding owns.
        /// </summary>
        private readonly List<BaseVariableState> m_variables = [];

        /// <summary>
        /// The active pre-opened handles, unique within this management instance.
        /// </summary>
        private readonly Dictionary<uint, ProductionDatasetTransferSlot> m_transfers = [];

        /// <summary>
        /// The concrete event subtypes owned by this binding.
        /// </summary>
        private readonly List<NodeId> m_eventTypes = [];

        /// <summary>
        /// The number of queued or running tracked operations.
        /// </summary>
        private int m_operations;

        /// <summary>
        /// Whether disposal has begun; protected by the lifetime lock when registering operations.
        /// </summary>
        private volatile bool m_disposing;
    }
}
