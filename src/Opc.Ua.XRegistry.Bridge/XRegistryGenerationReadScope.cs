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
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge
{
    /// <summary>
    /// Pins one optimistic inventory without retaining an upstream lease. Unqualified
    /// endpoints remain explicitly unguarded; their independent scan checks still apply.
    /// </summary>
    internal sealed class XRegistryGenerationReadScope : IXRegistryEndpoint
    {
        public XRegistryGenerationReadScope(IXRegistryEndpoint endpoint, XRegistryEndpointDescription description)
        {
            m_endpoint = endpoint.ThrowIfNull(nameof(endpoint));
            description.ThrowIfNull(nameof(description));
            if (description.SupportsGenerationGuards)
            {
                m_generation = !string.IsNullOrEmpty(description.Generation) ? description.Generation :
                    throw new InvalidDataException("The endpoint advertised generation guards without a generation.");
            }
        }

        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            XRegistryEndpointDescription current = await m_endpoint.InspectAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (m_generation is not null &&
                (!current.SupportsGenerationGuards || current.Generation != m_generation))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The registry generation changed during inventory.");
            }
            return current;
        }

        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (request.IsMutation ||
                (request.ExpectedGeneration is not null && request.ExpectedGeneration != m_generation))
            {
                throw new InvalidOperationException("An inventory scope cannot mutate or switch generations.");
            }
            XRegistryResponse response = await m_endpoint.ExecuteAsync(
                request with { ExpectedGeneration = m_generation }, cancellationToken).ConfigureAwait(false);
            if (m_generation is not null && response.IsSuccess && response.Generation != m_generation)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The endpoint returned an unqualified or different generation for a guarded read.");
            }
            return response;
        }

        private readonly IXRegistryEndpoint m_endpoint;
        private readonly string? m_generation;
    }
}
