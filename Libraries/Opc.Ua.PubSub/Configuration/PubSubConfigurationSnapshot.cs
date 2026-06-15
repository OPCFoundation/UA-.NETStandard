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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Immutable wrapper around a loaded
    /// <see cref="PubSubConfigurationDataType"/> plus the materialised
    /// lookup tables Phase 4 will compute (e.g. by connection name,
    /// by writer id). For Phase 1 only the source DataType and the
    /// load timestamp are exposed; index construction lands in
    /// Phase 4 (S4 — metadata-registry-config-validator).
    /// </summary>
    /// <remarks>
    /// Implements the runtime view of the configuration model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PubSub configuration model</see>.
    /// </remarks>
    public sealed class PubSubConfigurationSnapshot
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationSnapshot"/>.
        /// </summary>
        /// <param name="configuration">Underlying configuration.</param>
        /// <param name="createdAt">Load / build timestamp.</param>
        public PubSubConfigurationSnapshot(
            PubSubConfigurationDataType configuration,
            DateTimeUtc createdAt)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Configuration = configuration;
            CreatedAt = createdAt;
        }

        /// <summary>
        /// Underlying configuration.
        /// </summary>
        public PubSubConfigurationDataType Configuration { get; }

        /// <summary>
        /// Timestamp at which the snapshot was loaded or computed.
        /// </summary>
        public DateTimeUtc CreatedAt { get; }
    }
}
