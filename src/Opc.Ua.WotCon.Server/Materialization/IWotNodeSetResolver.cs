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

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Resolves the NodeSet2 for an OPC UA namespace a Thing Description depends on but the server
    /// does not already know.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thing Descriptions are uploaded at run time through the standard <c>WoTAssetFileType</c>
    /// upload, so the set of namespaces a server will be asked to serve is not known at start-up
    /// and static pre-loading is not sufficient. A resolver closes that gap: it is consulted for
    /// each unknown namespace, and what it returns is loaded through the server's existing runtime
    /// NodeSet support.
    /// </para>
    /// <para>
    /// A document that carries its own model does not need a resolver at all — the
    /// <c>uav:nodeSet</c> envelope embeds the NodeSet2 in the Thing Description itself.
    /// </para>
    /// <para>
    /// Resolution is recursive: a resolved NodeSet's own dependencies are resolved the same way.
    /// A namespace that cannot be resolved is reported as a diagnostic rather than failing the
    /// whole onboarding, so an operator can see exactly what is missing.
    /// </para>
    /// <para>
    /// No implementation ships with the library: resolving a namespace means reaching out to some
    /// catalogue — a UA Cloud Library instance, a corporate model repository, a folder on disk —
    /// which is a deployment decision. Registering none leaves behaviour unchanged.
    /// Implementations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface IWotNodeSetResolver
    {
        /// <summary>
        /// Attempts to resolve the NodeSet2 for <paramref name="namespaceUri"/>.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// A readable stream positioned at the start of the NodeSet2 XML, which the caller
        /// disposes; or <c>null</c> when this resolver does not know the namespace. Returning
        /// <c>null</c> is the expected way to decline — it is not an error.
        /// </returns>
        ValueTask<Stream?> TryResolveAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default);
    }
}
