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
#if NET8_0_OR_GREATER
using Opc.Ua;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Apache Arrow IPC-stream PubSub NetworkMessage. The stream schema
    /// describes one DataSet; each RecordBatch row is one DataSetMessage
    /// sample and each DataSet field is a typed Arrow column.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public sealed record ArrowNetworkMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// Identifies the MQTT transport profile URI used for Arrow PubSub frames.
        /// </summary>
        public const string PubSubMqttArrowTransport = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-arrow";

        /// <summary>
        /// Gets the DataSetClassId advertised with the Arrow stream schema metadata.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// Gets the schema identifier used by schema exchange and cache lookups.
        /// </summary>
        public string SchemaId { get; init; } = string.Empty;

        /// <inheritdoc/>
        public override string TransportProfileUri
        {
            get { return PubSubMqttArrowTransport; }
        }
    }
}
#endif
