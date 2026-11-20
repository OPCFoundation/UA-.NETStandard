/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.ISA95.Client.Hosting
{
    /// <summary>
    /// Options for ISA-95 client DI registration.
    /// </summary>
    public sealed class Isa95ClientOptions
    {
        /// <summary>
        /// Gets or sets whether <see cref="IIsa95ClientFactory.ConnectAsync"/>
        /// lazily acquires the registered ManagedSession. Defaults to true.
        /// </summary>
        public bool LazyConnect { get; set; } = true;
    }
}
