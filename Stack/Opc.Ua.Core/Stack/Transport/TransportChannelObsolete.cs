/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Obsolete channel apis
    /// </summary>
    public static class TransportChannelObsolete
    {
        /// <summary>
        /// Sends a request over the secure channel.
        /// </summary>
        [Obsolete("Use SendRequestAsync with await or GetAwaiter().GetResult() instead.")]
        public static IServiceResponse SendRequest(
            this ITransportChannel channel,
            IServiceRequest request)
        {
            return channel.SendRequestAsync(request, default)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        [Obsolete("Use SendRequestAsync instead.")]
        public static IAsyncResult BeginSendRequest(
            this ITransportChannel channel,
            IServiceRequest request,
            AsyncCallback callback,
            object callbackData)
        {
            return new AsyncResult(
                channel.SendRequestAsync(request, default).AsTask(),
                callback,
                callbackData);
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use SendRequestAsync instead.")]
        public static IServiceResponse EndSendRequest(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel,
#pragma warning restore RCS1175 // Unused 'this' parameter
            IAsyncResult result)
        {
            if (result is AsyncResult twar && twar.Task is Task<IServiceResponse> rt)
            {
                return rt.GetAwaiter().GetResult();
            }
            throw new ArgumentException(
                $"{nameof(result)} must be a AsyncResult with Task<IServiceResponse>.",
                nameof(result));
        }

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        [Obsolete("Use OpenAsync with await or GetAwaiter().GetResult() instead.")]
        public static void Initialize(
            this ITransportChannel channel,
            Uri url,
            TransportChannelSettings settings)
        {
            if (channel is ISecureChannel secure)
            {
                secure.OpenAsync(url, settings, default)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }

        /// <summary>
        /// Initializes a secure channel with a waiting connection
        /// </summary>
        [Obsolete("Use OpenAsync with await or GetAwaiter().GetResult() instead.")]
        public static void Initialize(
            this ITransportChannel channel,
            ITransportWaitingConnection connection,
            TransportChannelSettings settings)
        {
            if (channel is ISecureChannel secure)
            {
                secure.OpenAsync(connection, settings, default)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }

        /// <summary>
        /// open a secure channel with the endpoint identified by the URL.
        /// </summary>
        [Obsolete("Use OpenAsync with await or GetAwaiter().GetResult() instead.")]
        public static void Open(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel)
#pragma warning restore RCS1175 // Unused 'this' parameter
        {
        }

        /// <summary>
        /// Begins an asynchronous operation to open a secure channel with the endpoint identified by the URL.
        /// </summary>
        [Obsolete("Use OpenAsync instead.")]
        public static IAsyncResult BeginOpen(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel,
#pragma warning restore RCS1175 // Unused 'this' parameter
            AsyncCallback callback,
            object callbackData)
        {
            return new AsyncResult(
                Task.CompletedTask,
                callback,
                callbackData);
        }

        /// <summary>
        /// Completes an asynchronous operation to open a secure channel.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use OpenAsync instead.")]
        public static void EndOpen(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel,
#pragma warning restore RCS1175 // Unused 'this' parameter
            IAsyncResult result)
        {
            if (result is not AsyncResult twar)
            {
                throw new ArgumentException(
                    $"{nameof(result)} must be a AsyncResult<>.",
                    nameof(result));
            }
            twar.Task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void Reconnect(this ITransportChannel channel)
        {
            channel.ReconnectAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void Reconnect(
            this ITransportChannel channel,
            ITransportWaitingConnection connection)
        {
            channel.ReconnectAsync(connection).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static IAsyncResult BeginReconnect(
            this ITransportChannel channel,
            AsyncCallback callback,
            object callbackData)
        {
            return new AsyncResult(
                channel.ReconnectAsync().AsTask(),
                callback,
                callbackData);
        }

        /// <summary>
        /// Completes an asynchronous operation to close the existing secure
        /// channel and open a new one.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void EndReconnect(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel,
#pragma warning restore RCS1175 // Unused 'this' parameter
            IAsyncResult result)
        {
            if (result is not AsyncResult twar)
            {
                throw new ArgumentException(
                    $"{nameof(result)} must be a AsyncResult.",
                    nameof(result));
            }
            twar.Task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes the secure channel.
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static void Close(this ITransportChannel channel)
        {
            channel.CloseAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static IAsyncResult BeginClose(
            this ITransportChannel channel,
            AsyncCallback callback,
            object callbackData)
        {
            return new AsyncResult(
                channel.CloseAsync().AsTask(),
                callback,
                callbackData);
        }

        /// <summary>
        /// Completes an asynchronous operation to close the existing channel
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use CloseAsync instead.")]
        public static void EndClose(
#pragma warning disable RCS1175 // Unused 'this' parameter
            this ITransportChannel channel,
#pragma warning restore RCS1175 // Unused 'this' parameter
            IAsyncResult result)
        {
            if (result is not AsyncResult twar)
            {
                throw new ArgumentException(
                    $"{nameof(result)} must be a AsyncResult.",
                    nameof(result));
            }
            twar.Task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Wrap task in async result.
        /// </summary>
        internal sealed class AsyncResult : IAsyncResult
        {
            /// <inheritdoc/>
            public object AsyncState { get; }

            /// <inheritdoc/>
            public bool CompletedSynchronously { get; }

            /// <inheritdoc/>
            public bool IsCompleted => Task.IsCompleted;

            /// <inheritdoc/>
            public WaitHandle AsyncWaitHandle => ((IAsyncResult)Task).AsyncWaitHandle;

            internal Task Task { get; }

            /// <summary>
            /// Create async result
            /// </summary>
            public AsyncResult(Task task, AsyncCallback callback, object state)
            {
                Debug.Assert(task != null);
                Task = task;
                AsyncState = state;

                if (task.IsCompleted)
                {
                    // Synchronous completion. Done
                    CompletedSynchronously = true;
                    callback?.Invoke(this);
                }
                else if (callback != null)
                {
                    m_callback = callback;
                    Task.ConfigureAwait(continueOnCapturedContext: false)
                         .GetAwaiter()
                         // allocates a delegate, but avoids a closure
                         .OnCompleted(InvokeCallback);
                }
            }

            /// <summary>Invokes the callback.</summary>
            private void InvokeCallback()
            {
                Debug.Assert(!CompletedSynchronously);
                Debug.Assert(m_callback != null);
                m_callback.Invoke(this);
            }

            private readonly AsyncCallback m_callback;
        }
    }
}
