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

using System;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Wrapper carrying a node-manager factory registered under the
    /// "regular" OPC UA server feature
    /// (<see cref="IOpcUaServerBuilder.AddNodeManager{TFactory}"/> /
    /// <see cref="IOpcUaServerBuilder.AddSyncNodeManager{TFactory}"/>).
    /// </summary>
    /// <remarks>
    /// The hosted service resolves <c>IEnumerable&lt;OpcUaServerNodeManagerRegistration&gt;</c>
    /// — never the bare <see cref="IAsyncNodeManagerFactory"/> /
    /// <see cref="INodeManagerFactory"/> services — so node managers
    /// registered under the regular server feature do not bleed into
    /// other server features (GDS, LDS, WotCon) hosted in the same
    /// container.
    /// </remarks>
    public sealed class OpcUaServerNodeManagerRegistration
    {
        /// <summary>
        /// Creates a registration carrying an asynchronous factory.
        /// </summary>
        public OpcUaServerNodeManagerRegistration(IAsyncNodeManagerFactory factory)
        {
            AsyncFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Creates a registration carrying a synchronous factory.
        /// </summary>
        public OpcUaServerNodeManagerRegistration(INodeManagerFactory factory)
        {
            SyncFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// The asynchronous factory, if this is an async registration;
        /// otherwise <c>null</c>.
        /// </summary>
        public IAsyncNodeManagerFactory? AsyncFactory { get; }

        /// <summary>
        /// The synchronous factory, if this is a sync registration;
        /// otherwise <c>null</c>.
        /// </summary>
        public INodeManagerFactory? SyncFactory { get; }
    }
}
