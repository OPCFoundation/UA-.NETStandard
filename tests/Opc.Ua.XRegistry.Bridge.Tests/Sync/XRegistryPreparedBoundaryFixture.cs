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
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    /// <summary>
    /// Borrows a real transaction provider while injecting only its externally visible preparation response.
    /// </summary>
    internal sealed class XRegistryPreparedBoundaryFixture(XRegistryTransactionalEndpoint endpoint)
        : IXRegistryPreparedEndpoint
    {
        public int Preparations { get; private set; }

        public int Commits { get; private set; }

        public int Disposals { get; private set; }

        public XRegistryRequest? LastPreparedRequest { get; private set; }

        public Func<XRegistryRequest, XRegistryResponse, XRegistryResponse>? RewritePreview { get; set; }

        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            XRegistryEndpointDescription description =
                await endpoint.InspectAsync(context, cancellationToken).ConfigureAwait(false);
            return description with { SupportsPreparedSnapshots = false };
        }

        public ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            return endpoint.ExecuteAsync(request, cancellationToken);
        }

        public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            Preparations++;
            LastPreparedRequest = request;
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
            bool transferred = false;
            try
            {
                XRegistryResponse response = RewritePreview?.Invoke(request, operation.Response) ?? operation.Response;
                var wrapped = new BoundaryOperation(this, operation, response);
                transferred = true;
                return wrapped;
            }
            finally
            {
                if (!transferred)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class BoundaryOperation(
            XRegistryPreparedBoundaryFixture owner, IXRegistryPreparedOperation operation, XRegistryResponse response)
            : IXRegistryPreparedOperation
        {
            public XRegistryResponse Response => response;

            public ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Commits++;
                return operation.CommitAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                owner.Disposals++;
                return operation.DisposeAsync();
            }
        }
    }
}
