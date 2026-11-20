// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Contributes compiled predefined model nodes to a Machinery node manager.
    /// </summary>
    /// <remarks>
    /// Providers run in ascending <see cref="Opc.Ua.Di.Server.IDiModelProvider.Order"/>
    /// and the built-in core-model provider runs first. Providers that replace the
    /// built-in core-model provider must advertise the namespaces of every configured
    /// <see cref="MachineryParts"/> value through
    /// <see cref="Opc.Ua.Di.Server.IDiModelProvider.NamespaceUris"/>.
    /// </remarks>
    public interface IMachineryModelProvider : Opc.Ua.Di.Server.IDiModelProvider
    {
    }
}
