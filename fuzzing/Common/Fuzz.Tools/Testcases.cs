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
using System.IO;
using System.Linq;

namespace Opc.Ua.Fuzzing
{
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable RCS1043
    public static partial class Testcases
#pragma warning restore RCS1043, IDE0079
    {
        public delegate void MessageEncoder(IEncoder encoder);

        internal static DateTimeUtc SeedTimestamp { get; } = new(2025, 1, 2, 3, 4, 5);

        public static string[] DiscoverTestcaseEncoderSuffixes(string testcasesRoot)
        {
            if (string.IsNullOrEmpty(testcasesRoot))
            {
                return [];
            }

            string rootName = Path.GetFileName(
                testcasesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string parent = Path.GetDirectoryName(testcasesRoot);
            var directories = new List<string>();

            if (Directory.Exists(testcasesRoot))
            {
                directories.AddRange(Directory.EnumerateDirectories(testcasesRoot, "Testcases.*"));
            }

            if (parent != null && Directory.Exists(parent))
            {
                directories.AddRange(Directory.EnumerateDirectories(parent, rootName + ".*"));
            }

            return
            [
                .. directories
                    .Select(Path.GetFileName)
                    .Where(name => name.StartsWith(rootName + ".", StringComparison.OrdinalIgnoreCase))
                    .Select(name => name[rootName.Length..])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(suffix => suffix, StringComparer.OrdinalIgnoreCase)
            ];
        }

        /// <summary>
        /// Encodes the deterministic canonical ReadRequest corpus message.
        /// </summary>
        public static void ReadRequest(IEncoder encoder)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }
            encoder.EncodeMessage(CreateRichReadRequest());
        }

