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
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
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
    public sealed class ReaderGroup : IReaderGroup, IAsyncDisposable
    {
        private readonly ArrayOf<DataSetReader> m_readers;
        private readonly ArrayOf<IDataSetReader> m_dataSetReaders;
        private readonly ILogger<ReaderGroup> m_logger;
        private readonly IPubSubScheduler? m_scheduler;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly ITelemetryContext m_telemetry;
        private readonly System.Threading.Lock m_gate = new();
        private IPubSubActivationCoordinator m_activationCoordinator = AlwaysActiveCoordinator.Instance;
        private string m_componentId = string.Empty;
        private bool m_roleChangedSubscribed;
        private DataSetReaderTimeoutWatcher? m_timeoutWatcher;

        /// <summary>
        /// Initializes a new <see cref="ReaderGroup"/>.
        /// </summary>
        /// <param name="configuration">Configured reader group.</param>
        /// <param name="readers">Concrete reader instances.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public ReaderGroup(
            ReaderGroupDataType configuration,
            ArrayOf<DataSetReader> readers,
            ITelemetryContext telemetry)
            : this(configuration, readers, telemetry, scheduler: null, diagnostics: null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="ReaderGroup"/> with optional
        /// scheduler and diagnostics for the
        /// <see cref="DataSetReaderTimeoutWatcher"/>.
        /// </summary>
        /// <param name="configuration">Configured reader group.</param>
        /// <param name="readers">Concrete reader instances.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="scheduler">
        /// Scheduler used to drive the timeout watcher. When
        /// <see langword="null"/> the watcher is not started and
        /// receive-timeout enforcement is left to a higher-level loop.
        /// </param>
        /// <param name="diagnostics">
        /// Diagnostics sink for receive-timeout counter increments. When
        /// <see langword="null"/> no counters are emitted.
        /// </param>
        /// <param name="activationCoordinator">Optional high-availability activation coordinator.</param>
        /// <param name="componentId">Deterministic redundancy component id.</param>
        public ReaderGroup(
            ReaderGroupDataType configuration,
            ArrayOf<DataSetReader> readers,
            ITelemetryContext telemetry,
            IPubSubScheduler? scheduler,
            IPubSubDiagnostics? diagnostics,
            IPubSubActivationCoordinator? activationCoordinator = null,
            string? componentId = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            Configuration = configuration;
            m_readers = readers;
            m_dataSetReaders = readers.ToArrayOf<DataSetReader, IDataSetReader>(static reader => reader);
            Name = configuration.Name ?? string.Empty;
            ConfigureActivationCoordinator(
                componentId ?? $"pubsub:readergroup:{Name}",
                activationCoordinator);
            m_telemetry = telemetry;
            m_scheduler = scheduler;
            m_diagnostics = diagnostics;
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
        public ArrayOf<IDataSetReader> DataSetReaders => m_dataSetReaders;

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
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask DispatchAsync(
            PubSubNetworkMessage networkMessage,
            CancellationToken cancellationToken = default)
        {
            if (networkMessage is null)
            {
                throw new ArgumentNullException(nameof(networkMessage));
            }
            if (State.State != PubSubState.Operational)
            {
                return;
            }
            for (int messageIndex = 0; messageIndex < networkMessage.DataSetMessages.Count; messageIndex++)
            {
                PubSubDataSetMessage dataSetMessage = networkMessage.DataSetMessages[messageIndex];
                for (int readerIndex = 0; readerIndex < m_readers.Count; readerIndex++)
                {
                    DataSetReader reader = m_readers[readerIndex];
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
        public async ValueTask EnableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State.TryEnable())
            {
                for (int i = 0; i < m_readers.Count; i++)
                {
                    DataSetReader reader = m_readers[i];
                    _ = reader.State.TryEnable();
                }
                if (State.TryMarkOperational())
                {
                    _ = State.TryResumeCascade();
                }
            }
            SubscribeRoleChanges();
            await ApplyActivationRoleAsync(cancellationToken).ConfigureAwait(false);
            if (m_scheduler is not null && m_diagnostics is not null && m_timeoutWatcher is null)
            {
                m_timeoutWatcher = new DataSetReaderTimeoutWatcher(
                    m_readers,
                    m_scheduler,
                    m_diagnostics,
                    m_telemetry);
                await m_timeoutWatcher.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        internal void ConfigureActivationCoordinator(
            string componentId,
            IPubSubActivationCoordinator? activationCoordinator)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId is required.", nameof(componentId));
            }

            IPubSubActivationCoordinator previous;
            bool unsubscribe;
            lock (m_gate)
            {
                previous = m_activationCoordinator;
                unsubscribe = m_roleChangedSubscribed;
                m_activationCoordinator = activationCoordinator ?? AlwaysActiveCoordinator.Instance;
                m_componentId = componentId;
                m_roleChangedSubscribed = false;
            }
            if (unsubscribe)
            {
                previous.RoleChanged -= OnRoleChanged;
                SubscribeRoleChanges();
            }
        }

        internal async ValueTask ApplyActivationRoleAsync(CancellationToken cancellationToken = default)
        {
            IPubSubActivationCoordinator coordinator;
            string componentId;
            lock (m_gate)
            {
                coordinator = m_activationCoordinator;
                componentId = m_componentId;
            }

            PubSubComponentRole role = await coordinator.GetRoleAsync(componentId, cancellationToken)
                .ConfigureAwait(false);
            ApplyActivationRole(role);
        }

        private void SubscribeRoleChanges()
        {
            IPubSubActivationCoordinator coordinator;
            lock (m_gate)
            {
                if (m_roleChangedSubscribed)
                {
                    return;
                }

                coordinator = m_activationCoordinator;
                m_roleChangedSubscribed = true;
            }
            coordinator.RoleChanged += OnRoleChanged;
        }

        private void UnsubscribeRoleChanges()
        {
            IPubSubActivationCoordinator coordinator;
            lock (m_gate)
            {
                if (!m_roleChangedSubscribed)
                {
                    return;
                }

                coordinator = m_activationCoordinator;
                m_roleChangedSubscribed = false;
            }
            coordinator.RoleChanged -= OnRoleChanged;
        }

        private void OnRoleChanged(object? sender, PubSubRoleChangedEventArgs e)
        {
            if (!string.Equals(e.ComponentId, m_componentId, StringComparison.Ordinal))
            {
                return;
            }

            ApplyActivationRole(e.Role);
        }

        private void ApplyActivationRole(PubSubComponentRole role)
        {
            if (role == PubSubComponentRole.Standby)
            {
                _ = State.TryPause(PubSubStateTransitionReason.ByParent);
                return;
            }

            if (State.State == PubSubState.Paused)
            {
                _ = State.TryResume(PubSubStateTransitionReason.ByParent);
            }
            if (State.State == PubSubState.PreOperational)
            {
                _ = State.TryMarkOperational(PubSubStateTransitionReason.ByParent);
            }
            if (State.State == PubSubState.Operational)
            {
                _ = State.TryResumeCascade();
            }
        }

        /// <summary>
        /// Disables the reader group and every child reader.
        /// </summary>
        public async ValueTask DisableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnsubscribeRoleChanges();
            DataSetReaderTimeoutWatcher? watcher = m_timeoutWatcher;
            m_timeoutWatcher = null;
            if (watcher is not null)
            {
                await watcher.DisposeAsync().ConfigureAwait(false);
            }
            _ = State.TryDisable();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return DisableAsync(CancellationToken.None);
        }
    }
}
