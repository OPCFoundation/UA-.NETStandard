// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Marks a NodeId factory that is safe for Robotics topology construction.
    /// </summary>
    /// <remarks>
    /// Implementations of <c>New</c> must be thread-safe, reserve unique NodeIds
    /// that are not reused while created nodes remain unregistered, and allocate
    /// Robotics application instances in the configured instance namespace.
    /// </remarks>
    public interface IRoboticsNodeIdFactory : Di.Server.ICompanionNodeIdFactory
    {
    }
}
