// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.AliasNames.PubSub
{
    /// <summary>
    /// Event payload raised by <see cref="AliasNamePubSubReader.AliasUpdateReceived"/>
    /// every time a Part 17 Annex D <c>AliasUpdateDataType</c> message
    /// is fed into the reader.
    /// </summary>
    public sealed class AliasUpdateReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public AliasUpdateReceivedEventArgs(AliasUpdateDataType update)
        {
            Update = update ?? throw new ArgumentNullException(nameof(update));
        }

        /// <summary>
        /// The received message.
        /// </summary>
        public AliasUpdateDataType Update { get; }
    }

    /// <summary>
    /// Transport-agnostic Part 17 Annex D <c>AliasUpdate</c> subscriber
    /// surface. Callers feed in deserialized
    /// <see cref="AliasUpdateDataType"/> instances via
    /// <see cref="Submit(AliasUpdateDataType)"/>; subscribers receive
    /// them as <see cref="AliasUpdateReceived"/> events. Bridging from
    /// a particular wire transport (UADP, JSON-over-MQTT, …) is the
    /// caller's responsibility — typically by hooking
    /// <c>Opc.Ua.PubSub.UaPubSubApplication.DataReceived</c> and
    /// extracting the <c>AliasUpdateDataType</c> from the DataSet
    /// fields, then calling <see cref="Submit"/>.
    /// </summary>
    public sealed class AliasNamePubSubReader
    {
        /// <summary>
        /// Initializes a new reader.
        /// </summary>
        /// <param name="options">Optional tunables.</param>
        public AliasNamePubSubReader(AliasNamePubSubReaderOptions? options = null)
        {
            Options = options ?? new AliasNamePubSubReaderOptions();
        }

        /// <summary>
        /// The (snapshot of) configured options.
        /// </summary>
        public AliasNamePubSubReaderOptions Options { get; }

        /// <summary>
        /// Raised on every accepted update (after filter rules).
        /// </summary>
        public event EventHandler<AliasUpdateReceivedEventArgs>? AliasUpdateReceived;

        /// <summary>
        /// Submits a deserialized message to the reader. Filtered
        /// messages are dropped silently; accepted ones are raised
        /// through <see cref="AliasUpdateReceived"/>.
        /// </summary>
        /// <returns><c>true</c> if the message passed the filter and an
        /// event was raised; <c>false</c> if the message was dropped.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="update"/> is null.
        /// </exception>
        public bool Submit(AliasUpdateDataType update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }
            if (!string.IsNullOrEmpty(Options.ExpectedApplicationUri) &&
                !string.Equals(
                    update.ApplicationUri,
                    Options.ExpectedApplicationUri,
                    StringComparison.Ordinal))
            {
                return false;
            }
            AliasUpdateReceived?.Invoke(
                this, new AliasUpdateReceivedEventArgs(update));
            return true;
        }
    }
}