        /// <summary>
        /// Encodes the deterministic canonical ReadResponse corpus message.
        /// </summary>
        public static void ReadResponse(IEncoder encoder)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }
            encoder.EncodeMessage(CreateRichReadResponse());
        }

        /// <summary>
        /// Encodes the deterministic canonical PublishResponse corpus message.
        /// </summary>
        public static void PublishResponse(IEncoder encoder)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }
            encoder.EncodeMessage(CreatePublishResponse());
        }

        /// <summary>
        /// Encodes the deterministic canonical WriteRequest corpus message.
        /// </summary>
        public static void WriteRequest(IEncoder encoder)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }
            encoder.EncodeMessage(CreateRichWriteRequest());
        }

        internal static ReadRequest CreateRichReadRequest()
        {
            return new ReadRequest
            {
                RequestHeader = CreateRichRequestHeader(true),
                MaxAge = 1000,
                TimestampsToReturn = TimestampsToReturn.Source,
                NodesToRead =
                [
                    new ReadValueId { NodeId = new NodeId(123), AttributeId = Attributes.UserRolePermissions },
                    new ReadValueId { NodeId = new NodeId(4444, 2), AttributeId = Attributes.Description },
                    new ReadValueId
                    {
                        NodeId = new NodeId("RevisionCounter", 3),
                        AttributeId = Attributes.Value,
                        IndexRange = "1:2",
                        DataEncoding = new QualifiedName("Default Binary")
                    },
                    new ReadValueId
                    {
                        NodeId = new NodeId(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 1),
                        AttributeId = Attributes.DisplayName
                    },
                    new ReadValueId { NodeId = new NodeId(4444, 2), AttributeId = Attributes.AccessLevel },
                    new ReadValueId
                    {
                        NodeId = new NodeId(ByteString.From([66, 22, 55, 44, 11]), 4),
                        AttributeId = Attributes.RolePermissions
                    }
                ]
            };
        }

        internal static ReadResponse CreateRichReadResponse()
        {
            MatrixOf<byte> matrix = new byte[2, 2, 2]
            {
                { { 1, 2 }, { 3, 4 } },
                { { 11, 22 }, { 33, 44 } }
            };
            ArrayOf<ExtensionObject> vectors =
            [
                new ExtensionObject(new ThreeDVector { X = 1, Y = -1, Z = 0 }),
                new ExtensionObject(new ThreeDVector { X = 2, Y = -2, Z = 1 })
            ];
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = SeedTimestamp,
                    RequestHandle = 42,
                    AdditionalHeader = CreateAdditionalHeader(),
                    ServiceResult = StatusCodes.Good,
                    StringTable = ["Hello", "World", "Goodbye"],
                    ServiceDiagnostics = new DiagnosticInfo
                    {
                        SymbolicId = 0,
                        NamespaceUri = 1,
                        LocalizedText = 2,
                        AdditionalInfo = "NodeId not found",
                        InnerStatusCode = StatusCodes.BadAggregateConfigurationRejected,
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            AdditionalInfo = "Invalid index range",
                            InnerStatusCode = StatusCodes.BadIndexRangeInvalid,
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                AdditionalInfo = "Invalid channel",
                                InnerStatusCode = StatusCodes.BadSecureChannelIdInvalid,
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    AdditionalInfo = "Already exists",
                                    InnerStatusCode = StatusCodes.BadAlreadyExists
                                }
                            }
                        }
                    }
                },
                Results =
                [
                    CreateSeedValue(Variant.From("Hello World")),
                    CreateSeedValue(Variant.From(12345678u), StatusCodes.BadDataLost)
                        .WithServerTimestamp(new DateTimeUtc(2025, 1, 2)),
                    CreateSeedValue(Variant.From(ByteString.From([0, 1, 2, 3, 4, 5, 6])))
                        .WithServerTimestamp(DateTimeUtc.MaxValue),
                    new DataValue(Variant.From((byte)42), StatusCodes.Good, SeedTimestamp),
                    new DataValue(Variant.From((ulong)0xbadbeef), StatusCodes.Good, SeedTimestamp),
                    CreateSeedValue(Variant.From(matrix)),
                    CreateSeedValue(Variant.From(2025.111), StatusCodes.BadTooManyOperations),
                    CreateSeedValue(
                        Variant.From(new LocalizedText("en-us", "The text")),
                        StatusCodes.BadTooManyOperations),
                    CreateSeedValue(
                        Variant.From(new QualifiedName("The text", 2)),
                        StatusCodes.BadTooManyOperations),
                    CreateSeedValue(Variant.From(new ExtensionObject(new ThreeDVector { X = 1, Y = -1, Z = 0 }))),
                    CreateSeedValue(Variant.From(vectors))
                ],
                DiagnosticInfos = [CreateNestedDiagnosticInfo()]
            };
        }

        internal static PublishResponse CreatePublishResponse()
        {
            MatrixOf<uint> matrix = new uint[2, 2, 2]
            {
                { { 1, 2 }, { 3, 4 } },
                { { 11, 22 }, { 33, 44 } }
            };
            ArrayOf<uint> array = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0];
            return new PublishResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = SeedTimestamp,
                    RequestHandle = 42,
                    ServiceResult = StatusCodes.Good,
                    StringTable = ["No error occurred"]
                },
                SubscriptionId = 1234,
                AvailableSequenceNumbers = [1, 2, 3, 4],
                MoreNotifications = true,
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = 123456,
                    PublishTime = SeedTimestamp,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems =
                            [
                                CreateNotification(122, Variant.From("Hello World")),
                                CreateNotification(123, Variant.From(matrix)),
                                CreateNotification(124, Variant.From(new NodeId(1000, 2))),
                                CreateNotification(125, Variant.From(new NodeId("Counter", 1))),
                                CreateNotification(126, Variant.From(true)),
                                CreateNotification(127, Variant.From((byte)123)),
                                CreateNotification(128, Variant.From(123.123f)),
                                CreateNotification(129, Variant.From(12301232.123)),
                                CreateNotification(130, Variant.From(-123012321234123L)),
                                CreateNotification(131, Variant.From(123012321234123UL)),
                                CreateNotification(132, Variant.From(array))
                            ],
                            DiagnosticInfos = [CreateNestedDiagnosticInfo()]
                        })
                    ]
                },
                Results = [StatusCodes.Good, StatusCodes.BadSequenceNumberUnknown],
                DiagnosticInfos = [CreateNestedDiagnosticInfo()]
            };
        }

        internal static WriteRequest CreateRichWriteRequest()
        {
            ArrayOf<int> integers = [1, 2, 3, 4, 5];
            ArrayOf<float> singles = [1.1f, 2.2f, 3.3f];
            ArrayOf<double> doubles = [1.1, 2.2, 3.3];
            ArrayOf<string> strings = ["one", "two", "three"];
            return new WriteRequest
            {
                RequestHeader = CreateRichRequestHeader(false),
                NodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = new NodeId(123),
                        AttributeId = Attributes.Value,
                        IndexRange = "1:2",
                        Value = CreateSeedValue(Variant.From("Hello World"))
                    },
                    new WriteValue
                    {
                        NodeId = new NodeId(124),
                        AttributeId = Attributes.ValueRank,
                        Value = CreateSeedValue(Variant.From(12345))
                    },
                    CreateWriteValue(new NodeId(125), Variant.From(123.45f)),
                    CreateWriteValue(new NodeId(126), Variant.From(123.45)),
                    CreateWriteValue(new NodeId(127), Variant.From(true)),
                    CreateWriteValue(new NodeId(128), Variant.From(ByteString.From([1, 2, 3, 4, 5]))),
                    CreateWriteValue(new NodeId("FastCounter", 2), Variant.From(integers)),
                    CreateWriteValue(
                        new NodeId(ByteString.From([0xaa, 0xbb, 0xcc, 0xdd, 0xee]), 3),
                        Variant.From(singles)),
                    CreateWriteValue(
                        new NodeId(new Guid("10213243-5465-7687-98a9-bacbdcedfe0f"), 1),
                        Variant.From(doubles)),
                    CreateWriteValue(new NodeId(132, 3), Variant.From(strings))
                ]
            };
        }

        private static RequestHeader CreateRichRequestHeader(bool additionalHeader)
        {
            return new RequestHeader
            {
                Timestamp = SeedTimestamp,
                TimeoutHint = 10000,
                RequestHandle = 422,
                ReturnDiagnostics = (uint)DiagnosticsMasks.All,
                AdditionalHeader = additionalHeader ? CreateAdditionalHeader() : ExtensionObject.Null
            };
        }

        private static ExtensionObject CreateAdditionalHeader()
        {
            return new ExtensionObject(new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = new QualifiedName("traceparent"),
                        Value = Variant.From("00-00112233445566778899aabbccddeeff-0123456789abcdef-01")
                    }
                ]
            });
        }

        private static DiagnosticInfo CreateNestedDiagnosticInfo()
        {
            return new DiagnosticInfo
            {
                AdditionalInfo = "Hello World",
                InnerStatusCode = StatusCodes.BadCertificateHostNameInvalid,
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    AdditionalInfo = "Unknown node",
                    InnerStatusCode = StatusCodes.BadNodeIdUnknown
                }
            };
        }

        private static DataValue CreateSeedValue(in Variant value, StatusCode status = default)
        {
            return new DataValue(value, status, SeedTimestamp.AddMilliseconds(60000), SeedTimestamp, 10, 100);
        }

        private static MonitoredItemNotification CreateNotification(uint clientHandle, in Variant value)
        {
            return new MonitoredItemNotification { ClientHandle = clientHandle, Value = CreateSeedValue(value) };
        }

        private static WriteValue CreateWriteValue(NodeId nodeId, in Variant value)
        {
            return new WriteValue { NodeId = nodeId, AttributeId = Attributes.Value, Value = CreateSeedValue(value) };
        }

        public static readonly MessageEncoder[] MessageEncoders =
        [
            ReadRequest,
            ReadResponse,
            PublishResponse,
            WriteRequest
        ];
    }
}
