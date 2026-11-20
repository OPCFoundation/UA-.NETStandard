/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// The registered answer to whether a hosted server's NodeManagers watch
    /// for NodeId collisions.
    /// </summary>
    /// <remarks>
    /// A type of its own rather than a bare <c>bool</c> so that dependency
    /// injection can tell "the server was configured to leave it off" from
    /// "the server was not configured", which is the difference between
    /// overriding <see cref="DefaultNodeIdFactory.DetectCollisionsByDefault"/>
    /// and deferring to it.
    /// </remarks>
    /// <param name="Enabled">Whether NodeManagers watch for collisions.</param>
    public sealed record NodeIdCollisionDetection(bool Enabled);
}
