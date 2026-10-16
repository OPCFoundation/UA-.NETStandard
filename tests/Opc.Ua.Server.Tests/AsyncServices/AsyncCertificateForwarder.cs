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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.TestModels.AsyncExecution
{
    /// <summary>
    /// Demonstrates recoverable forwarding on a stable downstream management Session using generated Push proxies.
    /// </summary>
    internal sealed class AsyncCertificateForwarder(ManagedSession device, ITelemetryContext telemetry)
    {
        /// <summary>
        /// Gets downstream correlation IDs without exposing Server tokens.
        /// </summary>
        public ArrayOf<Uuid> OperationIds => m_operationIds.ToArray();

        /// <summary>
        /// Forwards actual CSR generation, including persistent pending-key ownership on the device.
        /// </summary>
        public ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId group, NodeId type, string subject, bool regenerate, ByteString nonce, CancellationToken ct)
        {
            return InvokeAsync((client, token) => Proxy(client).CreateSigningRequestAsync(
                group, type, subject, regenerate, nonce, token), ct);
        }

        /// <summary>
        /// Forwards the real certificate update using the device's pending-key and transaction infrastructure.
        /// </summary>
        public ValueTask<bool> UpdateCertificateAsync(
            NodeId group, NodeId type, ByteString certificate, ArrayOf<ByteString> issuers,
            string keyFormat, ByteString key, CancellationToken ct)
        {
            return InvokeAsync((client, token) => Proxy(client).UpdateCertificateAsync(
                group, type, certificate, issuers, keyFormat, key, token), ct);
        }

        /// <summary>
        /// Commits the actual push transaction once; ApplyChanges is never used as result polling.
        /// </summary>
        public async ValueTask ApplyChangesAsync(CancellationToken ct)
        {
            await using AsyncServiceOperation<StatusCode> operation = await m_client.CreateMethodAsync(
                device, (client, token) => Proxy(client).ApplyChangesAsync(token), ct).ConfigureAwait(false);
            m_operationIds.Enqueue(operation.ClientOperationId);
            AsyncServiceResult<StatusCode> outcome = await operation.ExecuteAsync(
                60_000, 60_000, ct: ct).ConfigureAwait(false);
            _ = outcome.GetValueOrThrow();
            await ConfirmAcknowledgementAsync(operation, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads installed public certificates through the same generated model on the device.
        /// </summary>
        public ValueTask<(ArrayOf<NodeId>, ArrayOf<ByteString>)> GetCertificatesAsync(
            NodeId group, CancellationToken ct)
        {
            return InvokeAsync((client, token) => Proxy(client).GetCertificatesAsync(group, token), ct);
        }

        /// <summary>
        /// Keeps recovery mechanics in the shared SDK handle instead of reproducing them in a gateway loop.
        /// </summary>
        /// <typeparam name="T">The generated method result.</typeparam>
        private async ValueTask<T> InvokeAsync<T>(
            Func<ISessionClient, CancellationToken, ValueTask<T>> method, CancellationToken ct)
        {
            await using AsyncServiceOperation<T> operation = await m_client.CreateMethodAsync(device, method, ct)
                .ConfigureAwait(false);
            m_operationIds.Enqueue(operation.ClientOperationId);
            AsyncServiceResult<T> outcome = await operation.ExecuteAsync(60_000, 60_000, ct: ct).ConfigureAwait(false);
            T value = outcome.GetValueOrThrow();
            await ConfirmAcknowledgementAsync(operation, ct).ConfigureAwait(false);
            return value;
        }

        /// <summary>
        /// Retries only ACK after a lost ACK reply; accepted method values are never resubmitted.
        /// </summary>
        /// <typeparam name="T">The accepted method result.</typeparam>
        private static async ValueTask ConfirmAcknowledgementAsync<T>(
            AsyncServiceOperation<T> operation, CancellationToken ct)
        {
            if (operation.AcknowledgementFailure != null)
            {
                ControlServiceExecutionResponse acknowledgement = await operation.AcknowledgeAsync(ct)
                    .ConfigureAwait(false);
                acknowledgement.Reuse();
            }
        }

        /// <summary>
        /// Binds standard Push Management to the captured Client seam, not to ad-hoc Call DTO construction.
        /// </summary>
        private ServerConfigurationTypeClient Proxy(ISessionClient client)
        {
            return new ServerConfigurationTypeClient(client, Ua.ObjectIds.ServerConfiguration, telemetry);
        }

        private readonly AsyncServiceClient m_client = new();
        private readonly ConcurrentQueue<Uuid> m_operationIds = new();
    }
}
