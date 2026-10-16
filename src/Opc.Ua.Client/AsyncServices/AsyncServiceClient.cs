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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Creates typed, same-Session operation handles before any cancellable submission can execute work.
    /// </summary>
    public sealed class AsyncServiceClient
    {
        /// <summary>
        /// Prepares one generated Read request without batching it into separate recoverable operations.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<ReadResponse>> CreateReadAsync(
            ISession session, ReadRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ReadRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.ReadRequest,
                (client, token) => client.ReadAsync(captured.RequestHeader, captured.MaxAge,
                    captured.TimestampsToReturn, captured.NodesToRead, token), ct);
        }

        /// <summary>
        /// Prepares one generated Write request, preserving its complete per-item response.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<WriteResponse>> CreateWriteAsync(
            ISession session, WriteRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            WriteRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.WriteRequest,
                (client, token) => client.WriteAsync(captured.RequestHeader, captured.NodesToWrite, token), ct);
        }

        /// <summary>
        /// Prepares one generated Call request without changing batch or per-item semantics.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<CallResponse>> CreateCallAsync(
            ISession session, CallRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            CallRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.CallRequest,
                (client, token) => client.CallAsync(captured.RequestHeader, captured.MethodsToCall, token), ct);
        }

        /// <summary>
        /// Captures one generated ObjectType proxy invocation and validates its typed output before acceptance/ACK.
        /// </summary>
        /// <remarks>
        /// Construct the proxy with the supplied client. Read-only preparation is allowed before the Call;
        /// another request after that Call is rejected. MethodId fallback and submission replay are disabled.
        /// </remarks>
        /// <typeparam name="TResult">The generated proxy's typed result.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<TResult>> CreateMethodAsync<TResult>(
            ISession session,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> method,
            CancellationToken ct = default)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            return CreateAsync(session, DataTypeIds.CallRequest, method, ct);
        }

        /// <summary>
        /// Captures a generated method with no output arguments, such as ApplyChanges, as one ordinary Call.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<StatusCode>> CreateMethodAsync(
            ISession session,
            Func<ISessionClient, CancellationToken, ValueTask> method,
            CancellationToken ct = default)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            return CreateAsync(session, DataTypeIds.CallRequest, async (client, token) =>
            {
                await method(client, token).ConfigureAwait(false);
                return StatusCodes.Good;
            }, ct);
        }

        /// <summary>
        /// Captures the shared managed lifecycle seam and transfers only local context ownership to the handle.
        /// </summary>
        /// <typeparam name="TResult">The generated response or method result.</typeparam>
        private static async ValueTask<AsyncServiceOperation<TResult>> CreateAsync<TResult>(
            ISession session, NodeId requestType,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> invoke, CancellationToken ct)
        {
            SessionRequestContext context = await SessionRequestContext.CaptureRecoverableAsync(session, ct)
                .ConfigureAwait(false);
            try
            {
                return new AsyncServiceOperation<TResult>(context, requestType, invoke);
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
