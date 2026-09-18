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
