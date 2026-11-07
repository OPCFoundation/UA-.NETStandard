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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Scales.Server.Builders;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// What the runtime of a scale needs from the node manager that hosts it.
    /// </summary>
    internal sealed class ScaleRuntimeServices : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Creates the shared controller dependencies.
        /// </summary>
        public ScaleRuntimeServices(
            ISystemContext context,
            ScaleNamespaceIndices namespaces,
            ScalesServerOptions options,
            Action<NodeState> register,
            Func<NodeId, CancellationToken, ValueTask<bool>> delete,
            Func<NodeId, NodeId, bool> isTypeOf,
            ILockService? lockService,
            ILogger logger,
            CancellationToken stoppingToken = default)
        {
            Context = context;
            Namespaces = namespaces;
            Options = options;
            Register = node =>
            {
                BindProductWriteGuards(node, null);
                register(node);
            };
            Delete = delete;
            IsTypeOf = isTypeOf;
            LockService = lockService;
            Logger = logger;
            StoppingToken = stoppingToken;
        }

        /// <summary>
        /// Gets the owning manager's system context.
        /// </summary>
        public ISystemContext Context { get; }

        /// <summary>
        /// Gets the resolved companion namespaces.
        /// </summary>
        public ScaleNamespaceIndices Namespaces { get; }

        /// <summary>
        /// Gets the selected runtime options.
        /// </summary>
        public ScalesServerOptions Options { get; }

        /// <summary>
        /// Registers a subtree created after the scale was registered.
        /// </summary>
        public Action<NodeState> Register { get; }

        /// <summary>
        /// Deletes a registered subtree.
        /// </summary>
        public Func<NodeId, CancellationToken, ValueTask<bool>> Delete { get; }

        /// <summary>
        /// Checks a type is the second type or a subtype of it.
        /// </summary>
        public Func<NodeId, NodeId, bool> IsTypeOf { get; }

        /// <summary>
        /// The DI locking service, when products are lockable.
        /// </summary>
        public ILockService? LockService { get; }

        /// <summary>
        /// Gets the runtime logger.
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Gets the runtime shutdown signal for asynchronous equipment providers.
        /// </summary>
        internal CancellationToken StoppingToken { get; }

        /// <summary>
        /// Creates file services owned by the address-space runtime, not by an individual recipe controller.
        /// </summary>
        public ScaleFileBinder CreateFileBinder()
        {
            lock (m_resourceGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ScaleRuntimeServices));
                }
                var files = new ScaleFileBinder();
                m_resources.Add(files);
                return files;
            }
        }

        /// <summary>
        /// Adopts controller resources before publication so failed constructions also release timers at shutdown.
        /// Resources must support repeated concurrent Dispose calls to stop admission independently of draining.
        /// </summary>
        internal void OwnResource(IDisposable resource)
        {
            lock (m_resourceGate)
            {
                if (m_disposed)
                {
                    resource.Dispose();
                    throw new ObjectDisposedException(nameof(ScaleRuntimeServices));
                }
                m_resources.Add(resource);
            }
        }

        /// <summary>
        /// Releases the resources of every scale, including nested controllers. A controller
        /// stops delivering notifications, but a callback already running is not waited for.
        /// </summary>
        public void Dispose()
        {
            _ = Stop(synchronous: true);
        }

        /// <summary>
        /// Releases the resources of every scale and waits until timer and subscriber
        /// callbacks already running in asynchronously disposable controllers have returned.
        /// A failing resource does not stop the others from being released; its exception is
        /// rethrown afterwards. Repeated callers share the drain and its result, including after Dispose.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(Stop(synchronous: false));
        }

        /// <summary>
        /// Detaches resources once and shares their asynchronous drain, including after synchronous shutdown.
        /// </summary>
        private Task<bool> Stop(bool synchronous)
        {
            IDisposable[] resources;
            TaskCompletionSource<bool> completion;
            bool first;
            lock (m_resourceGate)
            {
                first = m_disposal == null;
                if (!first && (!synchronous || m_disposal!.Task.IsCompleted))
                {
                    return m_disposal!.Task;
                }
                if (first)
                {
                    m_disposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposed = true;
                    m_stoppingResources = [.. m_resources];
                    m_resources.Clear();
                }
                completion = m_disposal!;
                resources = m_stoppingResources;
            }
            List<Exception>? failures = null;
            // Every synchronous caller stops every resource itself. Another caller may be
            // paused before reaching a controller, so sharing only the drain task is insufficient.
            for (int index = resources.Length - 1; index >= 0; index--)
            {
                try
                {
                    resources[index].Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
            if (first)
            {
                _ = CompleteDisposalAsync(resources, failures, completion);
            }
            if (synchronous)
            {
                ThrowFailures(failures);
            }
            return completion.Task;
        }

        /// <summary>
        /// Attempts every asynchronous drain and retains the result for all shutdown callers.
        /// </summary>
        private async Task CompleteDisposalAsync(
            IDisposable[] resources,
            List<Exception>? failures,
            TaskCompletionSource<bool> completion)
        {
            // Keep the synchronous failure list stable while Stop returns or rethrows it.
            List<Exception>? allFailures = failures == null ? null : [.. failures];
            for (int index = resources.Length - 1; index >= 0; index--)
            {
                try
                {
                    if (resources[index] is IAsyncDisposable asyncResource)
                    {
                        await asyncResource.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    (allFailures ??= []).Add(ex);
                }
            }
            lock (m_resourceGate)
            {
                m_stoppingResources = [];
            }
            try
            {
                ThrowFailures(allFailures);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Exception completionFailure = ex;
                try
                {
                    Logger.ResourceShutdownFailed(ex);
                }
                catch (Exception loggingFailure)
                {
                    completionFailure = new AggregateException(ex, loggingFailure);
                }
                completion.TrySetException(completionFailure);
                _ = completion.Task.Exception;
            }
        }

        /// <summary>
        /// Preserves a single cleanup failure or reports every failure together.
        /// </summary>
        private static void ThrowFailures(List<Exception>? failures)
        {
            if (failures?.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures != null)
            {
                throw new AggregateException(failures);
            }
        }

        /// <summary>
        /// Builds a NodeId in the Scales namespace.
        /// </summary>
        public NodeId ScalesId(uint id)
        {
            return new NodeId(id, Namespaces.Scales);
        }

        /// <summary>
        /// Builds a browse name for an application instance.
        /// </summary>
        public QualifiedName InstanceName(string name)
        {
            return new QualifiedName(name, Namespaces.Scales);
        }

        /// <summary>
        /// Protects product values before publishing an initial or dynamically created subtree.
        /// </summary>
        internal void BindProductWriteGuards(NodeState node, ScaleWriteGate? writeGate)
        {
            lock (m_resourceGate)
            {
                ScaleProductWriteGuard.Bind(this, node, writeGate);
            }
        }

        /// <summary>Protects resource adoption and disposal.</summary>
        private readonly Lock m_resourceGate = new();

        /// <summary>The resources of all controller instances.</summary>
        private readonly List<IDisposable> m_resources = [];

        /// <summary>The shared completion of resource shutdown and callback draining.</summary>
        private TaskCompletionSource<bool>? m_disposal;

        /// <summary>Idempotently disposable resources retained until every synchronous caller can stop them.</summary>
        private IDisposable[] m_stoppingResources = [];

        /// <summary>Whether resources have been released.</summary>
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated diagnostics for shared runtime resource shutdown.
    /// </summary>
    internal static partial class ScaleRuntimeServicesLog
    {
        /// <summary>
        /// Reports a cleanup failure even when only synchronous disposal is requested.
        /// </summary>
        [LoggerMessage(
            EventId = ScalesServerEventIds.ScaleRuntimeServices,
            Level = LogLevel.Error,
            Message = "Scales runtime resource shutdown failed.")]
        public static partial void ResourceShutdownFailed(this ILogger logger, Exception exception);
    }

    /// <summary>
    /// Value helpers shared by the scale runtime.
    /// </summary>
    internal static class ScaleValues
    {
        /// <summary>
        /// Stamps a written value and reports the change to monitored items.
        /// </summary>
        public static void Touch(ISystemContext context, BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            variable.ClearChangeMasks(context, includeChildren: false);
        }

        /// <summary>
        /// Stamps a written value and awaits notification of the live variable.
        /// </summary>
        /// <param name="context">The context for the node.</param>
        /// <param name="variable">The variable to notify.</param>
        public static async ValueTask TouchAsync(ISystemContext context, BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            await variable.ClearChangeMasksAsync(context, includeChildren: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a value and reports it.
        /// </summary>
        public static void Set(ISystemContext context, BaseVariableState? variable, Variant value)
        {
            if (variable == null)
            {
                return;
            }
            variable.WrappedValue = value;
            Touch(context, variable);
        }

        /// <summary>
        /// Writes a value and awaits notification of the live variable.
        /// </summary>
        /// <param name="context">The context for the node.</param>
        /// <param name="variable">The variable to update.</param>
        /// <param name="value">The value to publish.</param>
        public static async ValueTask SetAsync(ISystemContext context, BaseVariableState? variable, Variant value)
        {
            if (variable == null)
            {
                return;
            }
            variable.WrappedValue = value;
            await TouchAsync(context, variable).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a typed property and awaits notification of the live property.
        /// </summary>
        /// <typeparam name="T">The property's value type.</typeparam>
        /// <param name="context">The context for the node.</param>
        /// <param name="property">The property to update.</param>
        /// <param name="value">The value to publish.</param>
        public static async ValueTask SetAsync<T>(ISystemContext context, PropertyState<T>? property, T value)
        {
            if (property == null)
            {
                return;
            }
            property.Value = value;
            await TouchAsync(context, property).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a typed property value and reports it.
        /// </summary>
        /// <typeparam name="T">The property's value type.</typeparam>
        public static void Set<T>(ISystemContext context, PropertyState<T>? property, T value)
        {
            if (property == null)
            {
                return;
            }
            property.Value = value;
            Touch(context, property);
        }

        /// <summary>
        /// Writes the <c>EngineeringUnits</c> property of an analog variable,
        /// when it has one.
        /// </summary>
        public static void SetUnits(ISystemContext context, BaseVariableState? variable, EUInformation? units)
        {
            if (variable == null || units == null)
            {
                return;
            }
            if (variable.FindChild(context, new QualifiedName(Ua.BrowseNames.EngineeringUnits))
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(units));
                Touch(context, property);
            }
        }

        /// <summary>
        /// Writes the <c>EngineeringUnits</c> property and awaits its notification.
        /// </summary>
        /// <param name="context">The context for the node.</param>
        /// <param name="variable">The analog variable.</param>
        /// <param name="units">The engineering units.</param>
        public static async ValueTask SetUnitsAsync(
            ISystemContext context,
            BaseVariableState? variable,
            EUInformation? units)
        {
            if (variable == null || units == null)
            {
                return;
            }
            if (variable.FindChild(context, new QualifiedName(Ua.BrowseNames.EngineeringUnits))
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(units));
                await TouchAsync(context, property).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Writes the <c>EURange</c> property of an analog variable, when it
        /// has one.
        /// </summary>
        public static void SetRange(ISystemContext context, BaseVariableState? variable, Range? range)
        {
            if (variable == null || range == null)
            {
                return;
            }
            if (variable.FindChild(context, new QualifiedName(Ua.BrowseNames.EURange))
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(range));
                Touch(context, property);
            }
        }

        /// <summary>
        /// Checks an engineering unit passed to a method: it has to be one of
        /// the allowed units when the node publishes a list, and an SI unit
        /// when the server requires them (OPC 40200 §7.4.3, §12.2.2 Table 170).
        /// </summary>
        public static ServiceResult CheckUnit(
            EUInformation? unit,
            ArrayOf<EUInformation> allowed,
            bool requireSi)
        {
            if (unit == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "An engineering unit is required.");
            }
            if (requireSi && !ScaleUnits.IsSiMass(unit))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' is not an SI unit and this server only accepts SI units.",
                    unit.DisplayName.Text ?? string.Empty);
            }
            if (allowed.Count > 0)
            {
                foreach (EUInformation candidate in allowed)
                {
                    if (ScaleUnits.SameUnit(candidate, unit))
                    {
                        return ServiceResult.Good;
                    }
                }
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' is not one of the allowed engineering units.",
                    unit.DisplayName.Text ?? string.Empty);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the <c>EngineeringUnits</c> property of a variable.
        /// </summary>
        public static EUInformation? UnitsOf(ISystemContext context, BaseVariableState? variable)
        {
            if (variable?.FindChild(context, new QualifiedName(Ua.BrowseNames.EngineeringUnits))
                is BaseVariableState property &&
                property.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out EUInformation? units))
            {
                return units;
            }
            return null;
        }

        /// <summary>
        /// Sets the current state of a state machine the model supplies no
        /// tables for, such as the OPC 40001-1 MachineryItemState and
        /// MachineryOperationMode, by pointing <c>CurrentState</c> at the
        /// state object.
        /// </summary>
        public static void SetCurrentState(
            ISystemContext context,
            FiniteStateMachineState? machine,
            NodeId stateId,
            string stateName)
        {
            if (machine?.CurrentState == null)
            {
                return;
            }
            machine.CurrentState.Value = new LocalizedText(stateName);
            machine.CurrentState.Id?.Value = stateId;
            machine.CurrentState.StatusCode = StatusCodes.Good;
            machine.CurrentState.Timestamp = DateTimeUtc.Now;
            machine.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Rejects a call on behalf of a client that does not hold the lock of
        /// a lockable element another client holds (DI LockingServices, used
        /// by OPC 40200 §7.8.3 as the product access restriction).
        /// The runtime's own context is trusted; distinct request contexts must satisfy session ownership.
        /// </summary>
        public static ServiceResult CheckLock(
            ScaleRuntimeServices services,
            ISystemContext callContext,
            NodeId elementId)
        {
            if (ReferenceEquals(callContext, services.Context) || services.LockService is not { } lockService)
            {
                return ServiceResult.Good;
            }
            LockState state = lockService.GetState(elementId);
            if (!state.Locked)
            {
                return ServiceResult.Good;
            }
            // RenewLock only succeeds for the owning session, so it doubles as
            // the ownership test; renewing on the owner's own activity is what
            // a client holding the lock would do anyway.
            int status = lockService.RenewLock(callContext, elementId);
            return status == LockStatus.Ok
                ? ServiceResult.Good
                : ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "The element is locked by '{0}'.",
                    state.LockingClient);
        }
    }
}
