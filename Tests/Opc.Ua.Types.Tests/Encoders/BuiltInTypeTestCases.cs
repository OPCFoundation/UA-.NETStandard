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
using System.Collections;
using System.IO;
using System.Runtime.Serialization;
using System.Xml.XPath;
using Moq;
using NUnit.Framework;
using Opc.Ua.Types;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Built in type test cases
    /// </summary>
    public static class BuiltInTypeTestCases
    {
        public static IEnumerable ByteStrings
        {
            get
            {
                yield return new TestCaseData(
                    ByteString.Empty);
                yield return new TestCaseData(
                    ByteString.From(new byte[] { 1, 2, 3 }));
            }
        }

        public static IEnumerable ByteStringValues
        {
            get
            {
                yield return new TestCaseData(
                    ByteString.Empty, 0);
                yield return new TestCaseData(
                    ByteString.Empty, 4);
                yield return new TestCaseData(
                    ByteString.From(new byte[] { 1, 2, 3 }), 100);
            }
        }

        public static IEnumerable DataValues
        {
            get
            {
                yield return new TestCaseData(
                    (DataValue)null);
                yield return new TestCaseData(
                    new DataValue(Variant.From(1), 0, DateTimeUtc.Now, DateTimeUtc.Now));
                yield return new TestCaseData(
                    new DataValue(Variant.From(1u), 0, DateTimeUtc.MinValue, DateTimeUtc.Now));
                yield return new TestCaseData(
                    new DataValue(Variant.From(1f), 0, DateTimeUtc.MinValue, DateTimeUtc.MinValue));
                yield return new TestCaseData(
                    new DataValue(Variant.Null, 555, DateTimeUtc.Now, DateTimeUtc.MinValue));
                yield return new TestCaseData(
                    new DataValue(Variant.From("string"), StatusCodes.Bad, DateTimeUtc.MinValue, DateTimeUtc.Now));
            }
        }

        public static IEnumerable DataValueValues
        {
            get
            {
                yield return new TestCaseData(
                    null, 0);
                yield return new TestCaseData(
                    null, 55);
                yield return new TestCaseData(
                    new DataValue(Variant.From(1), 0, DateTimeUtc.Now, DateTimeUtc.Now), 1);
                yield return new TestCaseData(
                    new DataValue(Variant.From(1u), 0, DateTimeUtc.MinValue, DateTimeUtc.Now), 55);
                yield return new TestCaseData(
                    new DataValue(Variant.From(1f), 0, DateTimeUtc.MinValue, DateTimeUtc.MinValue), 33);
                yield return new TestCaseData(
                    new DataValue(Variant.Null, 555, DateTimeUtc.Now, DateTimeUtc.MinValue), 166);
                yield return new TestCaseData(
                    new DataValue(Variant.From("string"), StatusCodes.Bad, DateTimeUtc.MinValue, DateTimeUtc.Now), 2);
            }
        }

        public static IEnumerable DateTimes
        {
            get
            {
                yield return new TestCaseData(
                    DateTimeUtc.Now);
                yield return new TestCaseData(
                    new DateTimeUtc(1600, 12, 31, 23, 59, 59));
                yield return new TestCaseData(
                    new DateTimeUtc(1601, 1, 1, 0, 0, 0));
                yield return new TestCaseData(
                    new DateTimeUtc(2024, 1, 1, 12, 0, 0));
                yield return new TestCaseData(
                    DateTimeUtc.MinValue);
                yield return new TestCaseData(
                    DateTimeUtc.MaxValue);
            }
        }

        public static IEnumerable DateTimeValues
        {
            get
            {
                yield return new TestCaseData(
                    new DateTimeUtc(1600, 12, 31, 23, 59, 59), 0);
                yield return new TestCaseData(
                    DateTimeUtc.Now, 4);
                yield return new TestCaseData(
                    new DateTimeUtc(1600, 12, 31, 23, 59, 59), 4);
                yield return new TestCaseData(
                    new DateTimeUtc(1601, 1, 1, 0, 0, 0), 8);
                yield return new TestCaseData(
                    new DateTimeUtc(2024, 1, 1, 12, 0, 0), 8);
                yield return new TestCaseData(
                    new DateTimeUtc(2024, 1, 1, 12, 0, 0), 8);
                yield return new TestCaseData(
                    new DateTimeUtc(2024, 6, 15, 8, 30, 30), 8);
                yield return new TestCaseData(
                    new DateTimeUtc(2024, 12, 31, 23, 59, 59), 8);
                yield return new TestCaseData(
                    DateTimeUtc.MinValue, 0);
                yield return new TestCaseData(
                    DateTimeUtc.MinValue, 4);
                yield return new TestCaseData(
                    DateTimeUtc.MaxValue, 8);
                yield return new TestCaseData(
                    DateTimeUtc.Now, 100);
            }
        }

        public static IEnumerable DiagnosticInfos
        {
            get
            {
                yield return new TestCaseData(
                    (DiagnosticInfo)null);
                yield return new TestCaseData(
                    new DiagnosticInfo
                    {
                        AdditionalInfo = "addition",
                        InnerStatusCode = 5,
                        LocalizedText = 1,
                        NamespaceUri = 55
                    });
            }
        }

        public static IEnumerable DiagnosticInfoValues
        {
            get
            {
                yield return new TestCaseData(
                    null, 0);
                yield return new TestCaseData(
                    null, 55);
                yield return new TestCaseData(
                    new DiagnosticInfo
                    {
                        AdditionalInfo = "addition",
                        InnerStatusCode = 5,
                        LocalizedText = 1,
                        NamespaceUri = 55
                    }, 5);
            }
        }

        public static IEnumerable ExpandedNodeIds
        {
            get
            {
                yield return new TestCaseData(
                    ExpandedNodeId.Null);
                yield return new TestCaseData(
                    new ExpandedNodeId(100u));
                yield return new TestCaseData(
                    new ExpandedNodeId(100u, 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(100u, 200));
                yield return new TestCaseData(
                    new ExpandedNodeId(100u, 4, "http://uri/4"));
                yield return new TestCaseData(
                    new ExpandedNodeId(100u, 4, "http://uri/4", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(10000));
                yield return new TestCaseData(
                    new ExpandedNodeId(10000, 30));
                yield return new TestCaseData(
                    new ExpandedNodeId(10000, 200));
                yield return new TestCaseData(
                    new ExpandedNodeId(10000, 4, "http://uri/4"));
                yield return new TestCaseData(
                    new ExpandedNodeId(10000, 4, "http://uri/4", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue));
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 101));
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 4, "http://uri/4", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 7, "http://uri/7", 1000));
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 0));
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 155));
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 5, "http://uri/5"));
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 5, "http://uri/5", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.Empty, 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid()));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid(), 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid(), 120));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid(), 4, "http://uri/4"));
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid(), 4, "http://uri/4", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 0, string.Empty, 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 191));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 2, "http://uri/2", 5));
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 2, "http://uri/2", 101));
            }
        }

        public static IEnumerable ExpandedNodeIdValues
        {
            get
            {
                yield return new TestCaseData(
                    ExpandedNodeId.Null, 0);
                yield return new TestCaseData(
                    ExpandedNodeId.Null, 55);
                yield return new TestCaseData(
                    new ExpandedNodeId(100, 4, "http://uri/4"), 154);
                yield return new TestCaseData(
                    new ExpandedNodeId(10000), 2);
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 4, "http://uri/4", 5), 1);
                yield return new TestCaseData(
                    new ExpandedNodeId(100, 5), 4);
                yield return new TestCaseData(
                    new ExpandedNodeId(10000, 30), 100);
                yield return new TestCaseData(
                    new ExpandedNodeId(int.MaxValue, 7, "http://uri/7", 1000), 54);
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 0), 535);
                yield return new TestCaseData(
                    new ExpandedNodeId("test", 5, "http://uri/5"), 1);
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.Empty, 5), 65);
                yield return new TestCaseData(
                    new ExpandedNodeId(Uuid.NewUuid(), 4, "http://uri/4", 1), 35);
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 0, string.Empty, 5), 1);
                yield return new TestCaseData(
                    new ExpandedNodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 34, "http://uri/34", 5), 1000);
            }
        }

        public static IEnumerable ExtensionObjects
        {
            get
            {
                yield return new TestCaseData(
                    ExtensionObject.Null);
                yield return new TestCaseData(
                    new ExtensionObject(new NodeId("string", 0), ByteString.From(3, 4, 3)));
            }
        }

        public static IEnumerable ExtensionObjectValues
        {
            get
            {
                yield return new TestCaseData(
                    ExtensionObject.Null, 0);
                yield return new TestCaseData(
                    ExtensionObject.Null, 55);
                yield return new TestCaseData(
                    new ExtensionObject(new NodeId("string", 0), ByteString.From(3, 4, 3)), 5);
            }
        }

        public static IEnumerable Guids
        {
            get
            {
                yield return new TestCaseData(
                    Uuid.Empty);
                yield return new TestCaseData(
                    Uuid.NewUuid());
            }
        }

        public static IEnumerable GuidValues
        {
            get
            {
                yield return new TestCaseData(
                    Uuid.Empty, 0);
                yield return new TestCaseData(
                    Uuid.Empty, 4);
                yield return new TestCaseData(
                    Uuid.NewUuid(), 100);
            }
        }

        public static IEnumerable LocalizedTexts
        {
            get
            {
                yield return new TestCaseData(
                    LocalizedText.Null);
                yield return new TestCaseData(
                    new LocalizedText("en", "http://uri/3"));
                // yield return new TestCaseData(
                //     new LocalizedText("en", string.Empty));
                yield return new TestCaseData(
                    LocalizedText.From("test"));
                yield return new TestCaseData(
                    new LocalizedText(string.Empty, "http://uri/1"));
            }
        }

        public static IEnumerable LocalizedTextValues
        {
            get
            {
                yield return new TestCaseData(
                    LocalizedText.Null, 0);
                yield return new TestCaseData(
                    LocalizedText.Null, 55);
                yield return new TestCaseData(
                    new LocalizedText("en", "http://uri/3"), 5);
                // yield return new TestCaseData(
                //     new LocalizedText("en", string.Empty), 5);
                yield return new TestCaseData(
                    LocalizedText.From("test"), 5);
                yield return new TestCaseData(
                    new LocalizedText(string.Empty, "http://uri/1"), 53);
            }
        }

        public static IEnumerable NodeIds
        {
            get
            {
                yield return new TestCaseData(
                    NodeId.Null);
                yield return new TestCaseData(
                    new NodeId(100));
                yield return new TestCaseData(
                    new NodeId(10000));
                yield return new TestCaseData(
                    new NodeId(int.MaxValue));
                yield return new TestCaseData(
                    new NodeId(100, 5));
                yield return new TestCaseData(
                    new NodeId(10000, 3));
                yield return new TestCaseData(
                    new NodeId(int.MaxValue, 10));
                yield return new TestCaseData(
                    new NodeId("test", 0));
                yield return new TestCaseData(
                    new NodeId("test", 5));
                yield return new TestCaseData(
                    new NodeId(Uuid.Empty, 5));
                yield return new TestCaseData(
                    new NodeId(Uuid.NewUuid()));
                yield return new TestCaseData(
                    new NodeId(Uuid.NewUuid(), 111));
                yield return new TestCaseData(
                    new NodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })));
                yield return new TestCaseData(
                    new NodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 4));
            }
        }

        public static IEnumerable NodeIdValues
        {
            get
            {
                yield return new TestCaseData(
                    NodeId.Null, 0);
                yield return new TestCaseData(
                    NodeId.Null, 55);
                yield return new TestCaseData(
                    new NodeId(100), 154);
                yield return new TestCaseData(
                    new NodeId(10000), 2);
                yield return new TestCaseData(
                    new NodeId(int.MaxValue), 1);
                yield return new TestCaseData(
                    new NodeId(100, 5), 4);
                yield return new TestCaseData(
                    new NodeId(10000, 3), 100);
                yield return new TestCaseData(
                    new NodeId(int.MaxValue, 10), 54);
                yield return new TestCaseData(
                    new NodeId("test", 0), 535);
                yield return new TestCaseData(
                    new NodeId("test", 5), 1);
                yield return new TestCaseData(
                    new NodeId(Uuid.Empty, 5), 65);
                yield return new TestCaseData(
                    new NodeId(Uuid.NewUuid()), 35);
                yield return new TestCaseData(
                    new NodeId(Uuid.NewUuid(), 111), 8);
                yield return new TestCaseData(
                    new NodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })), 1);
                yield return new TestCaseData(
                    new NodeId(ByteString.From(new byte[] { 1, 2, 3, 4, 5 }), 4), 1000);
            }
        }

        public static IEnumerable QualifiedNames
        {
            get
            {
                yield return new TestCaseData(
                    QualifiedName.Null);
                yield return new TestCaseData(
                    new QualifiedName("adfadfasdfasdfsadfs"));
                yield return new TestCaseData(
                    new QualifiedName("test", 8));
            }
        }

        public static IEnumerable QualifiedNameValues
        {
            get
            {
                yield return new TestCaseData(
                    QualifiedName.Null, 0);
                yield return new TestCaseData(
                    QualifiedName.Null, 55);
                yield return new TestCaseData(
                    new QualifiedName("adfadfasdfasdfsadfs"), 154);
                yield return new TestCaseData(
                    new QualifiedName("test", 8), 2);
            }
        }

        public static IEnumerable StatusCodes2
        {
            get
            {
                yield return new TestCaseData(
                    StatusCodes.Good);
                yield return new TestCaseData(
                    StatusCodes.Bad);
                yield return new TestCaseData(
                    StatusCodes.BadIndexRangeNoData);
                yield return new TestCaseData(
                    StatusCodes.Uncertain);
            }
        }

        public static IEnumerable StatusCodeValues
        {
            get
            {
                yield return new TestCaseData(
                    StatusCodes.Good, 0);
                yield return new TestCaseData(
                    StatusCodes.Good, 55);
                yield return new TestCaseData(
                    StatusCodes.Bad, 154);
                yield return new TestCaseData(
                    StatusCodes.BadIndexRangeInvalid, 154);
                yield return new TestCaseData(
                    StatusCodes.Uncertain, 2);
            }
        }

        public static IEnumerable VariantsWithMatrix
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(new sbyte[,]
                    {
                        { 1, 2, 3 },
                        { 2, 4, 5 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new byte[,]
                    {
                        { 1, 2, 3 },
                        { 2, 4, 5 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new int[,]
                    {
                        { 1, 2, 3 },
                        { 2, 4, 5 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new string[,,]
                    {
                        {
                            { string.Empty, string.Empty },
                            { string.Empty, string.Empty }
                        }
                    }));
                yield return new TestCaseData(
                    Variant.From(new byte[,,]
                    {
                        {
                            { 1, 2, 3 },
                            { 2, 4, 5 }
                        },
                        {
                            { 1, 2, 3 },
                            { 2, 4, 5 }
                        }
                    }));
                yield return new TestCaseData(
                    Variant.From(new long[,,,]
                    {
                        {
                            {
                                { 1, 2, 3 },
                                { 2, 4, 5 }
                            }
                        }
                    }));
                yield return new TestCaseData(
                    Variant.From(new Variant[,]
                    {
                        { Variant.From(1), Variant.From(2) },
                        { Variant.From(string.Empty), Variant.From(0.1) }
                    }));
            }
        }

        public static IEnumerable Variants
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(true));
                yield return new TestCaseData(
                    Variant.From(StatusCodes.Bad));
                yield return new TestCaseData(
                    Variant.From(StatusCodes.Good));
                yield return new TestCaseData(
                    Variant.From(new NodeId(1)));
                yield return new TestCaseData(
                    Variant.From(new ExpandedNodeId(1)));
                yield return new TestCaseData(
                    Variant.From(new QualifiedName("test")));
                yield return new TestCaseData(
                    Variant.From(new LocalizedText("test")));
                yield return new TestCaseData(
                    Variant.From(new DataValue(Variant.From("test"))));
                yield return new TestCaseData(
                    Variant.From(DateTimeUtc.Now));
                yield return new TestCaseData(
                    Variant.From(Uuid.NewUuid()));
                yield return new TestCaseData(
                    Variant.From((float)5));
                yield return new TestCaseData(
                    Variant.From((double)5));
                yield return new TestCaseData(
                    Variant.From(float.NaN));
                yield return new TestCaseData(
                    Variant.From(double.NaN));
                yield return new TestCaseData(
                    Variant.From((byte)5));
                yield return new TestCaseData(
                    Variant.From((sbyte)-5));
                yield return new TestCaseData(
                    Variant.From(-335));
                yield return new TestCaseData(
                    Variant.From((short)335));
                yield return new TestCaseData(
                    Variant.From((ushort)335));
                yield return new TestCaseData(
                    Variant.From((uint)335));
                yield return new TestCaseData(
                    Variant.From((long)3434335));
                yield return new TestCaseData(
                    Variant.From((ulong)33532342343));
                yield return new TestCaseData(
                    Variant.From("test"));
                yield return new TestCaseData(
                    Variant.From((XmlElement)"<test/>"));
                yield return new TestCaseData(
                    Variant.From((ByteString)"test"u8));
                yield return new TestCaseData(
                    Variant.From(new ExtensionObject(new NodeId("Test", 0), (ByteString)"test"u8)));
                yield return new TestCaseData(
                    Variant.From([true, true, false, true, false, true]));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        StatusCodes.Bad,
                        StatusCodes.Good
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new NodeId(1),
                        new NodeId(Uuid.NewUuid())
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new ExpandedNodeId(1),
                        new ExpandedNodeId("test", 0)
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new QualifiedName("test", 1),
                        new QualifiedName("test", 2)
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new LocalizedText("test1"),
                        new LocalizedText("test2")
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new DataValue(Variant.From("test")),
                        new DataValue(Variant.From(1))
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        DateTimeUtc.Now,
                        DateTimeUtc.Now,
                        DateTimeUtc.Now
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        Uuid.NewUuid(),
                        Uuid.NewUuid(),
                        Uuid.NewUuid()
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        5,
                        float.NaN }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        5,
                        double.NaN
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (byte)1, (byte)2, (byte)3, (byte)4, (byte)5
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (sbyte)-5,
                        (sbyte)5
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        -335,
                        323423
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (short)335,
                        (short)-335
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (ushort)335,
                        (ushort)3222
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (uint)335,
                        (uint)232
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        3434335,
                        (long)33
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (ulong)33532342343,
                        (ulong)235234
                    }));
                yield return new TestCaseData(
                    Variant.From(["test", "test2"]));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (XmlElement)"<test/>",
                        (XmlElement)"<test2/>"
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        (ByteString)"test1"u8,
                        (ByteString)"test2"u8
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        Variant.From((long)3434335),
                        Variant.From((ByteString)"test"u8)
                    }));
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                        new ExtensionObject(new NodeId("Test", 0), (ByteString)"test"u8),
                        ExtensionObject.Null
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { true, true, false },
                        { true, false, true }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { StatusCodes.Bad },
                        { StatusCodes.Good }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new NodeId(1) },
                        { new NodeId(Uuid.NewUuid()) }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new ExpandedNodeId(1) },
                        { new ExpandedNodeId("test", 0) }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new QualifiedName("test", 1) },
                        { new QualifiedName("test", 2) }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new LocalizedText("test1") },
                        { new LocalizedText("test2") } }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new DataValue(Variant.From("test")) },
                        { new DataValue(Variant.From(1)) }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { DateTimeUtc.Now, DateTimeUtc.Now },
                        { DateTimeUtc.Now, DateTimeUtc.Now }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { Uuid.NewUuid(), Uuid.NewUuid() },
                        { Uuid.NewUuid(), Uuid.NewUuid() }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { float.PositiveInfinity, 5 },
                        { 5, float.NaN }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { double.NegativeInfinity, 5 },
                        { 5, double.NaN }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (byte)1, (byte)2, (byte)3 },
                        { (byte)4, (byte)5, (byte)6 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (sbyte)-5 },
                        { (sbyte)5 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { -335 },
                        { 323423 } }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (short)335 },
                        { (short)-335 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (ushort)335 },
                        { (ushort)3222 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (uint)335 },
                        { (uint)232 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { 3434335 },
                        { long.MaxValue }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (ulong)33532342343 },
                        { ulong.MaxValue }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { "test" },
                        { "test2" }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (XmlElement)"<test/>" },
                        { (XmlElement)"<test2/>" }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { (ByteString)"test1"u8 },
                        { (ByteString)"test2"u8 }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { Variant.From((long)3434335) },
                        { Variant.From((ByteString)"test"u8) }
                    }));
                yield return new TestCaseData(
                    Variant.From(new[,]
                    {
                        { new ExtensionObject(new NodeId("Test", 0), (ByteString)"test"u8) },
                        { ExtensionObject.Null }
                    }));
            }
        }

        public static IEnumerable VariantValues
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(true), 8);
                yield return new TestCaseData(
                    Variant.From(StatusCodes.Bad), 8);
                yield return new TestCaseData(
                    Variant.From(new NodeId(1)), 8);
                yield return new TestCaseData(
                    Variant.From(new ExpandedNodeId(1)), 8);
                yield return new TestCaseData(
                    Variant.From(new QualifiedName("test")), 8);
                yield return new TestCaseData(
                    Variant.From(new LocalizedText("test")), 8);
                yield return new TestCaseData(
                    Variant.From(new DataValue(Variant.From("test"))), 8);
                yield return new TestCaseData(
                    Variant.From(DateTimeUtc.Now), 8);
                yield return new TestCaseData(
                    Variant.From(Uuid.NewUuid()), 8);
                yield return new TestCaseData(
                    Variant.From((float)5), 8);
                yield return new TestCaseData(
                    Variant.From((double)5), 8);
                yield return new TestCaseData(
                    Variant.From((byte)5), 8);
                yield return new TestCaseData(
                    Variant.From((sbyte)-5), 5);
                yield return new TestCaseData(
                    Variant.From(-335), 33);
                yield return new TestCaseData(
                    Variant.From((short)335), 8);
                yield return new TestCaseData(
                    Variant.From((ushort)335), 83);
                yield return new TestCaseData(
                    Variant.From((uint)335), 4);
                yield return new TestCaseData(
                    Variant.From((long)3434335), 555);
                yield return new TestCaseData(
                    Variant.From((ulong)33532342343), 2);
                yield return new TestCaseData(
                    Variant.From("test"), 83);
                yield return new TestCaseData(
                    Variant.From((ByteString)"test"u8), 83);
                yield return new TestCaseData(
                    Variant.From(new int[,]
                    {
                        { 1, 2, 3 },
                        { 2, 4, 5 }
                    }), 4);
                yield return new TestCaseData(
                    Variant.From(new string[,,]
                    {
                        {
                            { string.Empty, string.Empty },
                            { string.Empty, string.Empty }
                        }
                    }), 2);
            }
        }

        public static IEnumerable XmlElements
        {
            get
            {
                yield return new TestCaseData(
                    XmlElement.Empty);
                yield return new TestCaseData(
                    SerializeXml(new Argument()));
            }
        }

        public static IEnumerable XmlElementValues
        {
            get
            {
                yield return new TestCaseData(
                    XmlElement.Empty, 0);
                yield return new TestCaseData(
                    XmlElement.Empty, 4);
                yield return new TestCaseData(
                    SerializeXml(new Argument()), 100);
            }
        }

        public static IEnumerable ScalarVariants
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(true));
                yield return new TestCaseData(
                    Variant.From((sbyte)-42));
                yield return new TestCaseData(
                    Variant.From((byte)255));
                yield return new TestCaseData(
                    Variant.From((short)-1234));
                yield return new TestCaseData(
                    Variant.From((ushort)65535));
                yield return new TestCaseData(
                    Variant.From(123456));
                yield return new TestCaseData(
                    Variant.From(123456u));
                yield return new TestCaseData(
                    Variant.From(123456789L));
                yield return new TestCaseData(
                    Variant.From(123456789uL));
                yield return new TestCaseData(
                    Variant.From(3.14f));
                yield return new TestCaseData(
                    Variant.From(2.718));
                yield return new TestCaseData(
                    Variant.From("hello"));
                yield return new TestCaseData(
                    Variant.From(new DateTimeUtc(2024, 1, 1, 0, 0, 0)));
                yield return new TestCaseData(
                    Variant.From(new Uuid(Guid.Empty)));
                yield return new TestCaseData(
                    Variant.From(new NodeId(1)));
                yield return new TestCaseData(
                    Variant.From(new ExpandedNodeId(1)));
                yield return new TestCaseData(
                    Variant.From(new QualifiedName("q")));
                yield return new TestCaseData(
                    Variant.From(new LocalizedText("en", "t")));
                yield return new TestCaseData(
                    Variant.From(new ExtensionObject(ExpandedNodeId.Null)));
                yield return new TestCaseData(
                    Variant.From(new DataValue(Variant.From(1))));
                yield return new TestCaseData(
                    Variant.From(ByteString.From([1, 2])));
                yield return new TestCaseData(
                    Variant.From(TestEnum.Value1));
                yield return new TestCaseData(
                    Variant.From(new StatusCode(0x80010000u)));
            }
        }

        public static IEnumerable ArrayVariants
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(s_booleanArray));
                yield return new TestCaseData(
                    Variant.From(new sbyte[] { 1, -1 }));
                yield return new TestCaseData(
                    Variant.From(new byte[] { 1, 2 }));
                yield return new TestCaseData(
                    Variant.From(new short[] { 1, -1 }));
                yield return new TestCaseData(
                    Variant.From(new ushort[] { 1, 2 }));
                yield return new TestCaseData(
                    Variant.From(new int[] { 1, -1 }));
                yield return new TestCaseData(
                    Variant.From(new uint[] { 1, 2 }));
                yield return new TestCaseData(
                    Variant.From(new long[] { 1, -1 }));
                yield return new TestCaseData(
                    Variant.From(new ulong[] { 1, 2 }));
                yield return new TestCaseData(
                    Variant.From(s_floatArray));
                yield return new TestCaseData(
                    Variant.From(s_doubleArray));
                yield return new TestCaseData(
                    Variant.From(s_stringArray));
                yield return new TestCaseData(
                    Variant.From([new DateTimeUtc(2024, 1, 1, 0, 0, 0)]));
                yield return new TestCaseData(
                    Variant.From([new Uuid(Guid.Empty)]));
                yield return new TestCaseData(
                    Variant.From([new NodeId(1)]));
                yield return new TestCaseData(
                    Variant.From([new ExpandedNodeId(1)]));
                yield return new TestCaseData(
                    Variant.From([StatusCodes.Good]));
                yield return new TestCaseData(
                    Variant.From([new QualifiedName("q")]));
                yield return new TestCaseData(
                    Variant.From([new LocalizedText("en", "t")]));
                yield return new TestCaseData(
                    Variant.From([new ExtensionObject(ExpandedNodeId.Null)]));
                yield return new TestCaseData(
                    Variant.From([new DataValue(Variant.From(1))]));
                yield return new TestCaseData(
                    Variant.From([Variant.From(1)]));
                yield return new TestCaseData(
                    Variant.From([ByteString.From([1, 2])]));
                yield return new TestCaseData(
                    Variant.From([TestEnum.Value1, TestEnum.Value2]));
            }
        }

        public static IEnumerable MatrixVariants
        {
            get
            {
                yield return new TestCaseData(
                    Variant.From(s_booleanArray.ToMatrixOf(2, 2)),
                    BuiltInType.Boolean);
                yield return new TestCaseData(
                    Variant.From(new sbyte[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                    BuiltInType.SByte);
                yield return new TestCaseData(
                    Variant.From(new byte[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                    BuiltInType.Byte);
                yield return new TestCaseData(
                    Variant.From(new short[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                    BuiltInType.Int16);
                yield return new TestCaseData(
                    Variant.From(new ushort[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                    BuiltInType.UInt16);
                yield return new TestCaseData(
                    Variant.From(new int[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                    BuiltInType.Int32);
                yield return new TestCaseData(
                    Variant.From(new uint[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                    BuiltInType.UInt32);
                yield return new TestCaseData(
                    Variant.From(new long[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                    BuiltInType.Int64);
                yield return new TestCaseData(
                    Variant.From(new ulong[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                    BuiltInType.UInt64);
                yield return new TestCaseData(
                    Variant.From(s_floatArray.ToMatrixOf(2, 2)),
                    BuiltInType.Float);
                yield return new TestCaseData(
                    Variant.From(s_doubleArray.ToMatrixOf(2, 2)),
                    BuiltInType.Double);
                yield return new TestCaseData(
                    Variant.From(s_stringArray.ToMatrixOf(2, 2)),
                    BuiltInType.String);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                    new DateTimeUtc(2024, 1, 2, 0, 0, 0),
                    new DateTimeUtc(2024, 1, 3, 0, 0, 0),
                    new DateTimeUtc(2024, 1, 4, 0, 0, 0)
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.DateTime);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    ByteString.From(new byte[] { 1 }),
                    ByteString.From(new byte[] { 2 }),
                    ByteString.From(new byte[] { 3 }),
                    ByteString.From(new byte[] { 4 })
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.ByteString);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    XmlElement.From("<a />"),
                    XmlElement.From("<b />"),
                    XmlElement.From("<c />"),
                    XmlElement.From("<d />")
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.XmlElement);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new NodeId(1),
                    new NodeId(2),
                    new NodeId(3),
                    new NodeId(4)
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.NodeId);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new ExpandedNodeId(1),
                    new ExpandedNodeId(2),
                    new ExpandedNodeId(3),
                    new ExpandedNodeId(4)
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.ExpandedNodeId);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    StatusCodes.Good,
                    StatusCodes.Bad,
                    StatusCodes.Good,
                    StatusCodes.Bad
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.StatusCode);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new QualifiedName("a"),
                    new QualifiedName("b"),
                    new QualifiedName("c"),
                    new QualifiedName("d")
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.QualifiedName);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new LocalizedText("en", "a"),
                    new LocalizedText("en", "b"),
                    new LocalizedText("en", "c"),
                    new LocalizedText("en", "d")
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.LocalizedText);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null)
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.ExtensionObject);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    new DataValue(Variant.From(1)),
                    new DataValue(Variant.From(2)),
                    new DataValue(Variant.From(3)),
                    new DataValue(Variant.From(4))
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.DataValue);
                yield return new TestCaseData(
                    Variant.From(new[]
                    {
                    Variant.From(1),
                    Variant.From(2),
                    Variant.From(3),
                    Variant.From(4)
                    }.ToMatrixOf(2, 2)),
                    BuiltInType.Variant);
                yield return new TestCaseData(
                    Variant.From(ArrayOf.Wrapped(
                        TestEnum.Value1,
                        TestEnum.Value2,
                        TestEnum.Value2,
                        TestEnum.Value3)
                    .ToMatrix(2, 2)),
                    BuiltInType.Int32);
            }
        }

        /// <summary>
        /// Creates a mock IEncodeable for testing.
        /// </summary>
        public static Mock<IEncodeable> CreateMockEncodeable()
        {
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(Guid.NewGuid());
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.TypeId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            return mockMessage;
        }

        /// <summary>
        /// Create from data contract
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static XmlElement SerializeXml<T>(T o)
        {
            var doc = new System.Xml.XmlDocument();
            XPathNavigator nav = doc.CreateNavigator();
            if (nav is null)
            {
                return XmlElement.Empty;
            }
            using (System.Xml.XmlWriter writer = nav.AppendChild())
            {
                new DataContractSerializer(typeof(T), new DataContractSerializerSettings
                {
                    SerializeReadOnlyTypes = true
                }).WriteObject(writer, o);
            }
            return doc.DocumentElement == null ? XmlElement.Empty
                : new XmlElement(doc.DocumentElement);
        }

        private static readonly bool[] s_booleanArray = [true, false, true, false];
        private static readonly double[] s_doubleArray = [1.0, 2.0, 3.0, 4.0];
        private static readonly string[] s_stringArray = ["a", "b", "c", "d"];
        private static readonly float[] s_floatArray = [1.0f, 2.0f, 3.0f, 4.0f];
    }

    /// <summary>
    /// Helper class to simulate a non-seekable stream for testing.
    /// </summary>
    internal sealed class NonSeekableMemoryStream : MemoryStream
    {
        public override bool CanSeek => m_canSeek;

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!m_canSeek)
            {
                throw new NotSupportedException("This stream does not support seeking.");
            }
            return base.Seek(offset, origin);
        }

        internal void ResetAndMakeSeekable()
        {
            m_canSeek = true;
            Position = 0;
        }

        private bool m_canSeek;
    }

    public sealed class TestEncodeable : IEncodeable
    {
        private static readonly ExpandedNodeId s_typeId = new(1, 0);
        private static readonly ExpandedNodeId s_binaryEncodingId = new(2, 0);
        private static readonly ExpandedNodeId s_xmlEncodingId = new(3, 0);

        public TestEncodeable()
            : this(0)
        {
        }

        public TestEncodeable(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public ExpandedNodeId TypeId => s_typeId;

        public ExpandedNodeId BinaryEncodingId => s_binaryEncodingId;

        public ExpandedNodeId XmlEncodingId => s_xmlEncodingId;

        public void Encode(IEncoder encoder)
        {
            encoder.WriteInt32(null, Value);
        }

        public void Decode(IDecoder decoder)
        {
            decoder.ReadInt32(null);
        }

        public bool IsEqual(IEncodeable encodeable)
        {
            return false;
        }

        public object Clone()
        {
            return new TestEncodeable(Value);
        }
    }

    public sealed class TestEncodeableType : EncodeableType<TestEncodeable>
    {
        public override System.Xml.XmlQualifiedName XmlName
            => new("TestEncodeable", Namespaces.OpcUaXsd);

        public override IEncodeable CreateInstance()
        {
            return new TestEncodeable();
        }
    }

    public enum TestByteEnum : byte
    {
        Zero = 0,
        One = 1,
        Max = byte.MaxValue
    }

    public enum TestSByteEnum : sbyte
    {
        Min = sbyte.MinValue,
        MinusOne = -1,
        Zero = 0,
        One = 1,
        Max = sbyte.MaxValue
    }

    public enum TestInt16Enum : short
    {
        Min = short.MinValue,
        MinusOne = -1,
        Zero = 0,
        One = 1,
        Max = short.MaxValue
    }

    public enum TestUInt16Enum : ushort
    {
        Zero = 0,
        One = 1,
        Max = ushort.MaxValue
    }

    public enum TestInt32Enum
    {
        Min = int.MinValue,
        MinusOne = -1,
        Zero = 0,
        One = 1,
        Max = int.MaxValue
    }

    public enum TestUInt32Enum : uint
    {
        Zero = 0,
        One = 1,
        Max = uint.MaxValue
    }

    public enum TestInt64Enum : long
    {
        Min = long.MinValue,
        MinusOne = -1,
        Zero = 0,
        One = 1,
        Max = long.MaxValue
    }

    public enum TestUInt64Enum : ulong
    {
        Zero = 0,
        One = 1,
        Max = ulong.MaxValue
    }

    [Flags]
    public enum TestFlagsEnum
    {
        None = 0,
        Flag1 = 1,
        Flag2 = 2,
        Flag3 = 4,
        Flag4 = 8,
        Combined = Flag1 | Flag2 | Flag3
    }

    public enum TestEnum
    {
        NegativeValue = -1,
        Zero = 0,
        Value1 = 1,
        Value2 = 2,
        Value3 = 3,
        LargeValue = 1000000
    }

    public enum TestNumericEnum
    {
        Item100 = 100,
        Item200 = 200
    }
}
