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
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Checks public evidence for configured restricted field names, local paths, and credential-bearing values.
    /// </summary>
    internal static partial class PublicEvidenceSafety
    {
        /// <summary>
        /// Recursively applies the restricted-content checks, including bounded decoding of supported attestation
        /// payloads.
        /// </summary>
        public static bool Check(JsonElement value, int depth = 0)
        {
            if (depth > 64)
            {
                return false;
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (RestrictedField().IsMatch(property.Name) || !Check(property.Value, depth + 1))
                    {
                        return false;
                    }
                }
                if (value.TryGetProperty("payloadType", out JsonElement type) &&
                    value.TryGetProperty("payload", out JsonElement payload) &&
                    type.ValueKind == JsonValueKind.String && payload.ValueKind == JsonValueKind.String)
                {
                    if (type.GetString() is not
                        ("application/vnd.in-toto+json" or "application/vnd.opcua.release-record+json") ||
                        payload.GetString()!.Length > 8 * 1024 * 1024)
                    {
                        return false;
                    }
                    try
                    {
                        using JsonDocument decoded = EvidenceFiles.ParseJson(
                            Convert.FromBase64String(payload.GetString()!));
                        return Check(decoded.RootElement, depth + 1);
                    }
                    catch (Exception ex) when (ex is FormatException or JsonException)
                    {
                        return false;
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (!Check(item, depth + 1))
                    {
                        return false;
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.String && RestrictedValue().IsMatch(value.GetString()!))
            {
                return false;
            }
            return true;
        }

        [GeneratedRegex(
            "password|clientSecret|apiKey|packageFolders|restoreSources|^rawFindings$|^rawTestLogs?$|^caseDetails$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex RestrictedField();

        [GeneratedRegex(
            @"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|\\\\|file://|https?://[^/\s]+@|[?&](token|sig|key|password)=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex RestrictedValue();
    }
}
