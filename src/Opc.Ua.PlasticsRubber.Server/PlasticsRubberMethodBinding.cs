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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Owns cancellation, authorization and asynchronous execution for one common capability.
    /// </summary>
    internal sealed class PlasticsRubberMethodBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates a binding for a registered common-model instance.
        /// </summary>
        internal PlasticsRubberMethodBinding(
            BaseObjectState node,
            ISystemContext context,
            IPlasticsRubberAccessPolicy accessPolicy,
            bool suppressCacheUpdate = false)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            m_suppressCacheUpdate = suppressCacheUpdate;
            m_disposal = new Lazy<Task>(DisposeCoreAsync);
        }

        /// <summary>
        /// Binds a parameterless standard method to an asynchronous provider operation.
        /// </summary>
        internal void Bind(
            MethodState method,
            Func<PlasticsRubberRequestContext, CancellationToken, ValueTask> action)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (method.OnCallMethod2 != null || method.OnCallMethod2Async != null)
            {
                throw new InvalidOperationException("The method already has a handler.");
            }
            method.OnCallMethod2Async = async (context, _, target, input, _, ct) =>
            {
                if (!input.IsEmpty)
                {
                    return StatusCodes.BadTooManyArguments;
                }
                return await ExecuteAsync(context, target, method.NodeId, action, ct).ConfigureAwait(false);
            };
            method.Executable = true;
            method.UserExecutable = true;
            m_methods.Add(method);
        }

        /// <summary>
        /// Executes an authorized operation without converting provider failures into success.
        /// </summary>
        internal ValueTask<ServiceResult> ExecuteAsync(
            ISystemContext context,
            NodeId targetId,
            NodeId memberId,
            Func<PlasticsRubberRequestContext, CancellationToken, ValueTask> action,
            CancellationToken cancellationToken)
        {
            if (targetId != m_node.NodeId)
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadNodeIdInvalid);
            }
            return RunAsync(async token =>
            {
                var request = new PlasticsRubberRequestContext(context, targetId);
                await m_accessPolicy.AuthorizeAsync(request, memberId, token).ConfigureAwait(false);
                await action(request, token).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// Serializes an authoritative server-side publication with service requests and disposal.
        /// </summary>
        internal async ValueTask<ServiceResult> RunAsync(
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken)
        {
            CancellationToken stopping;
            lock (m_lifecycle)
            {
                if (m_disposed != 0)
                {
                    return StatusCodes.BadInvalidState;
                }
                m_requests++;
                stopping = m_stopping.Token;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
                await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref m_disposed) != 0)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    await action(linked.Token).ConfigureAwait(false);
                    return ServiceResult.Good;
                }
                catch (ServiceResultException exception)
                {
                    return exception.Result;
                }
                finally
                {
                    m_gate.Release();
                }
            }
            finally
            {
                lock (m_lifecycle)
                {
                    m_requests--;
                    if (m_requests == 0 && m_disposed != 0)
                    {
                        m_drained.TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Binds an authorized scalar write that is acknowledged only after the provider commits it.
        /// </summary>
        internal void BindWrite(
            BaseVariableState variable,
            Func<PlasticsRubberRequestContext, Variant, CancellationToken, ValueTask> write)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (variable.OnWriteValueAsync != null ||
                variable.OnWriteValue != null ||
                variable.OnSimpleWriteValueAsync != null ||
                variable.OnSimpleWriteValue != null)
            {
                throw new InvalidOperationException("The variable already has a write handler.");
            }
            variable.OnWriteValueAsync = async (context, _, range, value, ct) =>
            {
                if (!range.IsNull)
                {
                    return new AttributeWriteResult(StatusCodes.BadIndexRangeInvalid);
                }
                ServiceResult result = await ExecuteAsync(
                    context,
                    m_node.NodeId,
                    variable.NodeId,
                    (request, token) => write(request, value, token),
                    ct).ConfigureAwait(false);
                return new AttributeWriteResult(result) { SuppressCacheUpdate = m_suppressCacheUpdate };
            };
            variable.AccessLevel |= AccessLevels.CurrentWrite;
            variable.UserAccessLevel |= AccessLevels.CurrentWrite;
            m_variables.Add(variable);
        }

        /// <summary>
        /// Includes a typed generated method in this capability's disposal lifecycle.
        /// </summary>
        internal void Track(MethodState method)
        {
            method.Executable = true;
            method.UserExecutable = true;
            m_methods.Add(method);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_disposal.Value.ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Exchange(ref m_resourcesDisposed, 1) == 0)
                {
                    m_gate.Dispose();
                    m_stopping.Dispose();
                }
            }
        }

        /// <summary>
        /// Cancels and drains every registered request before releasing its synchronization resources.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            Task drained;
            lock (m_lifecycle)
            {
                m_disposed = 1;
                drained = m_requests == 0 ? Task.CompletedTask : m_drained.Task;
            }
            try
            {
                try
                {
                    m_stopping.Cancel();
                }
                finally
                {
                    await drained.ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (MethodState method in m_methods)
                {
                    method.Executable = false;
                    method.UserExecutable = false;
                    method.ClearChangeMasks(m_context, false);
                }
                foreach (BaseVariableState variable in m_variables)
                {
                    variable.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                    variable.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                    variable.ClearChangeMasks(m_context, false);
                }
            }
        }

        /// <summary>
        /// The bound instance.
        /// </summary>
        private readonly BaseObjectState m_node;

        /// <summary>Whether an external publisher owns scalar value updates after command completion.</summary>
        private readonly bool m_suppressCacheUpdate;

        /// <summary>
        /// The owner context used when disabling methods.
        /// </summary>
        private readonly ISystemContext m_context;

        /// <summary>
        /// The explicitly supplied authorization policy.
        /// </summary>
        private readonly IPlasticsRubberAccessPolicy m_accessPolicy;

        /// <summary>
        /// The methods owned by this binding.
        /// </summary>
        private readonly List<MethodState> m_methods = [];

        /// <summary>
        /// The writable variables owned by this binding.
        /// </summary>
        private readonly List<BaseVariableState> m_variables = [];

        /// <summary>
        /// Serializes asynchronous requests to this capability.
        /// </summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>
        /// Stops in-flight provider operations before disposal waits for them.
        /// </summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>
        /// Serializes request registration against terminal disposal.
        /// </summary>
        private readonly Lock m_lifecycle = new();

        /// <summary>
        /// Completes after all active and queued calls have left their finally blocks.
        /// </summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Shares the same terminal operation between concurrent disposers.
        /// </summary>
        private readonly Lazy<Task> m_disposal;

        /// <summary>
        /// The number of registered calls, including callers waiting at the execution gate.
        /// </summary>
        private int m_requests;

        /// <summary>
        /// Whether disposal has started.
        /// </summary>
        private int m_disposed;

        /// <summary>
        /// Claims resource release after the shared drain has completed.
        /// </summary>
        private int m_resourcesDisposed;
    }
}
