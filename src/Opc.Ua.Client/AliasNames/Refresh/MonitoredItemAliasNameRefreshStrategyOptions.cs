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

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Tunables for
    /// <see cref="MonitoredItemAliasNameRefreshStrategy"/>.
    /// </summary>
    public sealed class MonitoredItemAliasNameRefreshStrategyOptions
    {
        /// <summary>
        /// When non-<c>null</c>, the strategy adds the
        /// <c>LastChange</c> monitored item to this existing
        /// <see cref="Subscription"/> instead of creating its own.
        /// Disposal will <c>not</c> delete the subscription in this case
        /// — only the monitored item is removed.
        /// </summary>
        public Subscription? SharedSubscription { get; set; }

        /// <summary>
        /// Display name to apply to the <see cref="Subscription"/> the
        /// strategy creates (ignored when
        /// <see cref="SharedSubscription"/> is set). Defaults to
        /// <c>"AliasNameLastChange"</c>.
        /// </summary>
        public string SubscriptionDisplayName { get; set; }
            = "AliasNameLastChange";

        /// <summary>
        /// Publishing interval (ms) of the owned
        /// <see cref="Subscription"/> (ignored when
        /// <see cref="SharedSubscription"/> is set). Defaults to 1000ms.
        /// </summary>
        public double PublishingIntervalMs { get; set; } = 1000.0;

        /// <summary>
        /// Sampling interval (ms) for the <c>LastChange</c> monitored
        /// item. Defaults to 1000ms.
        /// </summary>
        public double SamplingIntervalMs { get; set; } = 1000.0;
    }
}
