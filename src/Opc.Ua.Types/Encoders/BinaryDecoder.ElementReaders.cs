/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    public partial class BinaryDecoder
    {
        /// <summary>
        /// Reads one element of an array, see ReadArray. The readers are
        /// structs so that the JIT specializes the element loop for each of
        /// them and calls the element read directly instead of through a
        /// delegate.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private interface IElementReader<T>
        {
            /// <summary>
            /// The minimum number of bytes a single element takes on the
            /// wire, 0 when an element can be empty.
            /// </summary>
            int MinElementSize { get; }

            /// <summary>
            /// Reads a single element.
            /// </summary>
            T Read(BinaryDecoder decoder);
        }

        private readonly struct BooleanElementReader : IElementReader<bool>
        {
            public int MinElementSize => 1;

            public bool Read(BinaryDecoder decoder)
            {
                return decoder.ReadBoolean(null);
            }
        }

        private readonly struct StringElementReader : IElementReader<string?>
        {
            public int MinElementSize => 4;

            public string? Read(BinaryDecoder decoder)
            {
                return decoder.ReadString(null);
            }
        }

        private readonly struct DateTimeElementReader : IElementReader<DateTimeUtc>
        {
            public int MinElementSize => 8;

            public DateTimeUtc Read(BinaryDecoder decoder)
            {
                return decoder.ReadDateTime(null);
            }
        }

        private readonly struct GuidElementReader : IElementReader<Uuid>
        {
            public int MinElementSize => 16;

            public Uuid Read(BinaryDecoder decoder)
            {
                return decoder.ReadGuid(null);
            }
        }

        private readonly struct ByteStringElementReader : IElementReader<ByteString>
        {
            public int MinElementSize => 4;

            public ByteString Read(BinaryDecoder decoder)
            {
                return decoder.ReadByteString(null);
            }
        }

        private readonly struct XmlElementElementReader : IElementReader<XmlElement>
        {
            public int MinElementSize => 4;

            public XmlElement Read(BinaryDecoder decoder)
            {
                return decoder.ReadXmlElement(null);
            }
        }

        private readonly struct NodeIdElementReader : IElementReader<NodeId>
        {
            public int MinElementSize => 2;

            public NodeId Read(BinaryDecoder decoder)
            {
                return decoder.ReadNodeId(null);
            }
        }

        private readonly struct ExpandedNodeIdElementReader : IElementReader<ExpandedNodeId>
        {
            public int MinElementSize => 2;

            public ExpandedNodeId Read(BinaryDecoder decoder)
            {
                return decoder.ReadExpandedNodeId(null);
            }
        }

        private readonly struct StatusCodeElementReader : IElementReader<StatusCode>
        {
            public int MinElementSize => 4;

            public StatusCode Read(BinaryDecoder decoder)
            {
                return decoder.ReadStatusCode(null);
            }
        }

        private readonly struct DiagnosticInfoElementReader : IElementReader<DiagnosticInfo?>
        {
            public int MinElementSize => 1;

            public DiagnosticInfo? Read(BinaryDecoder decoder)
            {
                return decoder.ReadDiagnosticInfo(null);
            }
        }

        private readonly struct QualifiedNameElementReader : IElementReader<QualifiedName>
        {
            public int MinElementSize => 6;

            public QualifiedName Read(BinaryDecoder decoder)
            {
                return decoder.ReadQualifiedName(null);
            }
        }

        private readonly struct LocalizedTextElementReader : IElementReader<LocalizedText>
        {
            public int MinElementSize => 1;

            public LocalizedText Read(BinaryDecoder decoder)
            {
                return decoder.ReadLocalizedText(null);
            }
        }

        private readonly struct VariantElementReader : IElementReader<Variant>
        {
            public int MinElementSize => 1;

            public Variant Read(BinaryDecoder decoder)
            {
                return decoder.ReadVariant(null);
            }
        }

        private readonly struct DataValueElementReader : IElementReader<DataValue>
        {
            public int MinElementSize => 1;

            public DataValue Read(BinaryDecoder decoder)
            {
                return decoder.ReadDataValue(null);
            }
        }

        private readonly struct ExtensionObjectElementReader : IElementReader<ExtensionObject>
        {
            public int MinElementSize => 3;

            public ExtensionObject Read(BinaryDecoder decoder)
            {
                return decoder.ReadExtensionObject(null);
            }
        }

        private readonly struct EnumValueElementReader : IElementReader<EnumValue>
        {
            public int MinElementSize => 4;

            public EnumValue Read(BinaryDecoder decoder)
            {
                return decoder.ReadEnumerated(null);
            }
        }

        private readonly struct EnumeratedElementReader<T> : IElementReader<T>
            where T : struct, Enum
        {
            public int MinElementSize => 4;

            public T Read(BinaryDecoder decoder)
            {
                return decoder.ReadEnumerated<T>(null);
            }
        }

        // An encodeable can encode to no bytes at all, only the
        // MaxArrayLength limit applies to its element count.
        private readonly struct EncodeableElementReader<T> : IElementReader<T>
            where T : IEncodeable, new()
        {
            public int MinElementSize => 0;

            public T Read(BinaryDecoder decoder)
            {
                return decoder.ReadEncodeable<T>(null);
            }
        }

        // An encodeable can encode to no bytes at all, only the
        // MaxArrayLength limit applies to its element count.
        private readonly struct EncodeableByTypeIdElementReader<T> : IElementReader<T>
            where T : IEncodeable
        {
            public EncodeableByTypeIdElementReader(ExpandedNodeId encodeableTypeId)
            {
                m_encodeableTypeId = encodeableTypeId;
            }

            public int MinElementSize => 0;

            public T Read(BinaryDecoder decoder)
            {
                return decoder.ReadEncodeable<T>(null, m_encodeableTypeId);
            }

            private readonly ExpandedNodeId m_encodeableTypeId;
        }

        private readonly struct EncodeableAsExtensionObjectElementReader<T> : IElementReader<T>
            where T : IEncodeable
        {
            public int MinElementSize => 3;

            public T Read(BinaryDecoder decoder)
            {
                return decoder.ReadEncodeableAsExtensionObject<T>(null);
            }
        }
    }
}
