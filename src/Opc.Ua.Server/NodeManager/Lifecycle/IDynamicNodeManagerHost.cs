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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    internal interface IDynamicNodeManagerHost
    {
        ValueTask<PreparedNodeManager> PrepareAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        ValueTask PublishAsync(
            PreparedNodeManager prepared,
            CancellationToken ct = default);

        ValueTask ReplaceAsync(
            IAsyncNodeManager current,
            PreparedNodeManager replacement,
            CancellationToken ct = default);

        ValueTask CommitAsync(
            PreparedNodeManager prepared,
            Func<ValueTask>? beforeCommit = null,
            CancellationToken ct = default);

        ValueTask UnpublishAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        ValueTask DestroyAsync(
            IAsyncNodeManager nodeManager,
            bool removeExternalReferences,
            CancellationToken ct = default);

        ValueTask RollbackAsync(
            PreparedNodeManager prepared,
            CancellationToken ct = default);

        void Release(IAsyncNodeManager nodeManager);
    }

    internal sealed class PreparedNodeManager
    {
        public PreparedNodeManager(
            IAsyncNodeManager nodeManager,
            Dictionary<NodeId, IList<IReference>> externalReferences)
        {
            NodeManager = nodeManager;
            ExternalReferences = externalReferences;
        }

        public IAsyncNodeManager NodeManager { get; }

        public Dictionary<NodeId, IList<IReference>> ExternalReferences { get; }

        public bool Published { get; set; }

        public bool Staged { get; set; }

        public IAsyncNodeManager? ReplacedNodeManager { get; set; }

        public Dictionary<NodeId, IList<IReference>>? ReplacedExternalReferences { get; set; }
    }
}
