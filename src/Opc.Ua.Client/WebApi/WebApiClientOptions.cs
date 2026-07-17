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
using System.Net.Http;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// Options for <see cref="WebApiClient"/>.
    /// </summary>
    public sealed class WebApiClientOptions
    {
        /// <summary>
        /// The OPC UA JSON encoding flavour to advertise on outbound
        /// requests and use to decode responses. Defaults to
        /// <see cref="WebApiEncoding.Compact"/> per Part 6 §5.4.9.
        /// </summary>
        public WebApiEncoding Encoding { get; set; } = WebApiMediaType.DefaultEncoding;

        /// <summary>
        /// Encoding context (namespace tables, server tables, quotas,
        /// telemetry) used to encode requests and decode responses.
        /// When <c>null</c>, the client constructs an empty default
        /// context the first time it is needed.
        /// </summary>
        public IServiceMessageContext? MessageContext { get; set; }

        /// <summary>
        /// Optional <see cref="HttpMessageHandler"/> used to construct
        /// the underlying <c>HttpClient</c>. Set this to inject a
        /// pre-configured handler (e.g. <c>SocketsHttpHandler</c> with
        /// custom client certificates, mock handler in tests).
        /// </summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        /// <summary>
        /// When <c>true</c>, <see cref="HttpMessageHandler"/> is
        /// disposed when the client is disposed. Defaults to
        /// <c>true</c> only when the client created the handler
        /// internally; ignored when the caller injected
        /// <see cref="HttpMessageHandler"/> directly (caller owns it).
        /// </summary>
        public bool DisposeHandler { get; set; } = true;

        /// <summary>
        /// Bearer token to attach to every outbound request as
        /// <c>Authorization: Bearer &lt;token&gt;</c>. <c>null</c> to
        /// omit the header. Cannot be combined with
        /// <see cref="BasicCredentials"/>.
        /// </summary>
        public string? BearerToken { get; set; }

        /// <summary>
        /// HTTP Basic credentials to attach as
        /// <c>Authorization: Basic base64(user:password)</c>. <c>null</c>
        /// to omit the header. Cannot be combined with
        /// <see cref="BearerToken"/>.
        /// </summary>
        public (string Username, string Password)? BasicCredentials { get; set; }

        /// <summary>
        /// Per-request timeout for the underlying <c>HttpClient</c>.
        /// Defaults to 100 seconds, matching
        /// <c>HttpClient.Timeout</c>. Set to
        /// <c>System.Threading.Timeout.InfiniteTimeSpan</c> for the
        /// long-poll <c>/publish</c> endpoint so the server-side
        /// <see cref="RequestHeader.TimeoutHint"/> governs cancellation
        /// instead of the client timeout.
        /// </summary>
        public TimeSpan? RequestTimeout { get; set; }
    }
}
