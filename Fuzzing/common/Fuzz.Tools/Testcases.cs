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
        var nodeId = new NodeId(1000);
        var readRequest = new ReadRequest {
            RequestHeader = new RequestHeader {
                Timestamp = DateTime.UtcNow,
                RequestHandle = 42,
                AdditionalHeader = new ExtensionObject(),
            },
            NodesToRead = new ReadValueIdCollection {
                new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.Description,
                },
                new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                },
                new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.DisplayName,
                },
                new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.AccessLevel,
                },
                new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.RolePermissions,
                },
            },
        };
        encoder.EncodeMessage(readRequest);
    }

    public static void ReadResponse(IEncoder encoder)
    {
        var now = DateTime.UtcNow;
        var nodeId = new NodeId(1000);
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
                    InnerStatusCode = StatusCodes.BadNodeIdExists,
                    InnerDiagnosticInfo = new DiagnosticInfo {
                        AdditionalInfo = "Hello World",
                        InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                        InnerDiagnosticInfo = new DiagnosticInfo {
                            AdditionalInfo = "Hello World",
                            InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                            InnerDiagnosticInfo = new DiagnosticInfo {
                                AdditionalInfo = "Hello World",
                                InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                            },
                        },
                    },
                },
            },
        };
        encoder.EncodeMessage(readRequest);
    }
}
