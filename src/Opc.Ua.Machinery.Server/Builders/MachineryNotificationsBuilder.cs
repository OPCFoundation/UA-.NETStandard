// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Configures the OPC 40001-1 <c>Notifications</c> add-in.
    /// </summary>
    /// <remarks>
    /// OPC 40001-1 §18 makes <c>Notifications</c> the event notifier a machine
    /// publishes its own events on; the specification deliberately says
    /// nothing about which events those are. The add-in alone is only half of
    /// it — without a way to put an event on it the notifier is a node nobody
    /// can ever hear anything from — so this builder supplies the two shapes
    /// an application needs: a pull source registered with the node manager's
    /// event registry, and an imperative publisher for events the application
    /// raises itself.
    /// </remarks>
    public interface IMachineryNotificationsBuilder
    {
        /// <summary>
        /// Gets the notifications add-in being configured.
        /// </summary>
        NotificationsState State { get; }

        /// <summary>
        /// Registers an asynchronous event source with the node manager. The
        /// iterator is pumped while a client monitors the notifier, or
        /// continuously when <paramref name="options"/> asks for it.
        /// </summary>
        /// <typeparam name="TEvent">The event type produced.</typeparam>
        /// <param name="factory">Produces the events to publish.</param>
        /// <param name="options">Optional publishing options.</param>
        IMachineryNotificationsBuilder Publish<TEvent>(
            Func<NotificationsState, ISystemContext, CancellationToken, IAsyncEnumerable<TEvent>>
                factory,
            EventPublishOptions? options = null)
            where TEvent : BaseEventState;

        /// <summary>
        /// Hands back the publisher the application reports events with after
        /// the machine is built.
        /// </summary>
        /// <param name="publisher">Receives the publisher.</param>
        IMachineryNotificationsBuilder Bind(out IMachineryNotificationPublisher publisher);
    }

    /// <summary>
    /// Reports events on a machine's OPC 40001-1 <c>Notifications</c> add-in.
    /// </summary>
    public interface IMachineryNotificationPublisher
    {
        /// <summary>
        /// Gets the notifier events are reported on.
        /// </summary>
        NotificationsState Notifier { get; }

        /// <summary>
        /// Gets the system context an event is initialised against.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Reports an event on the machine's notifier.
        /// </summary>
        /// <remarks>
        /// The event has to carry a concrete type definition. An abstract one
        /// — OPC 40001 declares two — is refused rather than silently reported
        /// as an event no client can filter for; mint a concrete subtype
        /// instead.
        /// </remarks>
        /// <param name="notification">The event to report.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask PublishAsync(
            BaseEventState notification,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements both <see cref="IMachineryNotificationsBuilder"/> and
    /// <see cref="IMachineryNotificationPublisher"/> over the OPC 40001-1
    /// <c>Notifications</c> add-in, and registers the add-in as a root notifier
    /// once the machine is registered, so its events reach a client subscribed
    /// on the <c>Server</c> object.
    /// </summary>
    internal sealed class MachineryNotificationsBuilder :
        IMachineryNotificationsBuilder,
        IMachineryNotificationPublisher
    {
        public MachineryNotificationsBuilder(
            MachineryBuildScope scope,
            NotificationsState state)
        {
            m_scope = scope;
            State = state;

            // A machine's own events have to reach a client that subscribed on
            // the Server object, which is what the root-notifier registration
            // is for. It can only run once the node is in the address space.
            scope.PostRegistrationActions.Add(_ =>
            {
                scope.BuildContext.Nodes.NodeManager.AddRootNotifier(State);
                m_registered = true;
                return default;
            });
        }

        public NotificationsState State { get; }

        public NotificationsState Notifier => State;

        public ISystemContext Context => m_scope.Context;

        public IMachineryNotificationsBuilder Publish<TEvent>(
            Func<NotificationsState, ISystemContext, CancellationToken, IAsyncEnumerable<TEvent>>
                factory,
            EventPublishOptions? options = null)
            where TEvent : BaseEventState
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            m_scope.EnsureMutable();
            m_scope.PostRegistrationActions.Add(_ =>
            {
                m_scope.BuildContext.Nodes
                    .Node<NotificationsState>(State.NodeId)
                    .Publish(factory, options);
                return default;
            });
            return this;
        }

        public IMachineryNotificationsBuilder Bind(
            out IMachineryNotificationPublisher publisher)
        {
            publisher = this;
            return this;
        }

        public ValueTask PublishAsync(
            BaseEventState notification,
            CancellationToken cancellationToken = default)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }
            _ = cancellationToken;
            if (!m_registered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The machine's Notifications add-in is not registered yet; publish " +
                    "only after BuildAsync has completed.");
            }
            if (notification.TypeDefinitionId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "An event reported on the Notifications add-in must carry a concrete " +
                    "type definition.");
            }
            State.ReportEvent(m_scope.Context, notification);
            return default;
        }

        private readonly MachineryBuildScope m_scope;
        private volatile bool m_registered;
    }
}
