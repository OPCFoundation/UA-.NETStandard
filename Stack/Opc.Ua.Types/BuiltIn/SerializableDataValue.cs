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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Serialisation surrogate for <see cref="DataValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DataValue"/> itself is an immutable value carrier
    /// with no public setters and no <see cref="DataContractAttribute"/>
    /// decoration. Consumers that need <c>DataContractSerializer</c>
    /// interop register
    /// <see cref="DataValueSurrogateProvider.Instance"/> with their
    /// serializer so this type stands in for <c>DataValue</c> on
    /// the wire / in the schema. The XSD shape is identical to the
    /// historical <c>DataValue</c> contract (namespace, element
    /// names, member order), so wire format and schema-tool output
    /// remain compatible.
    /// </para>
    /// <example>
    /// <code lang="C#">
    /// // Round-trip a DataValue via DataContractSerializer:
    /// var serializer = new DataContractSerializer(typeof(DataValue));
    /// serializer.SetSerializationSurrogateProvider(
    ///     DataValueSurrogateProvider.Instance);
    /// </code>
    /// </example>
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd, Name = "DataValue")]
    public sealed class SerializableDataValue
    {
        /// <summary>
        /// Default constructor required by <c>DataContractSerializer</c>.
        /// </summary>
        public SerializableDataValue()
        {
        }

        /// <summary>
        /// Initialises a surrogate from a <see cref="DataValue"/>.
        /// </summary>
        public SerializableDataValue(DataValue source)
        {
            WrappedValue = source.WrappedValue;
            StatusCode = source.StatusCode;
            SourceTimestamp = source.SourceTimestamp;
            SourcePicoseconds = source.SourcePicoseconds;
            ServerTimestamp = source.ServerTimestamp;
            ServerPicoseconds = source.ServerPicoseconds;
        }

        /// <summary>
        /// Returns an immutable <see cref="DataValue"/> with the same
        /// fields as this surrogate.
        /// </summary>
        public DataValue ToDataValue()
        {
            return new DataValue(
                WrappedValue,
                StatusCode,
                SourceTimestamp,
                ServerTimestamp,
                SourcePicoseconds,
                ServerPicoseconds);
        }

        /// <summary>
        /// The Variant payload.
        /// </summary>
        [DataMember(Name = "Value", Order = 1, IsRequired = false)]
        public Variant WrappedValue { get; set; }

        /// <summary>
        /// The status code.
        /// </summary>
        [DataMember(Order = 2, IsRequired = false)]
        public StatusCode StatusCode { get; set; }

        /// <summary>
        /// The source timestamp.
        /// </summary>
        [DataMember(Order = 3, IsRequired = false)]
        public DateTimeUtc SourceTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the source timestamp.
        /// </summary>
        [DataMember(Order = 4, IsRequired = false)]
        public ushort SourcePicoseconds { get; set; }

        /// <summary>
        /// The server timestamp.
        /// </summary>
        [DataMember(Order = 5, IsRequired = false)]
        public DateTimeUtc ServerTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the server timestamp.
        /// </summary>
        [DataMember(Order = 6, IsRequired = false)]
        public ushort ServerPicoseconds { get; set; }
    }

    /// <summary>
    /// <see cref="ISerializationSurrogateProvider"/> implementation
    /// that ferries <see cref="DataValue"/> through
    /// <c>DataContractSerializer</c> via
    /// <see cref="SerializableDataValue"/>.
    /// </summary>
    /// <remarks>
    /// Register via
    /// <c>serializer.SetSerializationSurrogateProvider(DataValueSurrogateProvider.Instance);</c>.
    /// The provider is stateless and thread-safe.
    /// </remarks>
    public sealed class DataValueSurrogateProvider : ISerializationSurrogateProvider
    {
        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static DataValueSurrogateProvider Instance { get; } = new();

        private DataValueSurrogateProvider()
        {
        }

        /// <inheritdoc/>
        public Type GetSurrogateType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            return type == typeof(DataValue) ? typeof(SerializableDataValue) : type;
        }

        /// <inheritdoc/>
        public object GetObjectToSerialize(object obj, Type targetType)
        {
            // Convert DataValue → SerializableDataValue on the way out.
            if (obj is DataValue dv)
            {
                return new SerializableDataValue(dv);
            }
            return obj;
        }

        /// <inheritdoc/>
        public object GetDeserializedObject(object obj, Type targetType)
        {
            // Convert SerializableDataValue → DataValue on the way in.
            if (obj is SerializableDataValue surrogate)
            {
                return surrogate.ToDataValue();
            }
            return obj;
        }
    }
}
