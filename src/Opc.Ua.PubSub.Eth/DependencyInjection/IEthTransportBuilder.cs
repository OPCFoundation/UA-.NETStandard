// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent builder returned after registering the OPC UA PubSub
    /// Ethernet transport. Extensions such as <c>WithPcap()</c> chain on
    /// this interface.
    /// </summary>
    public interface IEthTransportBuilder : IPubSubBuilder
    {
        /// <summary>
        /// Gets the underlying PubSub builder.
        /// </summary>
        IPubSubBuilder PubSubBuilder { get; }
    }
}
