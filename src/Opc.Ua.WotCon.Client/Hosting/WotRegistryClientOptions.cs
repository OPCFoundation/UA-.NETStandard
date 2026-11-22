// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.WotCon.Client.Hosting
{
    /// <summary>
    /// Top-level options for
    /// <c>Microsoft.Extensions.DependencyInjection.OpcUaWotConClientBuilderExtensions.AddWotRegistryClient</c>.
    /// </summary>
    /// <remarks>
    /// Configurable client-side knobs for the WoT Connectivity 1.1 registry
    /// client surface. Today this is intentionally minimal: the connection
    /// itself is owned by the regular <c>.AddClient(...)</c> session
    /// factory and the registry client only composes on top of an
    /// established <see cref="Ua.Client.ISession"/>.
    /// </remarks>
    public sealed class WotRegistryClientOptions
    {
        /// <summary>
        /// When <c>true</c> (the default), the WoT registry client
        /// factory delegate registered with DI lazily connects the
        /// underlying <see cref="Ua.Client.ManagedSession"/> the first
        /// time it is awaited. Set to <c>false</c> when callers supply
        /// their own session lifecycle and only want a typed wrapper.
        /// </summary>
        public bool LazyConnect { get; set; } = true;
    }
}
