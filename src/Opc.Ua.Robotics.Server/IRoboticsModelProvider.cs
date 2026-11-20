/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Contributes compiled predefined model nodes to a Robotics node manager.
    /// </summary>
    /// <remarks>
    /// Providers run in ascending <see cref="Opc.Ua.Di.Server.IDiModelProvider.Order"/>
    /// and the built-in core-model provider runs first. Providers that replace the
    /// built-in core-model provider must advertise the IA and Robotics namespace URIs
    /// through <see cref="Opc.Ua.Di.Server.IDiModelProvider.NamespaceUris"/>.
    /// </remarks>
    public interface IRoboticsModelProvider : Opc.Ua.Di.Server.IDiModelProvider
    {
    }
}
