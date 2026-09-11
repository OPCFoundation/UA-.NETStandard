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
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal sealed class XRegistryHttpWire
    {
        public XRegistryHttpWire(XRegistryHttpAddress address, XRegistryHttpOptions options)
        {
            Address = address;
            Options = options;
            Body = new XRegistryHttpBody(options);
        }

        public XRegistryHttpAddress Address { get; }

        public XRegistryHttpOptions Options { get; }

        public XRegistryHttpBody Body { get; }

        public HttpRequestMessage EncodeRequest(XRegistryRequest request, XRegistryHttpShape shape)
        {
            ValidateRequest(request, shape);
            XRegistryView view = shape.IsResource && request.View == XRegistryView.Metadata
                ? XRegistryView.Metadata : XRegistryView.Default;
            var message = new HttpRequestMessage(new HttpMethod(Method(request.Action)),
                Address.GetUri(request.Path, view, request.Parameters));
            try
            {
                message.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
                bool document = shape.IsDocumentView(request);
                ByteString body = document ? request.Document : Body.Encode(request.Metadata);
                if (!body.IsNull || (document && request.IsMutation))
                {
                    Body.CheckLength(body.Length);
                    message.Content = new ByteArrayContent(body.Span.ToArray());
                    string? contentType = document ? request.ContentType : "application/json; charset=utf-8";
                    XRegistryHttpHeaders.ValidateContentType(contentType);
                    if (contentType is not null)
                    {
                        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    }
                }
                if (document)
                {
                    foreach (KeyValuePair<string, string> header in XRegistryHttpHeaders.Encode(
                        request.Metadata, shape, request: true))
                    {
                        message.Headers.Add(header.Key, header.Value);
                    }
                }
                XRegistryHttpHeaders.ValidateBudget(GetHeaders(message.Headers, message.Content?.Headers), Options);
                return message;
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }

        public XRegistryRequest DecodeRequest(
            XRegistryRequest request,
            XRegistryHttpShape shape,
            ArrayOf<KeyValuePair<string, string>> headers,
            string? contentType,
            ByteString body)
        {
            XRegistryHttpHeaders.ValidateBudget(headers, Options);
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (IsUnsupportedValidator(header.Key))
                {
                    throw new XRegistryHttpWireException(400, "about:blank",
                        "HTTP validator preconditions are not supported; use xRegistry epoch preconditions.");
                }
            }
            if (request.Action is XRegistryAction.Read or XRegistryAction.Describe)
            {
                if (!body.IsEmpty)
                {
                    throw new XRegistryHttpWireException(400, "about:blank", "GET and OPTIONS cannot carry bodies.");
                }
                return request;
            }
            bool document = shape.IsDocumentView(request);
            JsonElement metadata = default;
            if (document)
            {
                metadata = XRegistryHttpHeaders.Decode(headers, contentType, shape, request: true, Body);
            }
            else
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (header.Key.StartsWith("xRegistry-", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new XRegistryHttpWireException(400, "extra_xregistry_header",
                            "Metadata-body requests cannot include xRegistry attribute headers.");
                    }
                }
                if (!body.IsEmpty)
                {
                    ValidateJsonContentType(contentType);
                    metadata = Body.Parse(body);
                }
            }
            bool reference = document && HasExternalDocument(metadata, shape);
            XRegistryRequest decoded = request with
            {
                Metadata = metadata,
                Document = document && !reference ? body : default,
                ContentType = document ? contentType : null
            };
            if (reference && !body.IsEmpty)
            {
                throw new XRegistryHttpWireException(400, "invalid_attribute",
                    "An external document reference requires an empty HTTP body.");
            }
            ValidateRequest(decoded, shape);
            return decoded;
        }

        public async ValueTask<XRegistryResponse> DecodeResponseAsync(
            HttpResponseMessage message,
            XRegistryRequest request,
            XRegistryHttpShape shape,
            JsonElement model,
            Uri sentUri,
            CancellationToken cancellationToken)
        {
            if (message.RequestMessage?.RequestUri is Uri finalUri && finalUri != sentUri)
            {
                throw new InvalidDataException(
                    "The injected HTTP client followed a redirect. Disable automatic redirects and retries.");
            }
            ArrayOf<KeyValuePair<string, string>> headers = GetHeaders(message.Headers, message.Content?.Headers);
            XRegistryHttpHeaders.ValidateBudget(headers, Options);
            string? location = GetSingle(headers, "Location");
            string? contentLocation = GetSingle(headers, "Content-Location");
            string? correlation = GetSingle(headers, "xRegistry-xregcorrelationid");
            string? contentType = GetSingle(headers, "Content-Type");
            if (location is not null)
            {
                location = Address.ReadLink(location, sentUri);
            }
            if (contentLocation is not null)
            {
                contentLocation = Address.ReadLink(contentLocation, sentUri);
            }
            ByteString bytes = ByteString.Empty;
            if (message.Content is not null)
            {
#if NET8_0_OR_GREATER
                using Stream stream = await message.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                using Stream stream = await message.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                bytes = await Body.ReadAsync(stream, message.Content.Headers.ContentLength,
                    GetEncodings(headers), cancellationToken).ConfigureAwait(false);
            }
            int status = (int)message.StatusCode;
            if ((status is 204 or 304 || (status == 303 && shape.IsDocumentView(request))) && !bytes.IsEmpty)
            {
                throw new InvalidDataException("The backend returned a body for a bodyless HTTP response.");
            }
            JsonElement metadata = default;
            ByteString document = default;
            XRegistryError? error = null;
            if (status >= 400)
            {
                if (!bytes.IsEmpty)
                {
                    try
                    {
                        metadata = Body.Parse(bytes);
                        error = ParseError(metadata);
                    }
                    catch (JsonException)
                    {
                        // Non-xRegistry gateways can return HTML or opaque errors. Retain them verbatim.
                        document = bytes;
                    }
                }
            }
            else if (shape.IsDocumentView(request))
            {
                metadata = XRegistryHttpHeaders.Decode(headers, contentType, shape, request: false, Body);
                metadata = XRegistryHttpLinks.Translate(metadata, request, model, Address, Body, sentUri);
                if (status is >= 200 and < 300 and not 204)
                {
                    document = bytes;
                }
                else if (!bytes.IsEmpty)
                {
                    document = bytes;
                }
            }
            else if (!bytes.IsEmpty)
            {
                metadata = Body.Parse(bytes);
                metadata = XRegistryHttpLinks.Translate(metadata, request, model, Address, Body, sentUri);
            }
            else if (status is >= 200 and < 300 &&
                status != 204 &&
                request.Action is not (XRegistryAction.Describe or XRegistryAction.Delete))
            {
                throw new InvalidDataException("A metadata response is missing its JSON body.");
            }
            return new XRegistryResponse(status)
            {
                Metadata = metadata,
                Document = document,
                ContentType = contentType,
                Location = location,
                ContentLocation = contentLocation,
                CorrelationId = correlation is null ? null : XRegistryHttpHeaders.DecodeValue(correlation),
                Links = ReadLinks(headers, sentUri),
                AllowedActions = ReadAllowedActions(headers),
                Error = error
            };
        }

        public XRegistryHttpPreparedResponse PrepareResponse(
            XRegistryResponse response,
            XRegistryRequest request,
            XRegistryHttpShape shape,
            JsonElement model)
        {
            var headers = new List<KeyValuePair<string, string>>();
            bool documentView = shape.IsDocumentView(request);
            if (response.IsSuccess && response.Error is null)
            {
                if ((!documentView && !response.Document.IsNull) ||
                    (documentView && response.StatusCode != 204 && response.Document.IsNull))
                {
                    throw new InvalidDataException("The endpoint response does not match the model-defined HTTP view.");
                }
            }
            if (documentView && response.StatusCode == 303 && !response.Document.IsEmpty)
            {
                throw new InvalidDataException("A document redirect must have an empty HTTP body.");
            }
            ByteString body;
            string? contentType;
            if (response.Error is not null)
            {
                JsonElement metadata = response.Metadata.ValueKind == JsonValueKind.Undefined
                    ? CreateError(response.Error) : response.Metadata;
                body = Body.Encode(metadata);
                contentType = "application/json; charset=utf-8";
            }
            else if (!response.Document.IsNull)
            {
                body = response.Document;
                contentType = response.ContentType;
                if (response.StatusCode < 400)
                {
                    JsonElement metadata = XRegistryHttpLinks.Translate(
                        response.Metadata, request, model, Address, Body);
                    headers.AddRange(XRegistryHttpHeaders.Encode(metadata, shape, request: false));
                }
            }
            else if (documentView && (response.StatusCode == 204 || response.StatusCode is >= 300 and < 400))
            {
                body = default;
                contentType = response.ContentType;
                JsonElement metadata = XRegistryHttpLinks.Translate(response.Metadata, request, model, Address, Body);
                headers.AddRange(XRegistryHttpHeaders.Encode(metadata, shape, request: false));
            }
            else
            {
                JsonElement metadata = XRegistryHttpLinks.Translate(response.Metadata, request, model, Address, Body);
                body = Body.Encode(metadata);
                contentType = body.IsNull ? null : "application/json; charset=utf-8";
            }
            Body.CheckLength(body.Length);
            if (response.StatusCode is 204 or 304 or < 200)
            {
                if (!body.IsNull && !body.IsEmpty)
                {
                    throw new InvalidDataException("The endpoint returned a body for a bodyless HTTP status.");
                }
                body = default;
                contentType = null;
            }
            XRegistryHttpHeaders.ValidateContentType(contentType);
            AddHeader(headers, "Content-Type", contentType);
            AddHeader(headers, "Location", response.Location is null ? null : Address.WriteLink(response.Location));
            AddHeader(headers, "Content-Location",
                response.ContentLocation is null ? null : Address.WriteLink(response.ContentLocation));
            if (response.CorrelationId is not null)
            {
                headers.RemoveAll(static header =>
                    header.Key.Equals("xRegistry-xregcorrelationid", StringComparison.OrdinalIgnoreCase));
                AddHeader(headers, "xRegistry-xregcorrelationid",
                    XRegistryHttpHeaders.EncodeValue(response.CorrelationId));
            }
            headers.Add(new KeyValuePair<string, string>("Link",
                "<" + Address.Root.AbsoluteUri + ">;rel=xregistry-root"));
            foreach (XRegistryLink link in response.Links)
            {
                if (link.Relation == "xregistry-root")
                {
                    continue;
                }
                ValidateRelation(link.Relation);
                headers.Add(new KeyValuePair<string, string>(
                    "Link", "<" + Address.WriteLink(link.Target) + ">;rel=\"" + link.Relation + "\""));
            }
            if (response.AllowedActions.Count != 0 ||
                request.Action == XRegistryAction.Describe ||
                response.StatusCode == 405)
            {
                string allow = FormatAllowedActions(response.AllowedActions);
                headers.Add(new KeyValuePair<string, string>("Allow", allow));
                if (request.Action == XRegistryAction.Describe && response.IsSuccess)
                {
                    headers.Add(new KeyValuePair<string, string>("Access-Control-Allow-Methods", allow));
                }
            }
            ArrayOf<KeyValuePair<string, string>> result = [.. headers];
            XRegistryHttpHeaders.ValidateBudget(result, Options);
            return new XRegistryHttpPreparedResponse(response.StatusCode, body, result);
        }

        public static string Method(XRegistryAction action)
        {
            return action switch
            {
                XRegistryAction.Read => "GET",
                XRegistryAction.Replace => "PUT",
                XRegistryAction.Merge => "PATCH",
                XRegistryAction.Create => "POST",
                XRegistryAction.Delete => "DELETE",
                XRegistryAction.Describe => "OPTIONS",
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        public static bool TryAction(string method, out XRegistryAction action)
        {
            action = method switch
            {
                "GET" or "HEAD" => XRegistryAction.Read,
                "PUT" => XRegistryAction.Replace,
                "PATCH" => XRegistryAction.Merge,
                "POST" => XRegistryAction.Create,
                "DELETE" => XRegistryAction.Delete,
                "OPTIONS" => XRegistryAction.Describe,
                _ => (XRegistryAction)(-1)
            };
            return (int)action >= 0;
        }

        public static bool AllowsMutation(XRegistryEndpointDescription description, XRegistryAction action)
        {
            return description.SupportsAtomicMutations &&
                description.SupportsConditionalMutations &&
                (action == XRegistryAction.Delete || description.SupportsWriteTouch);
        }

        public static bool IsUnsupportedValidator(string name)
        {
            return name.Equals("If-Match", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("If-Modified-Since", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("If-Unmodified-Since", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("If-Range", StringComparison.OrdinalIgnoreCase);
        }

        public static XRegistryResponse MutationNotSupported(string path)
        {
            return new XRegistryResponse(405)
            {
                Error = new XRegistryError("action_not_supported",
                    "The endpoint is not qualified for this atomic, conditional xRegistry mutation.")
                {
                    Subject =
                        path
                },
                AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
            };
        }

        public static ArrayOf<KeyValuePair<string, string>> GetHeaders(HttpHeaders first, HttpHeaders? second = null)
        {
            var result = new List<KeyValuePair<string, string>>();
            AddHeaders(result, first);
            if (second is not null)
            {
                AddHeaders(result, second);
            }
            return [.. result];
        }

        public static string? GetSingle(ArrayOf<KeyValuePair<string, string>> headers, string name)
        {
            string? result = null;
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (header.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (result is not null)
                    {
                        throw new XRegistryHttpWireException(400, "header_error",
                            "A singleton HTTP header was repeated.");
                    }
                    result = header.Value;
                }
            }
            return result;
        }

        public static ArrayOf<string> GetEncodings(ArrayOf<KeyValuePair<string, string>> headers)
        {
            var encodings = new List<string>();
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    encodings.AddRange(header.Value.Split(','));
                }
            }
            return [.. encodings];
        }

        private void ValidateRequest(XRegistryRequest request, XRegistryHttpShape shape)
        {
            if (request.OperationId is not null)
            {
                throw new XRegistryHttpWireException(405, "action_not_supported",
                    "The HTTP binding does not provide operation replay protection.");
            }
            if (shape.IsCollection && request.Action == XRegistryAction.Replace)
            {
                throw new XRegistryHttpWireException(405, "action_not_supported", "PUT cannot target a collection.");
            }
            bool document = shape.IsDocumentView(request);
            if (document && request.Action == XRegistryAction.Merge)
            {
                throw new XRegistryHttpWireException(405, "details_required",
                    "PATCH of a document-backed resource requires the $details metadata view.");
            }
            if (!document && !request.Document.IsNull)
            {
                throw new XRegistryHttpWireException(400, "about:blank",
                    "This model-defined representation requires a JSON metadata body, not a document.");
            }
            if (document && request.IsMutation)
            {
                ValidateDocumentContentType(request.Metadata, request.ContentType);
                bool reference = HasExternalDocument(request.Metadata, shape);
                if ((request.Document.IsNull && !reference) || (reference && !request.Document.IsEmpty))
                {
                    throw new XRegistryHttpWireException(400, "invalid_attribute",
                        "A document write needs a present document or an external reference, not both.");
                }
            }
            if (!document &&
                request.Action is XRegistryAction.Replace or XRegistryAction.Merge or
                    XRegistryAction.Create &&
                request.Metadata.ValueKind == JsonValueKind.Undefined)
            {
                throw new XRegistryHttpWireException(400, "missing_body",
                    "A metadata write needs a JSON body; use '{}'.");
            }
            if (request.Action is XRegistryAction.Read or XRegistryAction.Describe &&
                (!request.Document.IsNull || request.Metadata.ValueKind != JsonValueKind.Undefined))
            {
                throw new XRegistryHttpWireException(400, "about:blank",
                    "GET and OPTIONS cannot carry registry bodies.");
            }
            Body.CheckLength(request.Document.Length);
        }

        private static void ValidateDocumentContentType(JsonElement metadata, string? contentType)
        {
            if (metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty("contenttype", out JsonElement attribute))
            {
                return;
            }
            if ((attribute.ValueKind == JsonValueKind.Null && contentType is null) ||
                (attribute.ValueKind == JsonValueKind.String && attribute.GetString() == contentType))
            {
                return;
            }
            throw new XRegistryHttpWireException(400, "header_error",
                "Document metadata contenttype must agree with the ContentType property.");
        }

        private ArrayOf<XRegistryLink> ReadLinks(ArrayOf<KeyValuePair<string, string>> headers, Uri sentUri)
        {
            var links = new List<XRegistryLink>();
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (!header.Key.Equals("Link", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string item in SplitLinkValues(header.Value))
                {
                    string value = item.Trim();
                    int close = value.IndexOf('>', StringComparison.Ordinal);
                    if (!value.StartsWith('<') || close < 1)
                    {
                        throw new InvalidDataException("A registry Link header is malformed.");
                    }
                    string target = Address.ReadLink(value[1..close], sentUri);
                    foreach (string parameter in value[(close + 1)..].Split(';'))
                    {
                        string part = parameter.Trim();
                        if (!part.StartsWith("rel=", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string relations = part[4..].Trim('"');
                        foreach (string relation in relations.Split(' '))
                        {
                            ValidateRelation(relation);
                            links.Add(new XRegistryLink(relation, target));
                        }
                    }
                }
            }
            return [.. links];
        }

        private JsonElement CreateError(XRegistryError error)
        {
            bool httpError = error.Code is "api_not_found" or "details_required" or
                "extra_xregistry_header" or "header_error" or "missing_body";
            string type = error.Code.Contains(':', StringComparison.Ordinal) ? error.Code :
                "https://github.com/xregistry/spec/blob/main/core/" +
                (
                    httpError ? "http.md#" : "spec.md#") +
                error.Code;
            var json = new JsonObject
            {
                ["type"] = type,
                ["title"] = error.Code,
                ["detail"] = error.Detail
            };
            if (error.Subject is not null)
            {
                json["subject"] = error.Subject;
            }
            return Body.Parse(Body.Encode(json));
        }

        private static XRegistryError? ParseError(JsonElement metadata)
        {
            if (metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty("type", out JsonElement type) ||
                type.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            string code = type.GetString()!;
            int fragment = code.LastIndexOf('#');
            if (fragment >= 0)
            {
                code = code[(fragment + 1)..];
            }
            string detail = string.Empty;
            if ((metadata.TryGetProperty("detail", out JsonElement text) ||
                metadata.TryGetProperty("title", out text)) &&
                text.ValueKind == JsonValueKind.String)
            {
                detail = text.GetString()!;
            }
            return new XRegistryError(code, detail)
            {
                Subject = metadata.TryGetProperty("subject", out JsonElement subject) &&
                    subject.ValueKind == JsonValueKind.String ? subject.GetString() : null
            };
        }

        private static bool HasExternalDocument(JsonElement metadata, XRegistryHttpShape shape)
        {
            return metadata.ValueKind == JsonValueKind.Object &&
                metadata.TryGetProperty(shape.Singular + "url", out JsonElement reference) &&
                reference.ValueKind != JsonValueKind.Null;
        }

        private static void ValidateJsonContentType(string? contentType)
        {
            if (contentType is null)
            {
                return;
            }
            XRegistryHttpHeaders.ValidateContentType(contentType);
            var parsed = MediaTypeHeaderValue.Parse(contentType);
            if (!(parsed.MediaType!.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                parsed.MediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) ||
                (parsed.CharSet is not null &&
                    !parsed.CharSet.Trim('"').Equals("utf-8",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new XRegistryHttpWireException(415, "about:blank", "Metadata bodies must contain UTF-8 JSON.");
            }
        }

        private static ArrayOf<XRegistryAction> ReadAllowedActions(ArrayOf<KeyValuePair<string, string>> headers)
        {
            var actions = new List<XRegistryAction>();
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (header.Key.Equals("Allow", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string method in header.Value.Split(','))
                    {
                        if (TryAction(method.Trim(), out XRegistryAction action) && !actions.Contains(action))
                        {
                            actions.Add(action);
                        }
                    }
                }
            }
            return [.. actions];
        }

        private static string FormatAllowedActions(ArrayOf<XRegistryAction> actions)
        {
            var methods = new List<string>();
            foreach (XRegistryAction action in actions)
            {
                string method = Method(action);
                if (!methods.Contains(method))
                {
                    methods.Add(method);
                }
                if (action == XRegistryAction.Read && !methods.Contains("HEAD"))
                {
                    methods.Add("HEAD");
                }
            }
            if (!methods.Contains("OPTIONS"))
            {
                methods.Add("OPTIONS");
            }
            return string.Join(", ", methods);
        }

        private static IEnumerable<string> SplitLinkValues(string value)
        {
            int start = 0;
            bool quoted = false;
            bool angle = false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '"' && !angle)
                {
                    quoted = !quoted;
                }
                else if (value[index] == '<' && !quoted)
                {
                    angle = true;
                }
                else if (value[index] == '>' && !quoted)
                {
                    angle = false;
                }
                else if (value[index] == ',' && !quoted && !angle)
                {
                    yield return value[start..index];
                    start = index + 1;
                }
            }
            if (angle || quoted)
            {
                throw new InvalidDataException("A registry Link header is unterminated.");
            }
            yield return value[start..];
        }

        private static void ValidateRelation(string relation)
        {
            if (relation.Length == 0)
            {
                throw new InvalidDataException("A registry link relation is empty.");
            }
            foreach (char character in relation)
            {
                if (character is <= ' ' or >= '\x7f' or '"' or '\\' or '<' or '>')
                {
                    throw new InvalidDataException("A registry link relation contains an invalid character.");
                }
            }
        }

        private static void AddHeader(List<KeyValuePair<string, string>> headers, string name, string? value)
        {
            if (value is not null)
            {
                headers.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        private static void AddHeaders(List<KeyValuePair<string, string>> result, HttpHeaders headers)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                foreach (string value in header.Value)
                {
                    result.Add(new KeyValuePair<string, string>(header.Key, value));
                }
            }
        }
    }

    internal sealed record XRegistryHttpPreparedResponse(
        int StatusCode,
        ByteString Body,
        ArrayOf<KeyValuePair<string, string>> Headers);
}
