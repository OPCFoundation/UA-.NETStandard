/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
