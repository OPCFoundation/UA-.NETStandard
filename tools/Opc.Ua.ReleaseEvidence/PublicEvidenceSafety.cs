// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Opc.Ua.ReleaseEvidence
{
    internal static partial class PublicEvidenceSafety
    {
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
