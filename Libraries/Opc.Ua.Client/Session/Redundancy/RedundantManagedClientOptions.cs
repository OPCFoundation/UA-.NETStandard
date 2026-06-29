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

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Selects the OPC 10000-4 §6.6.2.4.5.4 Hot redundancy notification strategy.
    /// </summary>
    public enum HotRedundancyNotificationMode
    {
        /// <summary>
        /// Hot (a): one server reports while the other Hot peers sample without reporting.
        /// </summary>
        ReportingHandoff,

        /// <summary>
        /// Hot (b): all Hot peers report and duplicate notifications are processed by the client.
        /// </summary>
        ReportingMerge
    }

    /// <summary>
    /// Options for <see cref="RedundantManagedClient"/> non-transparent server redundancy Failover.
    /// </summary>
    public sealed record RedundantManagedClientOptions
    {
        /// <summary>
        /// The notification behavior to use for OPC 10000-4 §6.6.2.4.5.4 Hot redundancy.
        /// </summary>
        public HotRedundancyNotificationMode HotNotificationMode { get; init; }

        /// <summary>
        /// Keeps lightweight ServiceLevel status-check sessions open to HotAndMirrored backup Servers.
        /// </summary>
        public bool EnableHotAndMirroredStatusChecks { get; init; }

        /// <summary>
        /// The interval used to poll <c>ServiceLevel</c> on HotAndMirrored status-check sessions.
        /// </summary>
        public TimeSpan HotAndMirroredStatusCheckInterval { get; init; } = TimeSpan.FromSeconds(5);
    }
}
