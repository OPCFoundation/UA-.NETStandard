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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: reports the local Server's current <b>load weight</b> (0 = idle,
    /// 255 = fully loaded) used only to break ties among peers already tied at the highest health
    /// <c>ServiceLevel</c> when directing a Client to the best Server in a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// The load weight is deliberately a <b>separate</b> signal from <c>ServiceLevel</c>: <c>ServiceLevel</c> keeps
    /// its OPC UA meaning (health/eligibility and Failover), while a stale or missing load weight only affects
    /// tie-breaking and never eligibility. The default <see cref="ConstantLoadWeightProvider"/> reports a fixed 0,
    /// which reduces load direction to random selection among equally-healthy peers.
    /// </remarks>
    public interface ILoadWeightProvider
    {
        /// <summary>
        /// The current load weight (0 = idle .. 255 = fully loaded).
        /// </summary>
        byte GetLoadWeight();

        /// <summary>
        /// Raised with the new load weight whenever it changes.
        /// </summary>
        event Action<byte>? LoadWeightChanged;
    }
}
