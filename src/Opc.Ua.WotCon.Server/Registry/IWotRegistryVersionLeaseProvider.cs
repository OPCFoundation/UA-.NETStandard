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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Optional owner capability for protecting an exact Version from retention eviction.
    /// Acquisition is atomic with the owner's allocation and retention decisions.
    /// </summary>
    public interface IWotRegistryVersionLeaseProvider
    {
        /// <summary>
        /// Acquires a lease on the current incarnation represented by a provider-issued Version
        /// snapshot. A deleted or replaced incarnation is rejected, rather than leased by id alone.
        /// The caller owns the returned lease and must dispose it when access ends.
        /// </summary>
        ValueTask<IWotRegistryVersionLease> AcquireVersionLeaseAsync(
            string groupId,
            string resourceId,
            WotResourceVersion version,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Protects one exact Version incarnation from retention until disposed.
    /// This is not a content-write reservation or a durable cross-process lease.
    /// </summary>
    public interface IWotRegistryVersionLease : IDisposable
    {
        /// <summary>
        /// Gets the canonical Version snapshot captured at acquisition.
        /// </summary>
        WotResourceVersion Version { get; }
    }
}
