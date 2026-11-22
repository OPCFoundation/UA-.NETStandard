// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Exposes the resource store that holds WoT document bytes for a registry store.
    /// </summary>
    internal interface IWotRegistryResourceStoreProvider
    {
        /// <summary>
        /// Gets the content-addressed resource store used for document bytes.
        /// </summary>
        IXRegistryResourceStore ResourceStore { get; }
    }
}
