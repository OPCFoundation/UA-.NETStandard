/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Configuration options for <see cref="GlobalDiscoveryServerClient"/> and
    /// <see cref="ServerPushConfigurationClient"/>. Bound by
    /// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> when the
    /// clients are resolved from the dependency injection container.
    /// </summary>
    public sealed class GdsClientOptions
    {
        /// <summary>
        /// Session lifetime requested when establishing a new session with the
        /// server. Defaults to one minute.
        /// </summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Maximum number of connect attempts before connecting
        /// gives up and rethrows the last <see cref="ServiceResultException"/>.
        /// Must be greater than zero. Defaults to <c>5</c>.
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 5;

        /// <summary>
        /// Delay between connect retries. Defaults to one second.
        /// </summary>
        public TimeSpan ConnectBackoff { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum trust list size accepted on read or sent on update, in
        /// bytes. Defaults to one megabyte.
        /// </summary>
        public long MaxTrustListSize { get; set; } = 1 * 1024 * 1024;

        /// <summary>
        /// Chunk size used for the trust-list file transfer. Must be greater
        /// than zero. Defaults to <c>256</c> bytes.
        /// </summary>
        public int FileTransferChunkSize { get; set; } = 256;
    }
}
