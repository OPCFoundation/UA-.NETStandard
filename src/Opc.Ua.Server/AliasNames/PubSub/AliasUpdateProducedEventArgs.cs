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

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Event payload raised by <see cref="AliasNamePublisher.AliasUpdateProduced"/>
    /// every time the publisher builds a Part 17 Annex D
    /// <see cref="AliasUpdateDataType"/> message in response to a
    /// store change. Hand this message off to the transport of your
    /// choice — e.g. a <c>Opc.Ua.PubSub.UaPubSubApplication</c>
    /// configured with the DataSet emitted by
    /// <see cref="AliasUpdateDataSetFactory"/>.
    /// </summary>
    public sealed class AliasUpdateProducedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public AliasUpdateProducedEventArgs(AliasUpdateDataType update)
        {
            Update = update ?? throw new ArgumentNullException(nameof(update));
        }

        /// <summary>
        /// The DataType instance ready to be published.
        /// </summary>
        public AliasUpdateDataType Update { get; }
    }
}
