/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores the results of an asynchronous operation.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ChannelAsyncOperation<T> : IAsyncResult, IDisposable
    {
        /// <summary>
        /// Initializes the object with a callback
        /// </summary>
        public ChannelAsyncOperation(int timeout, AsyncCallback? callback, object? asyncState, ILogger logger)
            : this(timeout, callback, asyncState, logger, null)
        {
        }

        /// <summary>
        /// Initializes the object with a callback using the supplied
        /// <see cref="TimeProvider"/> for timeout scheduling.
        /// </summary>
        public ChannelAsyncOperation(
            int timeout,
            AsyncCallback? callback,
            object? asyncState,
            ILogger logger,
            TimeProvider? timeProvider)
        {
            m_callback = callback;
            m_asyncState = asyncState;
            m_synchronous = false;
            m_completed = false;
            m_logger = logger;
            m_timeProvider = timeProvider ?? TimeProvider.System;

            if (timeout is > 0 and not int.MaxValue)
            {
                m_timer = m_timeProvider.CreateTimer(
                    s_onTimeout,
                    this,
                    TimeSpan.FromMilliseconds(timeout),
                    Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_lock)
                {
                    m_timer?.Dispose();
                    m_timer = null;

                    if (m_event != null)
                    {
                        m_event.Set();
                        m_event.Dispose();
                        m_event = null;
                    }

                    if (m_tcs != null)
                    {
                        if (!m_tcs.Task.IsCompleted)
                        {
                            m_tcs.TrySetCanceled();
                        }
                        m_tcs = null;
                    }

                    // like the cancelled task above: the wait is interrupted.
                    m_waiter?.TrySetResult(false);
                }
            }
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(T response)
        {
            return InternalComplete(true, Box(response));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(bool doNotBlock, T response)
        {
            return InternalComplete(doNotBlock, Box(response));
        }

        /// <summary>
        /// Boxes a response for <see cref="InternalComplete"/>. The default value of a
        /// value type, the usual response of a write, shares one immutable box.
        /// </summary>
        private static object? Box(T response)
        {
            if (typeof(T).IsValueType &&
                EqualityComparer<T>.Default.Equals(response, default!))
            {
                return s_boxedDefault;
            }
            return response;
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(ServiceResult error)
        {
            return InternalComplete(true, error);
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(bool doNotBlock, ServiceResult error)
        {
            return InternalComplete(doNotBlock, error);
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(StatusCode code, string format, params object[] args)
        {
            return InternalComplete(true, ServiceResult.Create(code, format, args));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(bool doNotBlock, StatusCode code, string format, params object[] args)
        {
            return InternalComplete(doNotBlock, ServiceResult.Create(code, format, args));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(Exception e, StatusCode defaultCode, string format, params object[] args)
        {
            return InternalComplete(true, ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(
            bool doNotBlock,
            Exception e,
            StatusCode defaultCode,
            string format,
            params object[] args)
        {
            return InternalComplete(doNotBlock, ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// The response returned from the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public T End(int timeout, bool throwOnError = true)
        {
            // check if the request has already completed.
            bool mustWait;
            lock (m_lock)
            {
                mustWait = !m_completed;

                if (mustWait)
                {
                    m_event = new ManualResetEvent(false);
                }
            }

            // wait for completion.
            if (mustWait)
            {
                try
                {
                    if (!m_event!.WaitOne(timeout) && throwOnError)
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestInterrupted);
                    }
                }
                finally
                {
                    lock (m_lock)
                    {
                        // Dispose the event
                        m_event?.Dispose();
                        m_event = null;
                    }
                }
            }

            // return the response.
            lock (m_lock)
            {
                if (m_error != null && throwOnError)
                {
                    throw new ServiceResultException(m_error);
                }

                return m_response!;
            }
        }

        /// <summary>
        /// The awaitable response returned from the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<T> EndAsync(
            int timeout,
            bool throwOnError = true,
            CancellationToken ct = default)
        {
            // check if the request has already completed.
            bool mustWait;
            lock (m_lock)
            {
                mustWait = !m_completed;

                if (mustWait)
                {
                    m_tcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            // wait for completion.
            if (mustWait)
            {
                bool badRequestInterrupted = false;
                CancellationTokenRegistration cancellation = default;
                try
                {
                    Task<bool> awaitableTask = m_tcs!.Task;
                    if (timeout != int.MaxValue)
                    {
                        awaitableTask = m_tcs.Task
                            .WaitAsync(TimeSpan.FromMilliseconds(timeout), m_timeProvider, ct);
                    }
                    else if (ct.CanBeCanceled)
                    {
                        // Cancellation ends the wait like WaitAsync(ct) would, without
                        // allocating a second task: the source completes with false,
                        // which reports BadRequestInterrupted below. A response that
                        // arrives later finds no source to complete.
                        cancellation = ct.Register(
                            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(false),
                            m_tcs);
                    }
                    if (!await awaitableTask.ConfigureAwait(false))
                    {
                        badRequestInterrupted = true;
                    }
                }
                catch (TimeoutException)
                {
                    badRequestInterrupted = true;
                }
                catch (TaskCanceledException)
                {
                    badRequestInterrupted = true;
                }
                finally
                {
                    cancellation.Dispose();
                    lock (m_lock)
                    {
                        m_tcs = null;
                    }
                }

                if (badRequestInterrupted && throwOnError)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestInterrupted);
                }
            }

            // return the response.
            lock (m_lock)
            {
                if (m_error != null && throwOnError)
                {
                    throw new ServiceResultException(m_error);
                }

                return m_response!;
            }
        }

        /// <summary>
        /// Waits for the operation like <see cref="EndAsync"/> without a timeout and
        /// throwing on error, but without allocating tasks for the wait.
        /// </summary>
        /// <remarks>
        /// Only one wait at a time is supported. The continuation runs on the thread
        /// pool, never on the thread that completes the operation.
        /// </remarks>
        /// <param name="ct">Ends the wait with BadRequestInterrupted.</param>
        /// <exception cref="ServiceResultException">
        /// BadRequestInterrupted when <paramref name="ct"/> is cancelled or the
        /// operation is disposed while waiting, or the error the operation failed
        /// with.
        /// </exception>
        internal ValueTask WaitForCompletionAsync(CancellationToken ct)
        {
            CompletionWaiter waiter;
            lock (m_lock)
            {
                if (m_completed)
                {
                    return m_error == null
                        ? default
                        : new ValueTask(Task.FromException(new ServiceResultException(m_error)));
                }
                waiter = new CompletionWaiter(this);
                m_waiter = waiter;
            }

            if (ct.CanBeCanceled)
            {
                // Runs the callback inline if the token is already cancelled.
                waiter.Registration = ct.Register(
                    static state => ((CompletionWaiter)state!).TrySetResult(false),
                    waiter);
            }
            return new ValueTask(waiter, waiter.Version);
        }

        /// <summary>
        /// Stores additional state information associated with the operation.
        /// </summary>
        public IDictionary<string, object> Properties
        {
            get
            {
                lock (m_lock)
                {
                    m_properties ??= [];

                    return m_properties;
                }
            }
        }

        /// <summary>
        /// Return the result of the operation.
        /// </summary>
        public ServiceResult Error => m_error ?? ServiceResult.Good;

        /// <inheritdoc/>
        public object? AsyncState
        {
            get
            {
                lock (m_lock)
                {
                    return m_asyncState;
                }
            }
        }

        /// <inheritdoc/>
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock (m_lock)
                {
                    m_event ??= new ManualResetEvent(m_completed);

                    return m_event;
                }
            }
        }

        /// <inheritdoc/>
        public bool CompletedSynchronously
        {
            get
            {
                lock (m_lock)
                {
                    return m_synchronous;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsCompleted
        {
            get
            {
                lock (m_lock)
                {
                    return m_completed;
                }
            }
        }

        /// <summary>
        /// Called when the operation times out.
        /// </summary>
        private void OnTimeout(object? state)
        {
            if (m_timer != null)
            {
                InternalComplete(false, new ServiceResult(StatusCodes.BadRequestTimeout));
            }
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        protected virtual bool InternalComplete(bool doNotBlock, object? result)
        {
            lock (m_lock)
            {
                // ignore multiple calls (i.e. a timeout after a response or vise versa).
                if (m_completed)
                {
                    return false;
                }

                if (result is T typed)
                {
                    m_response = typed;
                }
                else
                {
                    m_error = result as ServiceResult;
                }

                m_completed = true;

                m_timer?.Dispose();
                m_timer = null;

                m_event?.Set();

                m_tcs?.TrySetResult(true);

                m_waiter?.TrySetResult(true);
            }

            AsyncCallback? callback = m_callback;
            if (callback != null)
            {
                if (doNotBlock)
                {
                    // Queued rather than run inline because the completing frame
                    // may hold a channel's gate, which is not re-entrant: a
                    // callback that entered it from this stack would deadlock.
                    // UnsafeQueueUserWorkItem also avoids capturing and
                    // restoring the execution context, which this callback does
                    // not need.
                    ThreadPool.UnsafeQueueUserWorkItem(
                        static state =>
                        {
                            var operation = (ChannelAsyncOperation<T>)state!;
                            AsyncCallback? cb = operation.m_callback;

                            if (cb == null)
                            {
                                return;
                            }

                            // Same contract as the inline branch below: the
                            // callback is user code and may throw. Without this
                            // the exception reaches the thread pool and takes the
                            // process down.
                            try
                            {
                                cb(operation);
                            }
                            catch (Exception e)
                            {
                                operation.m_logger.ChannelAsyncOperationLogMessage0(e);
                            }
                        },
                        this);
                }
                else
                {
                    try
                    {
                        callback(this);
                    }
                    catch (Exception e)
                    {
                        m_logger.ChannelAsyncOperationLogMessage0(e);
                    }
                }
            }

            return true;
        }

        private static readonly TimerCallback s_onTimeout =
            static state => ((ChannelAsyncOperation<T>)state!).OnTimeout(null);
        private static readonly object? s_boxedDefault = default(T);
        private readonly Lock m_lock = new();
        private readonly AsyncCallback? m_callback;
        private readonly object? m_asyncState;
        private readonly bool m_synchronous;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private bool m_completed;
        private ManualResetEvent? m_event;
        private TaskCompletionSource<bool>? m_tcs;
        private T? m_response;
        private ServiceResult? m_error;
        private ITimer? m_timer;
        private Dictionary<string, object>? m_properties;
        private CompletionWaiter? m_waiter;

        /// <summary>
        /// Completes the wait of <see cref="WaitForCompletionAsync"/>: with
        /// <see langword="true"/> when the operation completed, with
        /// <see langword="false"/> when the wait was interrupted.
        /// </summary>
        private sealed class CompletionWaiter : IValueTaskSource
        {
            public CompletionWaiter(ChannelAsyncOperation<T> owner)
            {
                m_owner = owner;
                m_core.RunContinuationsAsynchronously = true;
            }

            /// <summary>
            /// The cancellation registration, disposed when the result is read.
            /// </summary>
            public CancellationTokenRegistration Registration { get; set; }

            public short Version => m_core.Version;

            /// <summary>
            /// Sets the result once; later calls are ignored.
            /// </summary>
            public void TrySetResult(bool completed)
            {
                if (Interlocked.Exchange(ref m_resultSet, 1) == 0)
                {
                    m_core.SetResult(completed);
                }
            }

            public void GetResult(short token)
            {
                bool completed;
                try
                {
                    completed = m_core.GetResult(token);
                }
                finally
                {
                    Registration.Dispose();
                    lock (m_owner.m_lock)
                    {
                        if (ReferenceEquals(m_owner.m_waiter, this))
                        {
                            m_owner.m_waiter = null;
                        }
                    }
                }

                if (!completed)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestInterrupted);
                }

                lock (m_owner.m_lock)
                {
                    if (m_owner.m_error != null)
                    {
                        throw new ServiceResultException(m_owner.m_error);
                    }
                }
            }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                return m_core.GetStatus(token);
            }

            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                m_core.OnCompleted(continuation, state, token, flags);
            }

            private readonly ChannelAsyncOperation<T> m_owner;
            private ManualResetValueTaskSourceCore<bool> m_core;
            private int m_resultSet;
        }
    }

    /// <summary>
    /// Source-generated log messages for ChannelAsyncOperation.
    /// </summary>
    internal static partial class ChannelAsyncOperationLog
    {
        [LoggerMessage(EventId = CoreEventIds.ChannelAsyncOperation + 0, Level = LogLevel.Error,
            Message = "ClientChannel: Unexpected error invoking AsyncCallback.")]
        public static partial void ChannelAsyncOperationLogMessage0(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
