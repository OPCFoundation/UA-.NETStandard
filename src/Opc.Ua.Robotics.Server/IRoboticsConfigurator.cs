// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Configures a Robotics address space after predefined models are available.
    /// </summary>
    public interface IRoboticsConfigurator
    {
        /// <summary>
        /// Configures the active Robotics build context.
        /// </summary>
        ValueTask ConfigureAsync(
            IRoboticsBuildContext context,
            CancellationToken cancellationToken);
    }
}
