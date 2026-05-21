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
    public class ChannelAsyncOperation<T> : IAsyncResult, IDisposable, IValueTaskSource<bool>, IValueTaskSource
    {
        /// <summary>
        /// Initializes the object with a callback
        /// </summary>
        public ChannelAsyncOperation(int timeout, AsyncCallback? callback, object? asyncState, ILogger logger)
        {
            m_callback = callback;
            m_asyncState = asyncState;
            m_synchronous = false;
            m_completed = false;
            m_logger = logger;
            m_asyncWaitSource.RunContinuationsAsynchronously = true;

            if (timeout is > 0 and not int.MaxValue)
            {
                m_timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
                m_timeoutCancellationRegistration =
                    m_timeoutCancellationTokenSource.Token.Register(OnTimeout);
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
                    m_timeoutCancellationRegistration.Dispose();
                    m_timeoutCancellationTokenSource?.Dispose();
                    m_timeoutCancellationTokenSource = null;

                    if (m_event != null)
                    {
                        m_event.Set();
                        m_event.Dispose();
                        m_event = null;
                    }

                    if (m_asyncWaitPending)
                    {
                        m_asyncWaitSource.SetException(
                            new TaskCanceledException("ChannelAsyncOperation was disposed while an async wait was pending."));
                        m_asyncWaitPending = false;
                    }
                }
            }
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(T response)
        {
            return InternalComplete(true, response);
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(bool doNotBlock, T response)
        {
            return InternalComplete(doNotBlock, response);
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
            bool mustWait = false;

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
        public Task<T> EndAsync(
            int timeout,
            bool throwOnError = true,
            CancellationToken ct = default)
        {
            return EndValueTaskAsync(timeout, throwOnError, ct).AsTask();
        }

        /// <summary>
        /// The low-allocation awaitable response returned from the server.
        /// </summary>
        internal async ValueTask<T> EndValueTaskAsync(
            int timeout,
            bool throwOnError = true,
            CancellationToken ct = default)
        {
            // check if the request has already completed.
            bool mustWait = false;
            ValueTask<bool> waitTask = default;

            lock (m_lock)
            {
                mustWait = !m_completed;

                if (mustWait)
                {
                    m_asyncWaitPending = true;
                    waitTask = new ValueTask<bool>(this, m_asyncWaitSource.Version);
                }
            }

            // wait for completion.
            if (mustWait)
            {
                bool badRequestInterrupted = false;
                try
                {
                    if (timeout != int.MaxValue || ct.CanBeCanceled)
                    {
                        await WaitAsync(waitTask, timeout, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        _ = await waitTask.ConfigureAwait(false);
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
                catch (OperationCanceledException)
                {
                    badRequestInterrupted = true;
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

                m_timeoutCancellationRegistration.Dispose();
                m_timeoutCancellationTokenSource?.Dispose();
                m_timeoutCancellationTokenSource = null;

                m_event?.Set();

                if (m_asyncWaitPending)
                {
                    m_asyncWaitSource.SetResult(true);
                    m_asyncWaitPending = false;
                }
            }

            AsyncCallback? callback = m_callback;
            if (callback != null)
            {
                if (doNotBlock)
                {
                    _ = Task.Run(() => callback(this));
                }
                else
                {
                    try
                    {
                        callback(this);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(
                            e,
                            "ClientChannel: Unexpected error invoking AsyncCallback.");
                    }
                }
            }

            return true;
        }

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
        {
            return m_asyncWaitSource.GetStatus(token);
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
        {
            return m_asyncWaitSource.GetStatus(token);
        }

        bool IValueTaskSource<bool>.GetResult(short token)
        {
            return m_asyncWaitSource.GetResult(token);
        }

        void IValueTaskSource.GetResult(short token)
        {
            ((IValueTaskSource)m_asyncWaitSource).GetResult(token);
        }

        void IValueTaskSource<bool>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            m_asyncWaitSource.OnCompleted(continuation, state, token, flags);
        }

        void IValueTaskSource.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            m_asyncWaitSource.OnCompleted(continuation, state, token, flags);
        }

        private void OnTimeout()
        {
            if (m_timeoutCancellationTokenSource != null)
            {
                InternalComplete(false, new ServiceResult(StatusCodes.BadRequestTimeout));
            }
        }

        private static async Task WaitAsync(ValueTask<bool> waitTask, int timeout, CancellationToken ct)
        {
            Task<bool> task = waitTask.AsTask();
            using CancellationTokenSource? timeoutCancellationTokenSource =
                timeout != int.MaxValue ? new CancellationTokenSource(timeout) : null;
            using CancellationTokenSource? linkedCancellationTokenSource =
                timeoutCancellationTokenSource != null && ct.CanBeCanceled ?
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCancellationTokenSource.Token) :
                null;

            CancellationToken effectiveCancellationToken =
                linkedCancellationTokenSource?.Token ??
                timeoutCancellationTokenSource?.Token ??
                ct;

            if (!effectiveCancellationToken.CanBeCanceled)
            {
                _ = await task.ConfigureAwait(false);
                return;
            }

#if NET6_0_OR_GREATER
            _ = await task.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
#else
            Task completedTask = await Task.WhenAny(
                task,
                Task.Delay(Timeout.Infinite, effectiveCancellationToken)).ConfigureAwait(false);

            if (!ReferenceEquals(completedTask, task))
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
            }

            _ = await task.ConfigureAwait(false);
#endif
        }

        private readonly Lock m_lock = new();
        private readonly AsyncCallback? m_callback;
        private readonly object? m_asyncState;
        private readonly bool m_synchronous;
        private readonly ILogger m_logger;
        private readonly ManualResetValueTaskSource<bool> m_asyncWaitSource = new();
        private bool m_completed;
        private bool m_asyncWaitPending;
        private ManualResetEvent? m_event;
        private T? m_response;
        private ServiceResult? m_error;
        private CancellationTokenSource? m_timeoutCancellationTokenSource;
        private CancellationTokenRegistration m_timeoutCancellationRegistration;
        private Dictionary<string, object>? m_properties;
    }
}
