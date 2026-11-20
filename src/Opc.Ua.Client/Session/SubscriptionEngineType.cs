/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// Specifies which subscription engine implementation to use
    /// for managing the publish loop and notification dispatch.
    /// </summary>
    public enum SubscriptionEngineType
    {
        /// <summary>
        /// The classic (V1) subscription engine. Uses fire-and-forget
        /// publish requests with task continuations for dispatch.
        /// This is the default and provides full backward compatibility.
        /// </summary>
        Classic = 0,

        /// <summary>
        /// The Channel-based (V2) subscription engine. Uses a pool of
        /// publish workers with <c>System.Threading.Channels</c> for
        /// ordered notification dispatch and automatic worker scaling.
        /// </summary>
        ChannelBased = 1
    }
}
