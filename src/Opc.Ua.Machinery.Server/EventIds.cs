/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
