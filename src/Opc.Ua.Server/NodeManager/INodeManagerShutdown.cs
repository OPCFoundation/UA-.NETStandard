// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Drains accepted work that can call back into the server before serialized
    /// address-space deletion or disposal begins.
    /// </summary>
    public interface INodeManagerShutdown
    {
        /// <summary>
        /// Stops admission and drains accepted work while session notifications and
        /// the address space are still available. The lifecycle joins an in-flight
        /// attempt and may invoke the hook again after a failure. Implementations must
        /// retain unresolved cleanup failures and avoid repeating cleanup that is not
        /// safe to retry.
        /// </summary>
        ValueTask PrepareForShutdownAsync();
    }
}
