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
using System.Reflection;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Surrogate marker interface
    /// </summary>
    public interface ISurrogate
    {
        /// <summary>
        /// Get the value being surrogated
        /// </summary>
        /// <returns></returns>
        object GetValue();
    }

    /// <summary>
    /// Denotes a surrogate for a specific type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISurrogateFor<out T> : ISurrogate
    {
        /// <summary>
        /// Which value is surrogated
        /// </summary>
        T Value { get; }
    }

    /// <summary>
    /// Surrogates for data contract serializer. Used to swap types that
    /// are not directly supported by the serializer for types that are.
    /// </summary>
    public class DataContractSurrogates : ISerializationSurrogateProvider
    {
        /// <summary>
        /// Known types
        /// </summary>
        public static Type[] KnownTypes =>
        [
            .. SurrogateMappings.Keys,
            .. SurrogateMappings.Values
        ];

        /// <summary>
        /// Create surrogate provider
        /// </summary>
        /// <param name="messageContext"></param>
        public DataContractSurrogates(IServiceMessageContext messageContext)
        {
            MessageContext = messageContext;
        }

        /// <summary>
        /// Access to message context passed to provider.
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <inheritdoc/>
        public object GetDeserializedObject(object obj, Type targetType)
        {
            if (obj is ISurrogate surrogate)
            {
                return surrogate.GetValue();
            }

            if (targetType == typeof(ByteString))
            {
                return ByteString.From((byte[])obj);
            }
            if (targetType == typeof(XmlElement))
            {
                return XmlElement.From((System.Xml.XmlElement)obj);
            }

            Type sourceType = obj?.GetType();
            if (sourceType?.IsGenericType == true &&
                sourceType.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!targetType.IsGenericType)
                {
                    return obj;
                }
                if (targetType.GetGenericTypeDefinition() == typeof(ArrayOf<>))
                {
                    // call FromList() on the List object to make an ArrayOf<T> target
                    return targetType
                        .GetMethod(
                            nameof(ArrayOf<>.FromList),
                            BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, [obj]);
                }
                if (targetType.GetGenericTypeDefinition() == typeof(MatrixOf<>))
                {
                    // TODO: call FromMatrix() on the object
                    // return sourceType.GetMethod(nameof(MatrixOf.T)).Invoke(obj, []);
                    return obj;
                }
            }
            // Not a surrogated type or null (default)
            return obj;
        }

        /// <inheritdoc/>
        public object GetObjectToSerialize(object obj, Type targetType)
        {
            // Fast path for already surrogated objects.
            if (obj is ISurrogate surrogate)
            {
                return surrogate; // Surrogate already - serialize as is
            }
            if (!SurrogateMappings.TryGetValue(targetType, out Type surrogateType))
            {
                if (obj is ByteString byteString)
                {
                    return byteString.ToArray();
                }
                if (obj is XmlElement xmlElement)
                {
                    return xmlElement.ToXmlElement();
                }
                Type sourceType = obj?.GetType();
                if (sourceType?.IsGenericType == true)
                {
                    if (sourceType.GetGenericTypeDefinition() == typeof(ArrayOf<>))
                    {
                        // call ToList() on the object
                        return sourceType.GetMethod(nameof(ArrayOf<>.ToList)).Invoke(obj, []);
                    }
                    if (sourceType.GetGenericTypeDefinition() == typeof(MatrixOf<>))
                    {
                        // TODO: call ToMatrix() on the object
                        // return sourceType.GetMethod(nameof(MatrixOf.T)).Invoke(obj, []);
                        return obj;
                    }
                }
                // Handle the case where we are passed the surrogate type as target
                // Could do a reverse lookup here too
                if (!typeof(ISurrogate).IsAssignableFrom(targetType))
                {
                    // Not a surrogated type
                    return obj;
                }
                surrogateType = targetType;
            }
            // Create surrogate instance - all surrogates have a constructor
            // that takes the original object it is surrogate for or a default
            // constructor to create a null instance.
            try
            {
                if (obj is null)
                {
                    return Activator.CreateInstance(surrogateType);
                }
                return Activator.CreateInstance(surrogateType, [obj]);
            }
            catch
            {
                return obj;
            }
        }

        /// <inheritdoc/>
        public Type GetSurrogateType(Type type)
        {
            if (SurrogateMappings.TryGetValue(type, out Type surrogateType))
            {
                return surrogateType;
            }
            if (type == typeof(ByteString))
            {
                return typeof(byte[]);
            }
            if (type == typeof(XmlElement))
            {
                return typeof(System.Xml.XmlElement);
            }
            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(ArrayOf<>))
                {
                    Type elementType = type.GetGenericArguments()[0];
                    return typeof(List<>).MakeGenericType([elementType]);
                }
                if (type.GetGenericTypeDefinition() == typeof(MatrixOf<>))
                {
                    return typeof(Matrix);
                }
            }
            return type;
        }

        /// <summary>
        /// Surrogate mappings (Could get from assembly)
        /// </summary>
        public static readonly Dictionary<Type, Type> SurrogateMappings = new()
        {
            { typeof(NodeId), typeof(SerializableNodeId) },
            { typeof(ExpandedNodeId), typeof(SerializableExpandedNodeId) },
            { typeof(ExtensionObject), typeof(SerializableExtensionObject) },
            { typeof(Uuid), typeof(SerializableUuid) },
            { typeof(StatusCode), typeof(SerializableStatusCode) },
            { typeof(QualifiedName), typeof(SerializableQualifiedName) },
            { typeof(Variant), typeof(SerializableVariant) },
            { typeof(LocalizedText), typeof(SerializableLocalizedText) },
            { typeof(XmlElementCollection), typeof(SerializableXmlElementCollection) }
        };
    }
}
