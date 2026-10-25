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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Installs a fully prepared address-space publication without running asynchronous or application code.
    /// </summary>
    internal static class AddressSpacePublisher
    {
        /// <summary>
        /// Completes prepared metadata and reference changes around the routing-view commit point.
        /// Every input has already been validated and reserved before the durable decision callback.
        /// </summary>
        internal static void Publish(
            NodeManagerRoutingTable.PreparedRoutes routes,
            StringTable.Publication namespaces,
            StringTable.Publication serverUris,
            TypeTable.Publication types,
            EncodeableFactory.Publication registrations,
            TypeTable typeTree,
            EncodeableFactory factory,
            ArrayOf<PreparedNodeManager> candidates,
            IReadOnlyList<NodeState.ReferenceUpdate> referenceUpdates,
            System.Action viewPublished)
        {
            namespaces.Complete();
            serverUris.Complete();
            types.Complete(typeTree);
            registrations.Complete(factory);
            foreach (PreparedNodeManager candidate in candidates)
            {
                ChangePreparer.Bind(candidate.NodeManager);
            }
            foreach (NodeState.ReferenceUpdate update in referenceUpdates)
            {
                update.Remove();
            }

            routes.Publish();
            viewPublished();

            foreach (NodeState.ReferenceUpdate update in referenceUpdates)
            {
                update.Complete();
            }
        }
    }
}
