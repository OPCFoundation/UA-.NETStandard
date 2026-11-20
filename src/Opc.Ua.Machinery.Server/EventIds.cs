/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Event identifiers used by the Machinery server surface.
    /// </summary>
    public static class MachineryServerEventIds
    {
        /// <summary>
        /// The Machinery address space finished loading.
        /// </summary>
        public const int AddressSpaceReady = 16000;

        /// <summary>
        /// A result-transfer handle was reclaimed after its timeout elapsed.
        /// </summary>
        public const int ResultTransferHandleExpired = 16001;

        /// <summary>
        /// A transient result file could not be created.
        /// </summary>
        public const int ResultTransferFileFailed = 16002;

        /// <summary>
        /// A transient result file could not be removed.
        /// </summary>
        public const int ResultTransferFileNotRemoved = 16004;

        /// <summary>
        /// The result-ready event stream faulted.
        /// </summary>
        public const int ResultEventStreamFailed = 16003;

        /// <summary>
        /// The OPC 40001-3 job order change stream (catalog changes or job
        /// status notifications) faulted.
        /// </summary>
        public const int JobOrderChangeStreamFailed = 16005;
    }
}
