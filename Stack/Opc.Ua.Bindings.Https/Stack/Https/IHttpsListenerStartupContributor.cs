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

using Microsoft.AspNetCore.Builder;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Extension hook into <see cref="HttpsTransportListener"/>'s Kestrel
    /// pipeline. Companion bindings (such as the REST binding in
    /// <c>OPCFoundation.NetStandard.Opc.Ua.Bindings.Rest</c>) register a
    /// contributor on <see cref="HttpsServiceHost.StartupContributors"/>
    /// to attach additional middleware (typically routing + MVC
    /// controllers) to the same Kestrel host that already serves the
    /// HTTPS-binary (Part 6 §7.4.4) and HTTPS-JSON (Part 6 §7.4.5)
    /// sub-profiles. Contributor middleware runs BEFORE the terminal
    /// binary / JSON dispatcher, so unmatched requests still fall
    /// through to the existing transport handlers.
    /// </summary>
    public interface IHttpsListenerStartupContributor
    {
        /// <summary>
        /// Invoked by <c>HttpsTransportListener</c>'s <c>Startup.Configure</c>
        /// after <see cref="WebSocketMiddlewareExtensions.UseWebSockets(IApplicationBuilder)"/>
        /// and before the terminal binary / JSON dispatcher
        /// <see cref="Microsoft.AspNetCore.Builder.RunExtensions.Run"/>.
        /// </summary>
        /// <param name="appBuilder">The Kestrel pipeline builder.</param>
        /// <param name="listener">The listener whose pipeline is being
        /// configured. Contributors typically read the listener's
        /// transport callback / message context out of this instance to
        /// wire their request handlers to the host server.</param>
        void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener);
    }
}
