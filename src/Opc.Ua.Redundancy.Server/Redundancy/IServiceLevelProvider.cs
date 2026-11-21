// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Computes the value of the Server's OPC UA <c>ServiceLevel</c>
    /// variable (0–255). In a <c>RedundantServerSet</c>, Clients select the Server
    /// reporting the highest <c>ServiceLevel</c> (OPC 10000-4 §6.6.2.4.2 and §6.6.2.4.3), so a healthy
    /// active leader reports the maximum and standbys report a lower value.
    /// </summary>
    /// <remarks>
    /// Wire this into the server so the <c>Server.ServiceLevel</c> node is
    /// initialized from <see cref="GetServiceLevel"/> and updated whenever
    /// <see cref="ServiceLevelChanged"/> fires. The default
    /// (<see cref="ConstantServiceLevelProvider"/>) reports a fixed 255,
    /// preserving single-instance behavior.
    /// </remarks>
    public interface IServiceLevelProvider
    {
        /// <summary>
        /// The current <c>ServiceLevel</c> (Table 105: Maintenance 0, NoData 1, Degraded 2-199, Healthy 200-255).
        /// </summary>
        byte GetServiceLevel();

        /// <summary>
        /// Raised with the new service level whenever it changes.
        /// </summary>
        event Action<byte>? ServiceLevelChanged;
    }
}
