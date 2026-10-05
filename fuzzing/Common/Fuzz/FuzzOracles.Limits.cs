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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzOracles
    {
        /// <summary>
        /// Values visited before the limit walk stops; a decode is bounded by its input, so
        /// only a runaway object graph reaches it.
        /// </summary>
        private const int kMaxLimitWalkValues = 1_000_000;

        /// <summary>
        /// Fails with <see cref="ResourceFindingKind.Limit"/> when a decoded value holds a
        /// string, ByteString or array longer than the limits of the context the decoder was
        /// given. A decoder must reject such input instead of materialising it: the limits are
        /// what servers rely on to bound per-request memory.
        /// <para>
        /// Strings are compared in UTF-16 code units, which is never more than their UTF-8 byte
        /// count, so a value accepted by a byte based check is never reported. Raw ExtensionObject
        /// bodies and XmlElement values are not checked; they are bounded by the message size.
        /// Matrices are made consumable through the legacy boxing, so a decoded Variant that no
        /// consumer can materialise fails as the exception it throws.
        /// </para>
        /// </summary>
        /// <exception cref="ResourceBudgetException"></exception>
        public static void CheckDecodedLimits(object value, IServiceMessageContext context, string operation)
        {
            if (value == null || context == null)
            {
                return;
            }

            var walker = new LimitWalker(context, operation);
            walker.Walk(value);
        }

        /// <summary>
        /// Walks the value graph iteratively, so the walk itself does not recurse on deeply
        /// nested values.
        /// </summary>
        private sealed class LimitWalker
        {
            public LimitWalker(IServiceMessageContext context, string operation)
            {
                m_maxStringLength = context.MaxStringLength;
                m_maxByteStringLength = context.MaxByteStringLength;
                m_maxArrayLength = context.MaxArrayLength;
                m_operation = operation;
            }

            public void Walk(object root)
            {
                m_pending.Push((root, "value"));
                int visited = 0;
                while (m_pending.Count > 0)
                {
                    if (visited++ >= kMaxLimitWalkValues)
                    {
                        // Reaching the cap with work still pending is itself the runaway the
                        // walk guards against, so it is a finding rather than a silent return:
                        // an over-limit value could otherwise hide later in the graph.
                        throw new ResourceBudgetException(
                            ResourceFindingKind.Limit,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Decoded value graph exceeds {0} nodes before the limit walk completed ('{1}').",
                                kMaxLimitWalkValues,
                                m_operation));
                    }
                    (object value, string path) = m_pending.Pop();
                    Visit(value, path);
                }
            }

            private void Visit(object value, string path)
            {
                switch (value)
                {
                    case null:
                        return;
                    case string text:
                        CheckString(text, path);
                        return;
                    case ByteString byteString:
                        CheckBytes(byteString.Length, path);
                        return;
                    case byte[] bytes:
                        CheckBytes(bytes.Length, path);
                        return;
                    case NodeId nodeId:
                        CheckNodeId(nodeId, path);
                        return;
                    case ExpandedNodeId expandedNodeId:
                        CheckString(expandedNodeId.NamespaceUri, path + ".NamespaceUri");
                        CheckNodeId(expandedNodeId.InnerNodeId, path);
                        return;
                    case QualifiedName qualifiedName:
                        CheckString(qualifiedName.Name, path + ".Name");
                        return;
                    case LocalizedText localizedText:
                        CheckString(localizedText.Locale, path + ".Locale");
                        CheckString(localizedText.Text, path + ".Text");
                        return;
                    case Variant variant:
                        VisitVariant(variant, path);
                        return;
                    case DataValue dataValue:
                        VisitVariant(dataValue.WrappedValue, path + ".Value");
                        return;
                    case ExtensionObject extensionObject:
                        m_pending.Push((extensionObject.TypeId, path + ".TypeId"));
                        // Without a context only an already decoded body is returned.
                        if (extensionObject.TryGetValue(out IEncodeable body))
                        {
                            m_pending.Push((body, path + ".Body"));
                        }
                        return;
                    case System.Xml.XmlNode:
                    case XmlElement:
                    case Type:
                        return;
                }

                Type type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || type.IsPointer || IsLeafValue(type))
                {
                    return;
                }

                if (!type.IsValueType && !m_seen.Add(value))
                {
                    return;
                }

                if (IsGeneric(type, typeof(ArrayOf<>)))
                {
                    // ArrayOf<T> and MatrixOf<T> wrap memory and are not IEnumerable.
                    VisitEnumerable(GetArrayOfElements(value, type), path);
                    return;
                }

                if (IsGeneric(type, typeof(MatrixOf<>)))
                {
                    // The decoder bounds the matrix shape by MaxArrayLength
                    // (BinaryDecoder.GetInlineMatrixElementCount), so the oracle checks the
                    // same product - including an empty matrix whose non-zero dimensions still
                    // multiply out above the limit - before walking the materialized elements.
                    CheckMatrixShape(value, type, path);
                    object elements = type.GetMethod(nameof(MatrixOf<int>.ToArrayOf), Type.EmptyTypes)
                        .Invoke(value, null);
                    VisitEnumerable(GetArrayOfElements(elements, elements.GetType()), path);
                    return;
                }

                if (value is IEnumerable enumerable)
                {
                    VisitEnumerable(enumerable, path);
                    return;
                }

                if (type.Namespace == null ||
                    !type.Namespace.StartsWith("Opc.Ua", StringComparison.Ordinal))
                {
                    return;
                }

                foreach (PropertyInfo property in GetWalkedProperties(type))
                {
                    object propertyValue;
                    try
                    {
                        propertyValue = property.GetValue(value);
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException != null)
                    {
                        // A getter that throws on a decoded value breaks every consumer.
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo
                            .Capture(ex.InnerException).Throw();
                        throw;
                    }
                    m_pending.Push((propertyValue, path + "." + property.Name));
                }
            }

            private void VisitVariant(Variant variant, string path)
            {
                object raw = variant.AsBoxedObject(Variant.BoxingBehavior.None);
                if (raw != null && IsGeneric(raw.GetType(), typeof(MatrixOf<>)))
                {
                    // The form every legacy consumer (DataValue.Value, the address space)
                    // materialises; a rank the CLR cannot represent throws here.
                    _ = variant.AsBoxedObject(Variant.BoxingBehavior.Legacy);
                }
                m_pending.Push((raw, path));
            }

            private static IEnumerable GetArrayOfElements(object value, Type type)
            {
                return (IEnumerable)type.GetMethod(nameof(ArrayOf<int>.ToArray), Type.EmptyTypes)
                    .Invoke(value, null) ?? Array.Empty<object>();
            }

            private void VisitEnumerable(IEnumerable enumerable, string path)
            {
                int count = 0;
                foreach (object element in enumerable)
                {
                    if (m_maxArrayLength > 0 && count >= m_maxArrayLength)
                    {
                        // Fail on the first element past the limit, before it is queued, so the
                        // oracle cannot itself build a huge pending stack and path strings for
                        // an array a decoder failed to bound.
                        Fail(
                            path,
                            "array",
                            m_maxArrayLength + 1,
                            m_maxArrayLength,
                            nameof(IServiceMessageContext.MaxArrayLength));
                    }
                    m_pending.Push((element, path + "[" + count.ToString(CultureInfo.InvariantCulture) + "]"));
                    count++;
                }
            }

            private void CheckMatrixShape(object matrix, Type type, string path)
            {
                if (m_maxArrayLength <= 0)
                {
                    return;
                }

                var dimensions = (int[])type
                    .GetProperty(nameof(MatrixOf<int>.Dimensions))!
                    .GetValue(matrix);
                if (dimensions == null)
                {
                    return;
                }

                // The product of the non-zero dimensions, as the decoder measures the shape; a
                // dimension <= 0 carries no values (OPC 10000-6 5.2.5 Table 28).
                long product = 1;
                foreach (int dimension in dimensions)
                {
                    if (dimension <= 0)
                    {
                        continue;
                    }
                    product *= dimension;
                    if (product > m_maxArrayLength)
                    {
                        Fail(
                            path,
                            "matrix",
                            product > int.MaxValue ? int.MaxValue : (int)product,
                            m_maxArrayLength,
                            nameof(IServiceMessageContext.MaxArrayLength));
                    }
                }
            }

            private void CheckNodeId(NodeId nodeId, string path)
            {
                if (nodeId.TryGetValue(out string text))
                {
                    CheckString(text, path + ".Identifier");
                }
                else if (nodeId.TryGetValue(out ByteString opaque))
                {
                    CheckBytes(opaque.Length, path + ".Identifier");
                }
            }

            private void CheckString(string text, string path)
            {
                if (text != null && m_maxStringLength > 0 && text.Length > m_maxStringLength)
                {
                    Fail(path, "string", text.Length, m_maxStringLength, nameof(IServiceMessageContext.MaxStringLength));
                }
            }

            private void CheckBytes(int length, string path)
            {
                if (m_maxByteStringLength > 0 && length > m_maxByteStringLength)
                {
                    Fail(path, "ByteString", length, m_maxByteStringLength, nameof(IServiceMessageContext.MaxByteStringLength));
                }
            }

            private void Fail(string path, string kind, int length, int limit, string limitName)
            {
                throw new ResourceBudgetException(
                    ResourceFindingKind.Limit,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Decoded {0} of length {1} at {2} exceeds {3} {4} ('{5}').",
                        kind,
                        length,
                        path,
                        limitName,
                        limit,
                        m_operation));
            }

            private static bool IsLeafValue(Type type)
            {
                return type == typeof(decimal) ||
                    type == typeof(DateTime) ||
                    type == typeof(DateTimeOffset) ||
                    type == typeof(TimeSpan) ||
                    type == typeof(Guid) ||
                    type == typeof(DateTimeUtc) ||
                    type == typeof(StatusCode) ||
                    type == typeof(Uuid);
            }

            private static bool IsGeneric(Type type, Type definition)
            {
                return type.IsGenericType && type.GetGenericTypeDefinition() == definition;
            }

            private static PropertyInfo[] GetWalkedProperties(Type type)
            {
                return s_properties.GetOrAdd(type, static t =>
                {
                    var properties = new List<PropertyInfo>();
                    foreach (PropertyInfo property in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (property.CanRead &&
                            property.GetIndexParameters().Length == 0 &&
                            !IsRefStruct(property.PropertyType) &&
                            property.PropertyType != typeof(Type) &&
                            property.Name != "Handle")
                        {
                            properties.Add(property);
                        }
                    }
                    return [.. properties];
                });
            }

            private readonly Stack<(object Value, string Path)> m_pending = new();
            private readonly HashSet<object> m_seen = new(ReferenceComparer.Instance);
            private readonly int m_maxStringLength;
            private readonly int m_maxByteStringLength;
            private readonly int m_maxArrayLength;
            private readonly string m_operation;
        }

        private static bool IsRefStruct(Type type)
        {
#if NETFRAMEWORK
            foreach (object attribute in type.GetCustomAttributes(false))
            {
                if (attribute.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                {
                    return true;
                }
            }
            return type.IsByRef;
#else
            return type.IsByRef || type.IsByRefLike;
#endif
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static ReferenceComparer Instance { get; } = new();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_properties = new();
    }
}
