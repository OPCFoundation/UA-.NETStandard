/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Supplies subscription services and lifecycle signals to the monitored-item manager.
    /// </summary>
    internal interface IMonitoredItemManagerContext
    {
        /// <summary>
        /// Gets the server identifier of the subscription that owns the monitored items.
        /// </summary>
        /// <value>The subscription identifier, or zero while the subscription is not created.</value>
        uint Id { get; }

        /// <summary>
        /// Gets the service set used to create, modify and delete monitored items.
        /// </summary>
        /// <value>The monitored-item service client for the owning subscription.</value>
        IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; }

        /// <summary>
        /// Gets the service set used to call server methods.
        /// </summary>
        /// <value>The method service client for the owning subscription.</value>
        IMethodServiceSetClientMethods MethodServiceSet { get; }

        /// <summary>
        /// Creates a monitored item bound to the supplied options and runtime context.
        /// </summary>
        /// <param name="name">The item's unique name within its monitored-item collection.</param>
        /// <param name="options">The observable configuration to apply to the item.</param>
        /// <param name="context">The context used by the item to request and report changes.</param>
        /// <returns>The newly created monitored item.</returns>
        MonitoredItem CreateMonitoredItem(
            string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            IMonitoredItemContext context);

        /// <summary>
        /// Signals the owning subscription to apply pending monitored-item changes.
        /// </summary>
        void Update();

        /// <summary>
        /// Requests recreation of the owning subscription on the next state-manager pass.
        /// </summary>
        void RequestRecreate();
    }
}
