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

#if NET8_0_OR_GREATER
namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Hosting modes selectable by callers of
    /// <c>AddWebApiTransport()</c>. Controls where the REST controllers
    /// run.
    /// </summary>
    public enum WebApiHostingMode
    {
        /// <summary>
        /// Mount the REST controllers into the existing
        /// <c>HttpsTransportListener</c> Kestrel pipeline via an internal
        /// startup-contributor hook. This is the default: a single Kestrel
        /// port serves binary, <c>opcua+uajson</c>, and REST requests
        /// (content-type-negotiated).
        /// </summary>
        SharedWithHttpsListener = 0,

        /// <summary>
        /// Run the REST controllers on a dedicated
        /// <c>WebApiTransportListener</c> with its own Kestrel host. The
        /// listener may still be co-located on a shared port through the
        /// <c>SharedKestrelHostRegistry</c>.
        /// </summary>
        OwnListener
    }

    /// <summary>
    /// Options for the REST binding configured through
    /// <c>AddWebApiTransport(Action&lt;WebApiTransportOptions&gt;)</c>.
    /// </summary>
    public sealed class WebApiTransportOptions
    {
        /// <summary>
        /// The hosting mode. Defaults to
        /// <see cref="WebApiHostingMode.SharedWithHttpsListener"/>.
        /// </summary>
        public WebApiHostingMode HostingMode { get; set; }
            = WebApiHostingMode.SharedWithHttpsListener;

        /// <summary>
        /// Default OPC UA JSON encoding flavour the server uses when the
        /// client does not advertise a preference through the
        /// <c>application/json; encoding=*</c> media-type parameter.
        /// Defaults to <see cref="WebApiEncoding.Compact"/>, which is
        /// mandatory per Part 6 §5.4.9.
        /// </summary>
        public WebApiEncoding DefaultEncoding { get; set; }
            = WebApiMediaType.DefaultEncoding;
    }
}
#endif
