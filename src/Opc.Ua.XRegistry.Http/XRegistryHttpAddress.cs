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
using System.Net;
using System.Text;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal sealed class XRegistryHttpAddress
    {
        public XRegistryHttpAddress(Uri root, XRegistryHttpOptions options)
        {
            root.ThrowIfNull(nameof(root));
            options.ThrowIfNull(nameof(options));
            options.Validate();
            if (!root.IsAbsoluteUri ||
                root.UserInfo.Length != 0 ||
                root.Query.Length != 0 ||
                root.Fragment.Length != 0 ||
                (root.Scheme != Uri.UriSchemeHttps &&
                    !(options.AllowLoopbackHttp && root.Scheme == Uri.UriSchemeHttp && IsLoopback(root))))
            {
                throw new ArgumentException(
                    "A registry root requires HTTPS, no credentials, query or fragment. " +
                    "Explicit loopback HTTP is the only exception.", nameof(root));
            }
            ValidateRawAddress(root.OriginalString);
            string path = XRegistryPath.Normalize(root.AbsolutePath);
            m_prefix = path == "/" ? string.Empty : path;
            Root = new Uri(root.GetLeftPart(UriPartial.Authority) + m_prefix + "/", UriKind.Absolute);
            m_options = options;
            CheckLength(Root.AbsoluteUri);
        }

        public Uri Root { get; }

        public Uri GetUri(string path, XRegistryView view, ArrayOf<XRegistryParameter> parameters)
        {
            string target = XRegistryPath.Normalize(path);
            if (view == XRegistryView.Metadata)
            {
                target += "$details";
            }
            string query = EncodeQuery(parameters);
            string absolute = new StringBuilder(Root.AbsoluteUri).Append(target, 1, target.Length - 1)
                .Append(query).ToString();
            CheckLength(absolute);
            return new Uri(absolute, UriKind.Absolute);
        }

        public string ReadLink(string target, Uri requestUri)
        {
            target.ThrowIfNull(nameof(target));
            requestUri.ThrowIfNull(nameof(requestUri));
            CheckLength(target);
            ValidateRawAddress(target);
            if (!Uri.TryCreate(target, target.StartsWith('/') ? UriKind.Relative : UriKind.RelativeOrAbsolute,
                    out Uri? reference) ||
                !Uri.TryCreate(requestUri, reference, out Uri? link) ||
                link.UserInfo.Length != 0 ||
                link.Fragment.Length != 0 ||
                !string.Equals(link.Scheme, Root.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(link.IdnHost, Root.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                link.Port != Root.Port)
            {
                throw new InvalidDataException("An HTTP registry link leaves the configured origin.");
            }

            (string path, bool details) = SplitDetails(link.AbsolutePath);
            path = XRegistryPath.Normalize(path);
            if (m_prefix.Length != 0)
            {
                if (path == m_prefix)
                {
                    path = "/";
                }
                else if (path.StartsWith(m_prefix + "/", StringComparison.Ordinal))
                {
                    path = path[m_prefix.Length..];
                }
                else
                {
                    throw new InvalidDataException("An HTTP registry link leaves the configured registry root.");
                }
            }
            return path + (details ? "$details" : string.Empty) + EncodeQuery(ParseQuery(link.Query));
        }

        public string WriteLink(string target, bool details = false)
        {
            target.ThrowIfNull(nameof(target));
            if (Uri.TryCreate(target, UriKind.Absolute, out Uri? absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
            {
                target = ReadLink(target, Root);
            }
            ValidateRawAddress(target);
            int queryOffset = target.IndexOf('?', StringComparison.Ordinal);
            string query = queryOffset < 0 ? string.Empty : target[queryOffset..];
            string path = queryOffset < 0 ? target : target[..queryOffset];
            (path, bool hadDetails) = SplitDetails(path);
            return GetUri(path, details || hadDetails ? XRegistryView.Metadata : XRegistryView.Default,
                ParseQuery(query)).AbsoluteUri;
        }

        public ArrayOf<XRegistryParameter> ParseQuery(string query)
        {
            query.ThrowIfNull(nameof(query));
            CheckLength(query);
            if (query.Length == 0 || query == "?")
            {
                return [];
            }
            if (query[0] == '?')
            {
                query = query[1..];
            }
            var result = new List<XRegistryParameter>();
            foreach (string item in query.Split('&'))
            {
                if (result.Count >= m_options.MaximumParameters)
                {
                    throw new InvalidDataException("The query contains too many parameters.");
                }
                int offset = item.IndexOf('=', StringComparison.Ordinal);
                string name = DecodeQueryPart(offset < 0 ? item : item[..offset]);
                if (name.Length == 0)
                {
                    throw new InvalidDataException("A query parameter must have a name.");
                }
                string? value = offset < 0 ? null : DecodeQueryPart(item[(offset + 1)..]);
                result.Add(new XRegistryParameter(name, value));
            }
            return [.. result];
        }

        public static (string Path, bool Details) SplitDetails(string path)
        {
            const string suffix = "$details";
            return path.EndsWith(suffix, StringComparison.Ordinal)
                ? (path[..^suffix.Length], true)
                : (path, false);
        }

        public static string DecodePercent(string text)
        {
            var bytes = new List<byte>(text.Length);
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (value == '%')
                {
                    if (index + 2 >= text.Length ||
                        !TryHex(text[index + 1], out int high) ||
                        !TryHex(text[index + 2], out int low))
                    {
                        throw new InvalidDataException("A value contains a malformed percent escape.");
                    }
                    bytes.Add((byte)((high << 4) | low));
                    index += 2;
                }
                else if (value <= 0x7f)
                {
                    bytes.Add((byte)value);
                }
                else
                {
                    throw new InvalidDataException("Non-ASCII wire values must use UTF-8 percent encoding.");
                }
            }
            try
            {
                return s_utf8.GetString([.. bytes]);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("A percent escape contains invalid UTF-8.", exception);
            }
        }

        private string EncodeQuery(ArrayOf<XRegistryParameter> parameters)
        {
            if (parameters.Count > m_options.MaximumParameters)
            {
                throw new InvalidDataException("The query contains too many parameters.");
            }
            var result = new StringBuilder();
            for (int index = 0; index < parameters.Count; index++)
            {
                XRegistryParameter parameter = parameters[index];
                if (parameter is null || string.IsNullOrEmpty(parameter.Name))
                {
                    throw new ArgumentException("Every query parameter requires a name.", nameof(parameters));
                }
                result.Append(index == 0 ? '?' : '&')
                    .Append(Uri.EscapeDataString(parameter.Name));
                if (parameter.Value is not null)
                {
                    result.Append('=')
                        .Append(Uri.EscapeDataString(parameter.Value));
                }
                CheckLength(result.Length);
            }
            return result.ToString();
        }

        private void CheckLength(string value)
        {
            CheckLength(value.Length);
        }

        private void CheckLength(int length)
        {
            if (length > m_options.MaximumUriLength)
            {
                throw new InvalidDataException("An HTTP registry URI exceeds its configured limit.");
            }
        }

        private static string DecodeQueryPart(string value)
        {
            return DecodePercent(value.Replace('+', ' '));
        }

        private static bool IsLoopback(Uri uri)
        {
            return uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out IPAddress? address) &&
                    IPAddress.IsLoopback(address));
        }

        private static void ValidateRawAddress(string value)
        {
            if (value.Contains('\\', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
            {
                throw new InvalidDataException("Registry addresses cannot contain backslashes or fragments.");
            }
            int start = 0;
            int scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                start = value.IndexOf('/', scheme + 3);
                if (start < 0)
                {
                    return;
                }
            }
            int query = value.IndexOf('?', start);
            string path = query < 0 ? value[start..] : value[start..query];
            if (path.Length != 0)
            {
                (path, _) = SplitDetails(path);
                _ = XRegistryPath.Normalize(path[0] == '/' ? path : "/" + path);
            }
        }

        private static bool TryHex(char value, out int digit)
        {
            digit = value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'a' and <= 'f' => value - 'a' + 10,
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1
            };
            return digit >= 0;
        }

        private readonly string m_prefix;
        private readonly XRegistryHttpOptions m_options;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
