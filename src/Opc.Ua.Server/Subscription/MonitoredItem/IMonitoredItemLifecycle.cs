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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides the internal lifecycle contract used when a monitored node is deleted or rebound.
    /// </summary>
    internal interface IMonitoredItemLifecycle
    {
        /// <summary>
        /// Gets whether the monitored item is detached from its node manager dispatch path.
        /// </summary>
        bool IsDetached { get; }

        /// <summary>
        /// Gets whether the monitored node is currently deleted.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Marks the monitored node as deleted and schedules the required status notification.
        /// </summary>
        void MarkNodeDeleted();

        /// <summary>
        /// Enters the manager-detach transition while preserving rollback ownership.
        /// </summary>
        void BeginDetach();

        /// <summary>
        /// Marks the monitored item as detached and replaces manager-owned references.
        /// </summary>
        void Detach(IAsyncNodeManager nodeManager, object managerHandle);

        /// <summary>
        /// Ensures that the missing-node status is scheduled for publication.
        /// </summary>
        void QueueNodeIdUnknown();

        /// <summary>
        /// Rebinds the monitored item to a compatible node manager and handle.
        /// </summary>
        void Rebind(IAsyncNodeManager nodeManager, object managerHandle);
    }
}
