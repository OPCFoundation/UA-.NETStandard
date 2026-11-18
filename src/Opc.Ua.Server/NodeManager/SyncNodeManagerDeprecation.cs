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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Messages and the diagnostic id of the <c>[Obsolete]</c> attributes that
    /// deprecate the synchronous node manager API in favour of the asynchronous seam
    /// (<see cref="IAsyncNodeManager"/>, <see cref="IAsyncNodeManagerFactory"/>,
    /// <see cref="AsyncCustomNodeManager"/>).
    /// </summary>
    /// <remarks>
    /// On .NET 5 and later the deprecation is reported as <c>UA0031</c> so that callers can
    /// suppress exactly this deprecation. On .NET Framework the attribute carries no diagnostic
    /// id and the compiler reports <c>CS0618</c>. Code that has to keep using the synchronous
    /// API therefore suppresses both: <c>#pragma warning disable CS0618, UA0031</c>.
    /// </remarks>
    internal static class SyncNodeManagerDeprecation
    {
        /// <summary>
        /// The diagnostic id reported for every member of the synchronous node manager API.
        /// </summary>
        public const string DiagnosticId = "UA0031";

        /// <summary>
        /// The help link for <see cref="DiagnosticId"/>.
        /// </summary>
        public const string UrlFormat =
            "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.1/node-managers.md#ua0031";

        private const string kSee = " See docs/migrate/2.1/node-managers.md.";

        /// <summary>
        /// Message for <see cref="CustomNodeManager2"/>.
        /// </summary>
        public const string CustomNodeManager2 =
            "CustomNodeManager2 is the synchronous node manager base class. Derive from AsyncCustomNodeManager, " +
            "or use FluentNodeManagerBase / the [NodeManager] source generator for new code." + kSee;

        /// <summary>
        /// Message for <see cref="INodeManager"/>, <see cref="INodeManager2"/> and <see cref="INodeManager3"/>.
        /// </summary>
        public const string INodeManager =
            "The synchronous INodeManager interfaces are deprecated. Implement IAsyncNodeManager and the " +
            "per-service capability interfaces (IReadAsyncNodeManager, ...), or derive from AsyncCustomNodeManager." + kSee;

        /// <summary>
        /// Message for <see cref="INodeManagerFactory"/>.
        /// </summary>
        public const string INodeManagerFactory =
            "INodeManagerFactory creates synchronous node managers. Implement IAsyncNodeManagerFactory instead." + kSee;

        /// <summary>
        /// Message for <see cref="AsyncNodeManagerAdapter"/> and
        /// <see cref="AsyncNodeManagerAdapterFactory.ToAsyncNodeManager"/>.
        /// </summary>
        public const string AsyncNodeManagerAdapter =
            "AsyncNodeManagerAdapter wraps a synchronous INodeManager. Implement IAsyncNodeManager directly, " +
            "or derive from AsyncCustomNodeManager." + kSee;

        /// <summary>
        /// Message for members that register or return synchronous node managers on the master node manager.
        /// </summary>
        public const string MasterSyncNodeManager =
            "Synchronous INodeManager registration and lookup are deprecated. Use the IAsyncNodeManager " +
            "overload (AsyncNodeManagers, GetManagerHandleAsync, RegisterNamespaceManager(string, IAsyncNodeManager))." + kSee;

        /// <summary>
        /// Message for members that register synchronous node manager factories.
        /// </summary>
        public const string SyncNodeManagerFactory =
            "Registering an INodeManagerFactory is deprecated. Register an IAsyncNodeManagerFactory instead " +
            "(AddNodeManager(IAsyncNodeManagerFactory), AsyncNodeManagerFactories)." + kSee;
    }
}
