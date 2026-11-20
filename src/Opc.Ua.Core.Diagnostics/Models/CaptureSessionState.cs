// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// State a <c>CaptureSession</c> can be in.
    /// </summary>
    public enum CaptureSessionState
    {
        /// <summary>
        /// The session has been created but not yet started.
        /// </summary>
        Starting = 0,

        /// <summary>
        /// The session is actively capturing.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The session is in the middle of being stopped.
        /// </summary>
        Stopping = 2,

        /// <summary>
        /// The session stopped cleanly.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// The session terminated because of an error.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// The session has been disposed.
        /// </summary>
        Disposed = 5
    }
}
