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
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Describes a single WoT action surfaced as an OPC UA
    /// <c>MethodNode</c> on an <c>IWoTAssetType</c> instance per
    /// OPC 10100-1 §6.3.9.
    /// </summary>
    public sealed class WotActionTag
    {
        /// <summary>
        /// Initialises a new <see cref="WotActionTag"/>.
        /// </summary>
        public WotActionTag(
            string name,
            NodeId nodeId,
            IReadOnlyList<Argument> inputArguments,
            IReadOnlyList<Argument> outputArguments,
            JsonElement? form)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NodeId = nodeId;
            InputArguments = inputArguments ?? throw new ArgumentNullException(nameof(inputArguments));
            OutputArguments = outputArguments ?? throw new ArgumentNullException(nameof(outputArguments));
            Form = form;
        }

        /// <summary>
        /// The WoT action name (used as <c>BrowseName</c>).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The <c>NodeId</c> of the materialised OPC UA method.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Input arguments derived from the action's <c>input</c> JSON schema.
        /// </summary>
        public IReadOnlyList<Argument> InputArguments { get; }

        /// <summary>
        /// Output arguments derived from the action's <c>output</c> JSON schema.
        /// </summary>
        public IReadOnlyList<Argument> OutputArguments { get; }

        /// <summary>
        /// Raw protocol-binding form (as parsed JSON). Providers cast or
        /// re-parse this element to read their specific metadata.
        /// </summary>
        public JsonElement? Form { get; }
    }
}
