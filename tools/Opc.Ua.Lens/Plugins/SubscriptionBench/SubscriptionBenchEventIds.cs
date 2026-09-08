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

namespace Opc.Ua;

internal static partial class UaLensEventIds
{
    /// <summary>
    /// The Diagnose tools reserve event IDs 2000-2999. Subscription Bench owns
    /// the 2000-2099 sub-range; Performance owns 2100-2199.
    /// </summary>
    public const int SubscriptionBenchConvergeFailed = 2000;
    public const int SubscriptionBenchEngineUnavailable = 2001;
    public const int SubscriptionBenchSubscriptionDisposeFailed = 2002;
    public const int SubscriptionBenchItemAddFailed = 2003;
    public const int SubscriptionBenchStopped = 2004;
    public const int SubscriptionBenchSubtreeWalkFailed = 2005;
    public const int SubscriptionBenchRestoreFailed = 2006;
}
