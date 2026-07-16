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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Selects which OPC UA JSON encoding flavour (Part 6 §5.4) the OPC UA REST
    /// binding (Part 6 §G.3 "OpenAPI Mapping") uses to (de)serialize a service
    /// request or response.
    /// </summary>
    public enum WebApiEncoding
    {
        /// <summary>
        /// Compact JSON encoding (Part 6 §5.4.9). Default flavour for the REST
        /// binding when the client does not specify an
        /// <see cref="WebApiMediaType.EncodingParameter"/> on
        /// <c>Accept</c> / <c>Content-Type</c>. Mandatory per the spec.
        /// </summary>
        Compact = 0,

        /// <summary>
        /// Verbose JSON encoding (Part 6 §5.4). Opt-in via
        /// <c>application/json; encoding=verbose</c>; preserves default values,
        /// emits enumerations as their symbolic names, and keeps
        /// union-switch / optional-field-encoding-mask fields.
        /// </summary>
        Verbose
    }

    /// <summary>
    /// Media-type identifiers and content-negotiation helpers for the OPC UA
    /// REST binding defined by OPC UA Part 6 §G.3 "OpenAPI Mapping" (v1.05.07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The REST binding uses the standard <c>application/json</c> media type
    /// (not the <c>application/opcua+uajson</c> type used by the envelope-based
    /// HTTPS-JSON sub-profile in Part 6 §7.4.5) and conveys the Part 6 §5.4
    /// encoding flavour (Compact or Verbose) through the
    /// <see cref="EncodingParameter"/> media-type parameter on the
    /// <c>Accept</c> and <c>Content-Type</c> headers.
    /// </para>
    /// <para>
    /// Example: <c>Content-Type: application/json; encoding=verbose</c>.
    /// </para>
    /// </remarks>
    public static class WebApiMediaType
    {
        /// <summary>
        /// HTTP <c>Content-Type</c> base value identifying an OPC UA REST
        /// request / response body. The OPC UA JSON encoding flavour is
        /// carried by the optional <see cref="EncodingParameter"/>
        /// media-type parameter; an unparameterized
        /// <c>application/json</c> is treated as
        /// <see cref="WebApiEncoding.Compact"/>.
        /// </summary>
        public const string ContentType = "application/json";

        /// <summary>
        /// Media-type parameter name (<c>encoding</c>) used to select the
        /// OPC UA JSON encoding flavour on <c>Accept</c> and
        /// <c>Content-Type</c>.
        /// </summary>
        public const string EncodingParameter = "encoding";

        /// <summary>
        /// Parameter value selecting the Compact JSON encoding
        /// (<see cref="WebApiEncoding.Compact"/>).
        /// </summary>
        public const string EncodingCompact = "compact";

        /// <summary>
        /// Parameter value selecting the Verbose JSON encoding
        /// (<see cref="WebApiEncoding.Verbose"/>).
        /// </summary>
        public const string EncodingVerbose = "verbose";

        /// <summary>
        /// Default encoding when the client does not specify an
        /// <see cref="EncodingParameter"/>. Mandated by Part 6 §5.4.9 to be
        /// <see cref="WebApiEncoding.Compact"/>.
        /// </summary>
        public const WebApiEncoding DefaultEncoding = WebApiEncoding.Compact;

        /// <summary>
        /// Parses a single media-type header value (e.g.
        /// <c>application/json; encoding=verbose</c>) and returns the selected
        /// OPC UA JSON encoding flavour. Returns <paramref name="fallback"/>
        /// when the header is <c>null</c> / empty / does not advertise
        /// <see cref="ContentType"/>, or when no
        /// <see cref="EncodingParameter"/> is present.
        /// </summary>
        /// <param name="headerValue">
        /// A single <c>Content-Type</c> or <c>Accept</c> header value.
        /// </param>
        /// <param name="fallback">
        /// Encoding to return when no preference is expressed. Defaults to
        /// <see cref="DefaultEncoding"/>.
        /// </param>
        /// <returns>The selected encoding flavour.</returns>
        public static WebApiEncoding ParseEncoding(
            string? headerValue,
            WebApiEncoding fallback = DefaultEncoding)
        {
            return TryParseEncoding(headerValue, out WebApiEncoding encoding)
                ? encoding
                : fallback;
        }

        /// <summary>
        /// Attempts to parse a media-type header value and extract the OPC UA
        /// JSON encoding flavour. Returns <c>false</c> when the header is
        /// missing, does not advertise <see cref="ContentType"/>, or carries
        /// no <see cref="EncodingParameter"/>; returns <c>false</c> with
        /// <paramref name="encoding"/> set to
        /// <see cref="WebApiEncoding.Compact"/> when the parameter is
        /// present but its value is unknown (Part 6 §5.4.9 requires the
        /// server to fall back to Compact rather than fault).
        /// </summary>
        /// <param name="headerValue">
        /// A single <c>Content-Type</c> or <c>Accept</c> header value.
        /// </param>
        /// <param name="encoding">
        /// On success, the parsed encoding flavour. On failure (no match), the
        /// safe default (<see cref="WebApiEncoding.Compact"/>).
        /// </param>
        /// <returns>
        /// <c>true</c> when an explicit, recognized encoding parameter was
        /// found; otherwise <c>false</c>.
        /// </returns>
        public static bool TryParseEncoding(string? headerValue, out WebApiEncoding encoding)
        {
            encoding = DefaultEncoding;
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return false;
            }

            ReadOnlySpan<char> remaining = headerValue.AsSpan();
            ReadOnlySpan<char> mediaType = ConsumeSegment(ref remaining, ';');
            if (!mediaType.Trim().Equals(ContentType.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            while (!remaining.IsEmpty)
            {
                ReadOnlySpan<char> parameter = ConsumeSegment(ref remaining, ';').Trim();
                if (parameter.IsEmpty)
                {
                    continue;
                }

                int equals = parameter.IndexOf('=');
                if (equals < 0)
                {
                    continue;
                }

                ReadOnlySpan<char> name = parameter[..equals].Trim();
                if (!name.Equals(EncodingParameter.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ReadOnlySpan<char> value = TrimQuotes(parameter[(equals + 1)..].Trim());

                if (value.Equals(EncodingCompact.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    encoding = WebApiEncoding.Compact;
                    return true;
                }
                if (value.Equals(EncodingVerbose.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    encoding = WebApiEncoding.Verbose;
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Formats a Content-Type header value advertising
        /// <see cref="ContentType"/> parameterized with the supplied
        /// <paramref name="encoding"/>.
        /// </summary>
        /// <param name="encoding">The encoding flavour to advertise.</param>
        /// <returns>e.g. <c>application/json; encoding=compact</c>.</returns>
        public static string FormatContentType(WebApiEncoding encoding)
        {
            return encoding switch
            {
                WebApiEncoding.Verbose => ContentType + "; " + EncodingParameter + "=" + EncodingVerbose,
                _ => ContentType + "; " + EncodingParameter + "=" + EncodingCompact
            };
        }

        /// <summary>
        /// Maps a <see cref="WebApiEncoding"/> to the matching
        /// <see cref="JsonEncoderOptions"/> profile defined by Part 6 §5.4.
        /// </summary>
        /// <param name="encoding">The encoding flavour.</param>
        /// <returns>
        /// <see cref="JsonEncoderOptions.Compact"/> for
        /// <see cref="WebApiEncoding.Compact"/>;
        /// <see cref="JsonEncoderOptions.Verbose"/> for
        /// <see cref="WebApiEncoding.Verbose"/>.
        /// </returns>
        public static JsonEncoderOptions ToEncoderOptions(WebApiEncoding encoding)
        {
            return encoding switch
            {
                WebApiEncoding.Verbose => JsonEncoderOptions.Verbose,
                _ => JsonEncoderOptions.Compact
            };
        }

        private static ReadOnlySpan<char> ConsumeSegment(
            ref ReadOnlySpan<char> remaining,
            char separator)
        {
            int index = remaining.IndexOf(separator);
            if (index < 0)
            {
                ReadOnlySpan<char> all = remaining;
                remaining = default;
                return all;
            }

            ReadOnlySpan<char> segment = remaining[..index];
            remaining = remaining[(index + 1)..];
            return segment;
        }

        private static ReadOnlySpan<char> TrimQuotes(ReadOnlySpan<char> value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1];
            }
            return value;
        }
    }
}
