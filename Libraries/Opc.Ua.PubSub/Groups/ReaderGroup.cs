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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Default sealed <see cref="IReaderGroup"/> implementation. Owns
    /// a list of <see cref="DataSetReader"/>s and dispatches each
    /// decoded <see cref="PubSubDataSetMessage"/> to the matching
    /// readers.
    /// </summary>
    /// <remarks>
    /// Implements the ReaderGroup contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.8">
    /// Part 14 §6.2.8 ReaderGroup</see>.
    /// </remarks>
    public sealed class ReaderGroup : IReaderGroup
    {
        private readonly IReadOnlyList<DataSetReader> m_readers;
        private readonly ILogger<ReaderGroup> m_logger;

        /// <summary>
        /// Initializes a new <see cref="ReaderGroup"/>.
        /// </summary>
        /// <param name="configuration">Configured reader group.</param>
        /// <param name="readers">Concrete reader instances.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public ReaderGroup(
            ReaderGroupDataType configuration,
            IReadOnlyList<DataSetReader> readers,
            ITelemetryContext telemetry)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (readers is null)
            {
                throw new ArgumentNullException(nameof(readers));
            }
            Configuration = configuration;
            m_readers = readers;
            Name = configuration.Name ?? string.Empty;
            m_logger = telemetry.CreateLogger<ReaderGroup>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? "reader-group" : Name,
                PubSubComponentKind.ReaderGroup,
                m_logger);
            foreach (DataSetReader reader in m_readers)
            {
                State.AttachChild(reader.State);
            }
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public IReadOnlyList<IDataSetReader> DataSetReaders => m_readers;

        /// <inheritdoc/>
        public ReaderGroupDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        /// <summary>
        /// Dispatches a decoded network message to all readers in the
        /// group whose filter matches.
        /// </summary>
        /// <param name="networkMessage">Decoded network message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask DispatchAsync(
            PubSubNetworkMessage networkMessage,
            CancellationToken cancellationToken = default)
        {
            if (networkMessage is null)
            {
                throw new ArgumentNullException(nameof(networkMessage));
            }
            if (State.State == PubSubState.Disabled)
            {
                return;
            }
            foreach (PubSubDataSetMessage dataSetMessage in networkMessage.DataSetMessages)
            {
                foreach (DataSetReader reader in m_readers)
                {
                    if (!reader.Matches(networkMessage, dataSetMessage))
                    {
                        continue;
                    }
                    try
                    {
                        await reader.DispatchAsync(dataSetMessage, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex,
                            "Reader {Reader} dispatch threw.", reader.Name);
                    }
                }
            }
        }

        /// <summary>
        /// Drives the reader group to operational; enables every reader.
        /// </summary>
        public ValueTask EnableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State.TryEnable())
            {
                foreach (DataSetReader reader in m_readers)
                {
                    _ = reader.State.TryEnable();
                    _ = reader.State.TryMarkOperational();
                }
                _ = State.TryMarkOperational();
            }
            return default;
        }

        /// <summary>
        /// Disables the reader group and every child reader.
        /// </summary>
        public ValueTask DisableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = State.TryDisable();
            return default;
        }
    }
}
