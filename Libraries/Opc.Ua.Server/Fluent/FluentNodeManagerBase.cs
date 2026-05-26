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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Opt-in base class for node managers that want to use the fluent
    /// <c>Publish</c> surface (external event sources delivered through
    /// <see cref="NodeState.ReportEvent"/>). The source-generator-emitted
    /// <c>NodeManagerBase</c> derives from this class when any wrapper
    /// in the design exposes a <c>Publish</c> binding; hand-written
    /// managers can also derive directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class owns an <see cref="EventSourceRegistry"/> whose
    /// reconcile loop runs as long as the manager is alive. Override
    /// <see cref="OnSubscribeToEventsAsync"/> hooks the registry so it
    /// activates and deactivates sources in lock-step with
    /// <see cref="NodeState.AreEventsMonitored"/>. <c>Dispose(bool)</c>
    /// tears the registry down before the base implementation runs so
    /// no iterator outlives the manager.
    /// </para>
    /// <para>
    /// Subclasses should never call into <see cref="EventSources"/>
    /// outside the fluent builder pipeline; the surface is exposed for
    /// generated code only.
    /// </para>
    /// </remarks>
    public abstract class FluentNodeManagerBase : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            params string[] namespaceUris)
            : base(server, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, logger, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            params string[] namespaceUris)
            : base(server, configuration, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, logger, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
            m_eventSources = new EventSourceRegistry(this, m_logger);
            m_simulations = new SimulationRegistry(this, m_logger);
        }

        /// <summary>
        /// Registry that the fluent <c>Publish</c> surface stores its
        /// registered event sources in. Accessed by
        /// <see cref="NodeManagerBuilder.AttachEventSources"/> during
        /// <c>Configure</c> and by generated wrappers; not intended for
        /// direct subclass use.
        /// </summary>
        internal EventSourceRegistry EventSources => m_eventSources;

        /// <summary>
        /// Registry that the fluent <c>Simulation</c> surface stores its
        /// registered periodic tick loops in. Started after
        /// <c>Configure</c> completes and torn down on disposal.
        /// </summary>
        internal SimulationRegistry Simulations => m_simulations;

        /// <summary>
        /// Attaches this manager's event-source registry to the supplied
        /// fluent builder so that <c>Publish</c> extension methods can
        /// resolve it. The generator-emitted <c>CreateAddressSpaceAsync</c>
        /// invokes this immediately after constructing the builder; hand-
        /// written managers that build their own
        /// <see cref="NodeManagerBuilder"/> should call this once before
        /// passing the builder into <c>Configure</c>.
        /// </summary>
        /// <param name="builder">
        /// The fluent builder that the manager's <c>Configure</c>
        /// partial(s) will receive.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// Raised when <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public void AttachToBuilder(NodeManagerBuilder builder)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }
            builder.AttachEventSources(m_eventSources);
            builder.AttachSimulations(m_simulations);
        }

        /// <summary>
        /// Signals the registry whenever a notifier's monitored-events
        /// ref-count flips so the reconcile loop can start or stop the
        /// matching iterator. Subclasses that further override
        /// <see cref="AsyncCustomNodeManager.OnSubscribeToEventsAsync"/>
        /// must call <c>base</c> before doing their own work.
        /// </summary>
        protected override ValueTask OnSubscribeToEventsAsync(
            ServerSystemContext context,
            MonitoredNode2 monitoredNode,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            m_eventSources.SignalReconcile();
            return base.OnSubscribeToEventsAsync(context, monitoredNode, unsubscribe, cancellationToken);
        }

        /// <summary>
        /// Cancels every running iterator and waits (bounded by each
        /// source's
        /// <see cref="EventPublishOptions.CancellationTimeout"/>) before
        /// invoking the base disposer. Subclasses that further override
        /// <c>Dispose</c> must call <c>base.Dispose(disposing)</c>.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_simulations.Dispose();
                m_eventSources.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Internal trampoline used by <see cref="EventSourceRegistry"/>
        /// to register a notifier as a root notifier from a background
        /// worker thread. Wraps
        /// <see cref="AsyncCustomNodeManager.AddRootNotifierAsync"/>
        /// so the registry does not have to know its protected
        /// signature.
        /// </summary>
        internal Task AddRootNotifierFromFluentAsync(
            NodeState notifier,
            CancellationToken cancellationToken)
        {
            return AddRootNotifierAsync(notifier, cancellationToken).AsTask();
        }

        private readonly EventSourceRegistry m_eventSources;
        private readonly SimulationRegistry m_simulations;
    }
}
