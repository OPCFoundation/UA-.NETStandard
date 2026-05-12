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

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="ISubscriptionContext"/>. Holds
    /// references to the public service-set interfaces the subscription
    /// uses (which the tests still mock with Moq because they are public
    /// source-generated interfaces). Replaces
    /// <c>Mock&lt;ISubscriptionContext&gt;</c>.
    /// </summary>
    internal sealed class FakeSubscriptionContext : ISubscriptionContext
    {
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public ISubscriptionServiceSetClientMethods SubscriptionServiceSet { get; set; }
            = null!;

        public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; set; }
            = null!;

        public IMethodServiceSetClientMethods MethodServiceSet { get; set; }
            = null!;
    }
}
