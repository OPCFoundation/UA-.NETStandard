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

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryOpcUaEndpoint : IXRegistryAddressResolver
    {
        /// <inheritdoc/>
        public async ValueTask<XRegistryAddressResolution> ResolveAddressAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            XRegistryEndpointDescription description = await InspectAsync(request.Context, cancellationToken)
                .ConfigureAwait(false);
            string address = request.AddressPath ?? request.Path;
            if (description.ShortLinkPrefix is not { } prefix ||
                (address != prefix && !address.StartsWith(prefix + "/", StringComparison.Ordinal)))
            {
                return new XRegistryAddressResolution(request);
            }
            XRegistryResponse response = await ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, address)
            {
                Context = request.Context,
                View = XRegistryView.Metadata
            }, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return new XRegistryAddressResolution(request, response);
            }
            if (response.Metadata.ValueKind != JsonValueKind.Object ||
                !response.Metadata.TryGetProperty("xid", out JsonElement xid) ||
                xid.ValueKind != JsonValueKind.String ||
                xid.GetString() is not { } canonical)
            {
                throw new InvalidDataException("The native alias response did not identify its canonical entity.");
            }
            return new XRegistryAddressResolution(request.AtResolvedPath(canonical));
        }
    }
}
