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

namespace Opc.Ua
{
    internal static class TransportProfileIdentity
    {
        public static string GetEffective(string? profile, string endpointUrl, bool binaryEncoding = true)
        {
            if (!string.IsNullOrEmpty(profile))
            {
                return Profiles.NormalizeUri(profile);
            }
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri))
            {
                return string.Empty;
            }
            return uri.Scheme switch
            {
                Utils.UriSchemeOpcTcp => Profiles.UaTcpTransport,
                Utils.UriSchemeHttps or Utils.UriSchemeOpcHttps =>
                    binaryEncoding ? Profiles.HttpsBinaryTransport : Profiles.HttpsJsonTransport,
                Utils.UriSchemeOpcHttpsWebApi => Profiles.HttpsOpenApiTransport,
                Utils.UriSchemeOpcWss =>
                    binaryEncoding ? Profiles.UaWssTransport : Profiles.UaWssJsonTransport,
                Utils.UriSchemeOpcWssOpenApi => Profiles.WssOpenApiTransport,
                _ => string.Empty
            };
        }

        public static bool IsHttps(string? profile)
        {
            return profile is Profiles.HttpsBinaryTransport or Profiles.HttpsJsonTransport or Profiles.HttpsOpenApiTransport;
        }
    }
}
