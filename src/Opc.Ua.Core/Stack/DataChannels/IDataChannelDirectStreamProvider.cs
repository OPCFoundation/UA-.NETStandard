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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Associates an admitted opaque token with an actual ordered transport stream.
    /// The flow runtime must verify binding, direction, ASSOCIATE/ACK and READY before using payloads.
    /// Provider availability alone does not establish an authenticated direct capability.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelDirectStreamProvider
    {
        /// <summary>
        /// Gets whether the owning transport supports token-prefaced ordered streams.
        /// </summary>
        bool AreDirectStreamsAvailable { get; }

        /// <summary>
        /// Opens an actual stream and writes its UA Binary Guid token preface before returning it.
        /// The caller must already have authenticated this sending direction and its token.
        /// Tokens are single-use even when allocation or transmission fails.
        /// The returned stream supports asynchronous I/O and must be disposed asynchronously.
        /// If ordinary disposal can abandon accepted writes, it must also implement
        /// <see cref="IDataChannelDirectStreamCompletion"/> to retain an orderly END before release.
        /// </summary>
        ValueTask<Stream> OpenDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers an authenticated receiving token before the peer is allowed to open its stream,
        /// then returns the actual stream after consuming and validating its complete token preface.
        /// Unknown, premature and reused stream tokens are refused without returning payloads.
        /// Cancellation or expiry does not make a token reusable.
        /// The returned stream supports asynchronous I/O and must be disposed asynchronously.
        /// </summary>
        ValueTask<Stream> AcceptDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default);
    }
}
