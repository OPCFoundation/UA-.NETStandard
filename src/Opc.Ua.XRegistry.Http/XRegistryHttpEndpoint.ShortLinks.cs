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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    public sealed partial class XRegistryHttpEndpoint
    {
        /// <inheritdoc/>
        public async ValueTask<XRegistryAddressResolution> ResolveAddressAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (!IsShortLink(request.AddressPath ?? request.Path))
            {
                return new XRegistryAddressResolution(request);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_options.RequestTimeout);
            XRegistryEndpointDescription description = await InspectCoreAsync(request.Context, timeout.Token)
                .ConfigureAwait(false);
            return await ResolveAddressCoreAsync(request, description, timeout.Token).ConfigureAwait(false);
        }

        private async ValueTask<XRegistryAddressResolution> ResolveAddressCoreAsync(
            XRegistryRequest request, XRegistryEndpointDescription description, CancellationToken ct)
        {
            if (description.ShortLinkPrefix is null)
            {
                return new XRegistryAddressResolution(request, new XRegistryResponse(405)
                {
                    Error = new XRegistryError("action_not_supported",
                        "The alias profile requires doc reads; no raw representation can be guessed.")
                });
            }
            var probe = new XRegistryRequest(XRegistryAction.Read, request.AddressPath ?? request.Path)
            {
                Context = request.Context,
                Parameters = [new XRegistryParameter("doc", null)]
            };
            XRegistryResponse response = await SendAsync(probe, new XRegistryHttpShape(), description.Model, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return new XRegistryAddressResolution(request, response);
            }
            if (response.Metadata.ValueKind != JsonValueKind.Object ||
                !response.Metadata.TryGetProperty("xid", out JsonElement xid) ||
                xid.ValueKind != JsonValueKind.String ||
                xid.GetString() is not { } canonical ||
                IsShortLink(canonical))
            {
                throw new InvalidDataException("The alias doc response must identify a canonical registry entity.");
            }
            canonical = XRegistryPath.Normalize(canonical);
            if (request.AddressPath is not null && request.Path != canonical)
            {
                return new XRegistryAddressResolution(request, new XRegistryResponse(409)
                {
                    Error = new XRegistryError(
                        "address_changed", "The original alias no longer matches the resolved entity.")
                });
            }
            return new XRegistryAddressResolution(request.AtResolvedPath(canonical));
        }

        private bool IsShortLink(string path)
        {
            return m_options.ShortLinkPrefix is { } prefix &&
                (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal));
        }
    }
}
