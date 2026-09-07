/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ModelChange
{
    /// <summary>
    /// Provides the client side namespace table together with the
    /// ability to re-read it from the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server may append namespace uris while it is running — for
    /// example when a NodeManager is registered live. It signals this
    /// by updating <c>Server_NamespaceArray</c> and
    /// <c>Server_UrisVersion</c> and reporting an address space model
    /// change. A <see cref="ModelChangeTracker"/> that is given an
    /// implementation of this interface re-reads the namespace table
    /// when it observes such a change, so NodeIds from newly added
    /// namespaces keep resolving.
    /// </para>
    /// <para>
    /// Both <see cref="Session"/> and <see cref="ManagedSession"/>
    /// implement this interface, so either can be handed to the
    /// tracker directly. Supply a custom implementation to control
    /// how and when the table is refreshed.
    /// </para>
    /// </remarks>
    public interface INamespaceTableRefresher
    {
        /// <summary>
        /// The table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Re-reads the server's namespace and server uri tables and
        /// updates <see cref="NamespaceUris"/> in place.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task FetchNamespaceTablesAsync(CancellationToken ct = default);
    }
}
