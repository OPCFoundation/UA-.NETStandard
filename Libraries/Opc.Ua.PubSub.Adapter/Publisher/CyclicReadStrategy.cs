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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// <see cref="IExternalReadStrategy"/> that obtains current values by issuing a
    /// Read service call to the external server on every publish cycle. The strategy
    /// ensures the underlying <see cref="IExternalServerSession"/> is connected and
    /// then delegates the Read; the cyclic cadence implies <c>maxAge = 0</c>
    /// (always-fresh) semantics, which the managed session applies.
    /// </summary>
    /// <remarks>
    /// The strategy is fail-soft: a service fault or transport error does not escape
    /// into the publish loop. Instead, a positionally aligned array of
    /// <see cref="DataValue"/> carrying a Bad <see cref="StatusCode"/> is returned so
    /// the writer can still produce a (bad-quality) DataSetMessage. Cancellation is
    /// always propagated to the caller.
    /// </remarks>
    public sealed class CyclicReadStrategy : IExternalReadStrategy
    {
        private readonly IExternalServerSession m_session;
        private readonly ILogger m_logger;

        /// <summary>
        /// Creates a cyclic read strategy over the supplied external-server session.
        /// </summary>
        /// <param name="session">
        /// The external-server session used to issue the Read service calls.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        public CyclicReadStrategy(
            IExternalServerSession session,
            ITelemetryContext telemetry)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<CyclicReadStrategy>();
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            if (nodesToRead.IsNull || nodesToRead.Count == 0)
            {
                return ArrayOf<DataValue>.Empty;
            }

            try
            {
                if (!m_session.IsConnected)
                {
                    await m_session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                return await m_session.ReadAsync(nodesToRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogWarning(
                    sre,
                    "Cyclic read of {Count} node(s) failed with {StatusCode}; " +
                    "returning Bad values for this publish cycle.",
                    nodesToRead.Count,
                    sre.StatusCode);
                return CreateFaultedResults(nodesToRead.Count, sre.StatusCode);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "Cyclic read of {Count} node(s) failed; returning Bad values " +
                    "for this publish cycle.",
                    nodesToRead.Count);
                return CreateFaultedResults(
                    nodesToRead.Count,
                    (StatusCode)StatusCodes.BadCommunicationError);
            }
        }

        private static ArrayOf<DataValue> CreateFaultedResults(int count, StatusCode statusCode)
        {
            var results = new DataValue[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = DataValue.FromStatusCode(statusCode);
            }
            return results;
        }
    }
}
