// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Options that configure an external-server PubSub publisher wired through
    /// <c>AddServerAsPublisher</c>. The publisher reads the configured
    /// PublishedDataSets from an external OPC UA server and emits them as PubSub
    /// DataSets, either by issuing cyclic Read calls or by maintaining client
    /// Subscriptions.
    /// </summary>
    /// <remarks>
    /// Simple properties are bindable from <c>IConfiguration</c>. Object-typed
    /// members, such as <see cref="ServerConnectionOptions.ApplicationConfiguration"/>
    /// and <see cref="ServerConnectionOptions.UserIdentity"/>, must be supplied
    /// from code.
    /// </remarks>
    public sealed class ServerPublisherOptions
    {
        /// <summary>
        /// The connection options describing the external OPC UA server the
        /// publisher reads from.
        /// </summary>
        public ServerConnectionOptions Connection { get; set; } = new();

        /// <summary>
        /// Selects how the publisher obtains the source values. Defaults to
        /// <see cref="ReadMode.Cyclic"/>.
        /// </summary>
        public ReadMode ReadMode { get; set; } = ReadMode.Cyclic;

        /// <summary>
        /// Selects how monitored items are grouped into client Subscriptions when
        /// <see cref="ReadMode"/> is <see cref="ReadMode.Subscription"/>.
        /// Defaults to <see cref="SubscriptionAffinity.WriterGroup"/>.
        /// </summary>
        public SubscriptionAffinity Affinity { get; set; }
            = SubscriptionAffinity.WriterGroup;
    }
}
