// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent builder returned after registering the OPC UA PubSub UDP transport.
    /// </summary>
    public interface IUdpTransportBuilder : IPubSubBuilder
    {
        /// <summary>
        /// Gets the underlying PubSub builder.
        /// </summary>
        IPubSubBuilder PubSubBuilder { get; }
    }
}
