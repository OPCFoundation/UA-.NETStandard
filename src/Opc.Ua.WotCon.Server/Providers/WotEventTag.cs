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
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Describes a single WoT event affordance surfaced as an OPC UA
    /// EventType on an <c>IWoTAssetType</c> instance per
    /// OPC 10100-1 §6.3.10.
    /// </summary>
    public sealed class WotEventTag
    {
        /// <summary>
        /// Initialises a new <see cref="WotEventTag"/>.
        /// </summary>
        /// <param name="name">The WoT event name.</param>
        /// <param name="eventTypeId">
        /// The NodeId of the materialised OPC UA EventType.
        /// </param>
        /// <param name="sourceNodeId">
        /// The NodeId of the asset object that notifies the event.
        /// </param>
        /// <param name="fields">
        /// Event fields derived from the event's <c>data</c> schema.
        /// </param>
        /// <param name="severity">
        /// The server fallback severity used when the provider supplies none.
        /// </param>
        /// <param name="form">The raw protocol-binding form.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> or <paramref name="fields"/> is <c>null</c>.
        /// </exception>
        public WotEventTag(
            string name,
            NodeId eventTypeId,
            NodeId sourceNodeId,
            IReadOnlyList<Argument> fields,
            ushort severity,
            JsonElement? form)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            EventTypeId = eventTypeId;
            SourceNodeId = sourceNodeId;
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            Severity = severity;
            Form = form;
        }

        /// <summary>
        /// The WoT event name (used as <c>BrowseName</c>).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The <c>NodeId</c> of the materialised OPC UA EventType.
        /// </summary>
        public NodeId EventTypeId { get; }

        /// <summary>
        /// The <c>NodeId</c> of the asset object that notifies the event.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <summary>
        /// Event fields derived from the event's <c>data</c> JSON schema, in
        /// the order a provider supplies their values.
        /// </summary>
        public IReadOnlyList<Argument> Fields { get; }

        /// <summary>
        /// The server fallback OPC 10000-5
        /// <c>BaseEventType.Severity</c> (1..1000).
        /// </summary>
        public ushort Severity { get; }

        /// <summary>
        /// Raw protocol-binding form (as parsed JSON). Providers cast or
        /// re-parse this element to read their specific metadata.
        /// </summary>
        public JsonElement? Form { get; }
    }
}
