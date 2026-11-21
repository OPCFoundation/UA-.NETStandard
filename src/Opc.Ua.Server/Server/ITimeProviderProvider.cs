// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes a server's
    /// <see cref="TimeProvider"/> so subordinate components can resolve
    /// the server-wide clock without any change to
    /// <see cref="IServerInternal"/>; external/mocked
    /// <see cref="IServerInternal"/> implementations remain unaffected
    /// and can fall back to <see cref="TimeProvider.System"/> when this
    /// interface is not implemented.
    /// </summary>
    /// <remarks>
    /// Consumers should resolve the time provider in a null-safe manner,
    /// e.g.:
    /// <code>
    /// var tp = (server as ITimeProviderProvider)?.TimeProvider
    ///          ?? TimeProvider.System;
    /// </code>
    /// </remarks>
    public interface ITimeProviderProvider
    {
        /// <summary>
        /// Gets the server's <see cref="TimeProvider"/>; never
        /// <c>null</c>. Defaults to <see cref="TimeProvider.System"/>
        /// when the server was constructed without an explicit provider.
        /// </summary>
        TimeProvider TimeProvider { get; }
    }
}
