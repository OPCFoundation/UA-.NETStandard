// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Allows OPC 10000-4 §6.6.5 administrative Server state changes to publish a new <c>ServiceLevel</c> through the
    /// active provider.
    /// </summary>
    public interface IServiceLevelController
    {
        /// <summary>
        /// Sets the <c>ServiceLevel</c> and raises the provider change notification.
        /// </summary>
        /// <param name="serviceLevel">The service level to publish.</param>
        void SetServiceLevel(byte serviceLevel);
    }
}
