// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Factory for creating <see cref="ISubscriptionEngine"/> instances.
    /// An engine factory can be injected at session creation time to
    /// select the publish strategy. If not provided, the session uses
    /// the classic (V1) engine by default.
    /// </summary>
    public interface ISubscriptionEngineFactory
    {
        /// <summary>
        /// Create a new <see cref="ISubscriptionEngine"/> for the
        /// given session context.
        /// </summary>
        /// <param name="context">The context providing session
        /// services to the engine.</param>
        /// <returns>A new subscription engine instance.</returns>
        ISubscriptionEngine Create(ISubscriptionEngineContext context);
    }
}
