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
    /// Event payload raised by an
    /// <see cref="IPubSubConfigurationStore"/> when the persisted
    /// configuration changes. Carries the previous configuration
    /// (or <see langword="null"/> on first load) and the new one so
    /// the application can compute a delta and cascade re-enable /
    /// disable transitions.
    /// </summary>
    /// <remarks>
    /// Implements the configuration-change notification surface
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PublishedDataSetFolder / configuration root</see>.
    /// </remarks>
    public sealed class PubSubConfigurationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationChangedEventArgs"/>.
        /// </summary>
        /// <param name="previous">
        /// Prior configuration, or <see langword="null"/> if this is
        /// the first load.
        /// </param>
        /// <param name="current">New configuration.</param>
        public PubSubConfigurationChangedEventArgs(
            PubSubConfigurationDataType? previous,
            PubSubConfigurationDataType current)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            Previous = previous;
            Current = current;
        }

        /// <summary>
        /// Prior configuration, or <see langword="null"/> if this
        /// is the first load.
        /// </summary>
        public PubSubConfigurationDataType? Previous { get; }

        /// <summary>
        /// New configuration.
        /// </summary>
        public PubSubConfigurationDataType Current { get; }
    }
}
