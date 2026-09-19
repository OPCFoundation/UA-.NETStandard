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

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Supplies application certificates and entry recovery services to the certificate-change processor.
    /// </summary>
    internal interface IChannelCertRotationHost
    {
        /// <summary>
        /// Gets the application configuration and its certificate manager.
        /// </summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Gets the logger used to report certificate loading and rotation failures.
        /// </summary>
        ILogger? Logger { get; }

        /// <summary>
        /// Gets whether the channel manager has begun disposal.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Captures the entries currently registered with the manager.
        /// </summary>
        /// <returns>A snapshot of the registered channel entry references.</returns>
        ChannelEntry[] SnapshotEntries();

        /// <summary>
        /// Takes ownership of the certificate and chain and updates the matching certificate-type snapshot.
        /// </summary>
        /// <param name="clientCertificate">
        /// The replacement application certificate whose ownership is transferred.
        /// </param>
        /// <param name="clientCertificateChain">
        /// The replacement chain whose ownership is transferred, if supplied.
        /// </param>
        void ReplaceClientCertificate(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain);

        /// <summary>
        /// Retains the active rotation task for diagnostics and lifetime tracking.
        /// </summary>
        /// <param name="task">The rotation task, or null to clear the tracked task.</param>
        void SetCertificateRotationTask(Task? task);
    }
}
