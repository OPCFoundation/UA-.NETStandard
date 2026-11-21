// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
