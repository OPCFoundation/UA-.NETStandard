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
using System.Linq;
using System.Text.Json;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal static class XRegistryHttpDocumentReference
    {
        public static bool IsRedirect(XRegistryResponse response, XRegistryRequest request, XRegistryHttpShape shape)
        {
            return IsRedirect(response.StatusCode, response.Metadata, response.Location, request, shape);
        }

        public static bool IsRedirect(
            int status, JsonElement metadata, string? location, XRegistryRequest request, XRegistryHttpShape shape)
        {
            if (status != 303 || !shape.IsDocumentView(request, response: true))
            {
                return false;
            }
            if (location is null ||
                metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty(shape.Singular + "url", out JsonElement reference) ||
                reference.ValueKind != JsonValueKind.String ||
                reference.GetString() != location ||
                location.Length is 0 or > 4096 ||
                location.Any(char.IsControl) ||
                location.Contains('\\', StringComparison.Ordinal) ||
                !Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out Uri? uri) ||
                (uri.IsAbsoluteUri && uri.UserInfo.Length != 0))
            {
                throw new InvalidDataException("A document redirect must match its valid external document URI.");
            }
            return true;
        }
    }
}
