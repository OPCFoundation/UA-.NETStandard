/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

public partial class Testcases
{
    public delegate void MessageEncoder(IEncoder encoder);

    public static ServiceMessageContext MessageContext = ServiceMessageContext.GlobalContext;

    public static MessageEncoder[] MessageEncoders = new MessageEncoder[] {
        ReadRequest,
        ReadResponse,
    };

    public static void ReadRequest(IEncoder encoder)
    {
        var twoByteNodeIdNumeric = new NodeId(123);
        var nodeIdNumeric = new NodeId(4444, 2);
        var nodeIdString = new NodeId("ns=3;s=RevisionCounter");
        var nodeIdGuid = new NodeId(Guid.NewGuid());
        var nodeIdOpaque = new NodeId(new byte[] { 66, 22, 55, 44, 11 });
        var readRequest = new ReadRequest {
            RequestHeader = new RequestHeader {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = 10000,
                RequestHandle = 422,
                AdditionalHeader = new ExtensionObject(),
                ReturnDiagnostics = (uint)DiagnosticsMasks.All,
            },
            NodesToRead = new ReadValueIdCollection {
                new ReadValueId {
                    NodeId = twoByteNodeIdNumeric,
                    AttributeId = Attributes.UserRolePermissions,
                },
                new ReadValueId {
                    NodeId = nodeIdNumeric,
                    AttributeId = Attributes.Description,
                },
                new ReadValueId {
                    NodeId = nodeIdString,
                    AttributeId = Attributes.Value,
                    IndexRange = "1:2",
                },
                new ReadValueId {
                    NodeId = nodeIdGuid,
                    AttributeId = Attributes.DisplayName,
                },
                new ReadValueId {
                    NodeId = nodeIdNumeric,
                    AttributeId = Attributes.AccessLevel,
                },
                new ReadValueId {
                    NodeId = nodeIdOpaque,
                    AttributeId = Attributes.RolePermissions,
                },
            },
            MaxAge = 1000,
            TimestampsToReturn = TimestampsToReturn.Source,
        };
        encoder.EncodeMessage(readRequest);
    }

    public static void ReadResponse(IEncoder encoder)
    {
        var now = DateTime.UtcNow;
        var nodeId = new NodeId(1000);
        var matrix = new byte[2, 2, 2] { { { 1, 2 }, { 3, 4 } }, { { 11, 22 }, { 33, 44 } } };
        var readRequest = new ReadResponse {
            Results = new DataValueCollection {
                    new DataValue {
                        Value = new Variant("Hello World"),
                        ServerTimestamp = now,
                        SourceTimestamp = now.AddMinutes(1),
                        ServerPicoseconds = 100,
                        SourcePicoseconds = 10,
                        StatusCode = StatusCodes.Good,
                    },
                    new DataValue {
                        Value = new Variant((uint)12345678),
                        ServerTimestamp = now,
                        SourceTimestamp = now.AddMinutes(1),
                        StatusCode = StatusCodes.BadDataLost,
                    },
                    new DataValue {
                        Value = new Variant(new byte[] { 0,1,2,3,4,5,6 }),
                        ServerTimestamp = now,
                        SourceTimestamp = now.AddMinutes(1),
                        StatusCode = StatusCodes.Good,
                    },
                    new DataValue {
                        Value = new Variant((byte)42),
                        SourceTimestamp = now,
                    },
                    new DataValue {
                        Value = new Variant(new Matrix(matrix, BuiltInType.Byte)),
                        ServerTimestamp = now,
                    },

                },
            DiagnosticInfos = new DiagnosticInfoCollection {
                        new DiagnosticInfo {
                            AdditionalInfo = "Hello World",
                            InnerStatusCode = StatusCodes.BadCertificateHostNameInvalid,
                            InnerDiagnosticInfo = new DiagnosticInfo {
                                AdditionalInfo = "Hello World",
                                InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                            },
                        },
                    },
            ResponseHeader = new ResponseHeader {
                Timestamp = DateTime.UtcNow,
                RequestHandle = 42,
                ServiceResult = StatusCodes.Good,
                ServiceDiagnostics = new DiagnosticInfo {
                    AdditionalInfo = "NodeId not found",
                    InnerStatusCode = StatusCodes.BadAggregateConfigurationRejected,
                    InnerDiagnosticInfo = new DiagnosticInfo {
                        AdditionalInfo = "Hello World",
                        InnerStatusCode = StatusCodes.BadIndexRangeInvalid,
                        InnerDiagnosticInfo = new DiagnosticInfo {
                            AdditionalInfo = "Hello World",
                            InnerStatusCode = StatusCodes.BadSecureChannelIdInvalid,
                            InnerDiagnosticInfo = new DiagnosticInfo {
                                AdditionalInfo = "Hello World",
                                InnerStatusCode = StatusCodes.BadAlreadyExists,
                            },
                        },
                    },
                },
            },
        };
        encoder.EncodeMessage(readRequest);
    }
}
