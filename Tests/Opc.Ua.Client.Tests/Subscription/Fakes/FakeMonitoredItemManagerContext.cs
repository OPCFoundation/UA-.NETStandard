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

#nullable enable

using System;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IMonitoredItemManagerContext"/>.
    /// Replaces <c>Mock&lt;IMonitoredItemManagerContext&gt;</c>.
    /// </summary>
    internal sealed class FakeMonitoredItemManagerContext : IMonitoredItemManagerContext
    {
        public uint Id { get; set; }

        public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; set; }
            = null!;

        public IMethodServiceSetClientMethods MethodServiceSet { get; set; }
            = null!;

        /// <summary>
        /// Required factory for <see cref="CreateMonitoredItem"/>. Tests must
        /// assign this before invoking the manager.
        /// </summary>
        public Func<string, IOptionsMonitor<MonitoredItems.MonitoredItemOptions>,
            IMonitoredItemContext, MonitoredItems.MonitoredItem> CreateMonitoredItemFactory
        { get; set; }
            = (_, _, _) => throw new InvalidOperationException(
                "CreateMonitoredItemFactory not set on FakeMonitoredItemManagerContext.");

        /// <summary>Number of times <see cref="Update"/> was invoked.</summary>
        public int UpdateCalls { get; private set; }

        public MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
            IMonitoredItemContext context)
        {
            return CreateMonitoredItemFactory(name, options, context);
        }

        public void Update()
        {
            UpdateCalls++;
        }
    }
}
