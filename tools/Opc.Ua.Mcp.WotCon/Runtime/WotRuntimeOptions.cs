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
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host ceilings for live WoT operations, independent of catalogue selection and caller authority.
    /// Shared runtime quotas, idle expiry and hard expiry remain in <see cref="McpExecutionOptions"/>.
    /// </summary>
    public sealed class WotRuntimeOptions
    {
        /// <summary>
        /// Gets or sets the request and response payload ceiling, in bytes.
        /// </summary>
        public int MaxPayloadBytes { get; set; } = 65_536;

        /// <summary>
        /// Gets or sets the complete typed JSON result ceiling, in bytes.
        /// </summary>
        public int MaxResultBytes { get; set; } = 262_144;

        /// <summary>
        /// Gets or sets the largest observation batch.
        /// </summary>
        public int MaxNotifications { get; set; } = 64;

        /// <summary>
        /// Gets or sets the longest single observation window.
        /// </summary>
        public int MaxObservationMilliseconds { get; set; } = 10_000;

        /// <summary>
        /// Gets or sets the timeout for an executor request; the registry's independent deadline still applies.
        /// </summary>
        public int OperationTimeoutMilliseconds { get; set; } = 15_000;

        /// <summary>
        /// Gets or sets the minimum HTTP/Modbus polling or requested OPC UA sampling interval.
        /// </summary>
        public int ObserveIntervalMilliseconds { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the maximum action argument count.
        /// HTTP and MQTT additionally require zero or one body argument.
        /// </summary>
        public int MaxInputArguments { get; set; } = 32;

        /// <summary>
        /// Rejects invalid host settings rather than silently raising a boundary.
        /// </summary>
        public void Validate()
        {
            Range(MaxPayloadBytes, 1, 1_048_576);
            Range(MaxResultBytes, 1024, 4_194_304);
            Range(MaxNotifications, 1, 256);
            Range(MaxObservationMilliseconds, 1, 60_000);
            Range(OperationTimeoutMilliseconds, 1, 120_000);
            Range(ObserveIntervalMilliseconds, 100, 60_000);
            Range(MaxInputArguments, 1, 128);
        }

        /// <summary>
        /// Captures immutable-by-ownership host settings for a service instance.
        /// </summary>
        internal WotRuntimeOptions Snapshot()
        {
            Validate();
            return new WotRuntimeOptions
            {
                MaxPayloadBytes = MaxPayloadBytes,
                MaxResultBytes = MaxResultBytes,
                MaxNotifications = MaxNotifications,
                MaxObservationMilliseconds = MaxObservationMilliseconds,
                OperationTimeoutMilliseconds = OperationTimeoutMilliseconds,
                ObserveIntervalMilliseconds = ObserveIntervalMilliseconds,
                MaxInputArguments = MaxInputArguments
            };
        }

        /// <summary>
        /// Supplies existing planners and executors with the same payload, URI and timeout limits.
        /// </summary>
        internal WotBindingBounds CreateBounds()
        {
            return new WotBindingBounds
            {
                MaxPayloadBytes = MaxPayloadBytes,
                MaxTopicLength = 2048,
                MaxUriLength = 2048,
                AllowMqttWildcardTopics = false,
                DefaultTimeout = TimeSpan.FromMilliseconds(OperationTimeoutMilliseconds)
            };
        }

        /// <summary>
        /// Validates one inclusive host bound.
        /// </summary>
        private static void Range(int value, int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum);
        }
    }
}
