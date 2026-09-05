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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Manager-level monitored-item batch hooks.
    /// </summary>
    public static class MonitoredItemBuilderExtensions
    {
        /// <summary>
        /// Registers an asynchronous callback for successfully created
        /// monitored-item batches.
        /// </summary>
        public static INodeManagerBuilder OnMonitoredItemsCreated(
            this INodeManagerBuilder builder,
            MonitoredItemsBatchHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(
                    builder,
                    "OnMonitoredItemsCreated");
            concrete.RegisterMonitoredItemsCreated(handler);
            return builder;
        }

        /// <summary>
        /// Registers an asynchronous callback for successfully deleted
        /// monitored-item batches.
        /// </summary>
        public static INodeManagerBuilder OnMonitoredItemsDeleted(
            this INodeManagerBuilder builder,
            MonitoredItemsBatchHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(
                    builder,
                    "OnMonitoredItemsDeleted");
            concrete.RegisterMonitoredItemsDeleted(handler);
            return builder;
        }
    }
}
