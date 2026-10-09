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

using System.Net;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A trusted host's classification result. Weights and limits are resolved from policy, not this result.
    /// </summary>
    public readonly record struct ResourceIsolationIdentity(string Key, ResourceIsolationClass Class);

    /// <summary>
    /// Explicit trust boundary for deployment-specific tenant and ingress mappings.
    /// </summary>
    /// <remarks>
    /// Implementations must use an authenticated ingress boundary or verified channel/session
    /// evidence, never request names, source-address history or unvalidated certificates.
    /// Returning false uses ordinary best-effort shared classification.
    /// </remarks>
    public interface IResourceIsolationClassifier
    {
        /// <summary>
        /// Classifies an observed endpoint using an explicitly trusted ingress mapping.
        /// This is the only way to protect a caller before protocol authentication.
        /// </summary>
        bool TryClassifyIngress(IPEndPoint? remoteEndpoint, out ResourceIsolationIdentity identity);

        /// <summary>
        /// Maps a transport-established channel and optional live verified session to an owner.
        /// None-policy certificates are removed before this method is called.
        /// A successful mapping is explicitly trusted host policy, not session authorization.
        /// </summary>
        bool TryClassify(
            SecureChannelContext channelContext,
            SessionBindingContext? sessionBinding,
            out ResourceIsolationIdentity identity);
    }
}
