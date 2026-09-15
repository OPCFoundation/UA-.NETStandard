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
using Opc.Ua.Client;

namespace UaLens.Subscriptions
{
    /// <summary>
    /// Session-wide publish pipeline limits, shared by every subscription.
    /// </summary>
    internal sealed record SessionPublishingSettings
    {
        public SessionPublishingSettings(int minimumRequests, int maximumRequests)
        {
            if (minimumRequests < 1 || maximumRequests < minimumRequests)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumRequests),
                    "The minimum must be positive and no greater than the maximum.");
            }
            MinimumRequests = minimumRequests;
            MaximumRequests = maximumRequests;
        }

        public int MinimumRequests { get; }

        public int MaximumRequests { get; }

        public void Apply(ISession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (MaximumRequests >= session.MaxPublishRequestCount)
            {
                session.MaxPublishRequestCount = MaximumRequests;
                session.MinPublishRequestCount = MinimumRequests;
            }
            else
            {
                session.MinPublishRequestCount = MinimumRequests;
                session.MaxPublishRequestCount = MaximumRequests;
            }
        }
    }
}
