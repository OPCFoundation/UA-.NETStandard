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

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Provider-model factory for the Kafka client adapter used by
    /// <see cref="KafkaBrokerTransport"/>. Test code can swap in a fake
    /// to drive the transport without an actual broker; the default
    /// implementation creates a TFM-specific Kafka-backed adapter.
    /// </summary>
    /// <remarks>
    /// Provides the adapter seam used by the Kafka broker transport per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public interface IKafkaClientFactory
    {
        /// <summary>
        /// Creates a fresh adapter instance.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for the adapter.</param>
        /// <returns>The new adapter.</returns>
        internal IKafkaClientAdapter Create(
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
