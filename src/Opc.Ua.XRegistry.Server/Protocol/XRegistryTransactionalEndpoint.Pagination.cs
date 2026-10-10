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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private void ApplyContinuation(Transaction transaction, XRegistryTarget target)
        {
            XRegistryRequest request = transaction.Request;
            XRegistryParameter[] cursors =
                [.. request.Parameters.ToList().Where(parameter => parameter.Name == "cursor")];
            if (cursors.Length == 0)
            {
                return;
            }
            if (cursors.Length != 1 ||
                request.Parameters.Count != 1 ||
                request.Action != XRegistryAction.Read ||
                !target.IsCollection ||
                cursors[0].Value is not { Length: > 0 and <= 65536 } token)
            {
                throw new XRegistryRejectionException(
                    "bad_flag", "A continuation must be used unmodified on its collection.");
            }
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid continuation envelope.");
                }
                byte[] payload = FromUrlBase64(parts[0]);
                byte[] supplied = FromUrlBase64(parts[1]);
                using var hmac = new HMACSHA256(m_cursorKey);
                byte[] expected = hmac.ComputeHash(payload);
                int difference = expected.Length ^ supplied.Length;
                for (int index = 0; index < expected.Length; index++)
                {
                    difference |= expected[index] ^ (index < supplied.Length ? supplied[index] : 0);
                }
                if (difference != 0)
                {
                    throw new FormatException("The continuation signature is invalid.");
                }
                using var document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                string? generation = request.ExpectedGeneration ?? m_observationGeneration;
                DateTimeOffset expires = root.GetProperty("expires").GetDateTimeOffset();
                if (root.GetProperty("path").GetString() != request.Path ||
                    root.GetProperty("view").GetInt32() != (int)request.View ||
                    root.GetProperty("scope").GetString() != CursorScope(request.Context) ||
                    root.GetProperty("generation").GetString() != generation ||
                    expires <= m_time.GetUtcNow())
                {
                    throw new FormatException("The continuation is stale or belongs to another path, view or caller.");
                }
                int offset = root.GetProperty("offset").GetInt32();
                int limit = root.GetProperty("limit").GetInt32();
                if (offset <= 0 || limit <= 0 || limit > m_options.PageSize)
                {
                    throw new FormatException("The continuation window is invalid.");
                }
                var parameters = new List<XRegistryParameter>();
                foreach (JsonElement parameter in root.GetProperty("parameters").EnumerateArray())
                {
                    parameters.Add(new XRegistryParameter(parameter.GetProperty("name").GetString()!,
                        parameter.GetProperty("value").GetString()));
                }
                transaction.PageOffset = offset;
                transaction.PageLimit = limit;
                transaction.CursorExpires = expires;
                transaction.Request = request with { Parameters = [.. parameters] };
            }
            catch (Exception exception) when (exception is FormatException or JsonException
                or InvalidOperationException or OverflowException or ArgumentException or KeyNotFoundException)
            {
                throw new XRegistryRejectionException("bad_flag", "The continuation is invalid or no longer current.");
            }
        }

        private XRegistryResponse Page(Transaction transaction, XRegistryTarget target, JsonObject collection)
        {
            XRegistryRequest request = transaction.Request;
            XRegistryParameter[] limits =
                [.. request.Parameters.ToList().Where(parameter => parameter.Name == "limit")];
            if (request.Action != XRegistryAction.Read)
            {
                if (limits.Length != 0 || transaction.PageOffset != 0)
                {
                    throw new XRegistryRejectionException(
                        "bad_flag", "Pagination is supported only for collection reads.");
                }
                return new XRegistryResponse(200) { Metadata = Element(collection) };
            }
            int limit = transaction.PageLimit == 0 ? m_options.PageSize : transaction.PageLimit;
            if (limits.Length != 0)
            {
                if (limits.Length != 1 ||
                    !ulong.TryParse(
                        limits[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong requested) ||
                    requested == 0)
                {
                    throw new XRegistryRejectionException("bad_flag", "limit must be a positive UInt64.");
                }
                limit = (int)Math.Min((ulong)limit, requested);
            }
            int offset = transaction.PageOffset;
            if (offset > collection.Count)
            {
                throw new XRegistryRejectionException(
                    "bad_flag", "The continuation is outside the current result set.");
            }
            var page = new JsonObject(collection.Skip(offset).Take(limit)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
            if ((long)offset + limit >= collection.Count)
            {
                return new XRegistryResponse(200) { Metadata = Element(page) };
            }
            var parameters = new JsonArray();
            foreach (XRegistryParameter parameter in request.Parameters)
            {
                JsonNode encoded = new JsonObject { ["name"] = parameter.Name, ["value"] = parameter.Value };
                parameters.Add(encoded);
            }
            DateTimeOffset expires = transaction.CursorExpires ?? m_time.GetUtcNow().Add(m_options.CursorLifetime);
            var cursor = new JsonObject
            {
                ["path"] = target.Path,
                ["view"] = (int)request.View,
                ["scope"] = CursorScope(request.Context),
                ["generation"] = request.ExpectedGeneration ?? m_observationGeneration,
                ["offset"] = offset + limit,
                ["limit"] = limit,
                ["expires"] = expires.ToString("O", CultureInfo.InvariantCulture),
                ["parameters"] = parameters
            };
            byte[] payload = Encoding.UTF8.GetBytes(cursor.ToJsonString());
            using var hmac = new HMACSHA256(m_cursorKey);
            string token = UrlBase64(payload) + "." + UrlBase64(hmac.ComputeHash(payload));
            if (token.Length > 65536)
            {
                throw new XRegistryRejectionException(
                    "too_large", "The query cannot fit in a bounded continuation.", 413);
            }
            return new XRegistryResponse(200)
            {
                Metadata = Element(page),
                Expires = expires,
                Links =
                    [new XRegistryLink("next", target.Path + "?cursor=" + token) { Count = (ulong)collection.Count }]
            };
        }

        private static string CursorScope(XRegistryCallContext context)
        {
            var scope = new JsonObject
            {
                ["authority"] = context.Authority,
                ["subject"] = context.Subject,
                ["authenticated"] = context.IsAuthenticated,
                ["session"] = context.SessionId,
                ["roles"] = new JsonArray(context.Roles.ToList().OrderBy(role => role, StringComparer.Ordinal)
                    .Select(role => (JsonNode?)JsonValue.Create(role)).ToArray())
            };
            byte[] value = Encoding.UTF8.GetBytes(scope.ToJsonString());
#if NET5_0_OR_GREATER
            return UrlBase64(SHA256.HashData(value));
#else
            using var hash = SHA256.Create();
            return UrlBase64(hash.ComputeHash(value));
#endif
        }

        private static string UrlBase64(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromUrlBase64(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        }

        private static byte[] NewCursorKey()
        {
            var bytes = new byte[32];
            using RandomNumberGenerator random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return bytes;
        }

        private readonly byte[] m_cursorKey = NewCursorKey();
    }
}
