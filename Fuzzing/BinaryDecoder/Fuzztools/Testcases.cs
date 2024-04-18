
using System;
using System.IO;
using BinaryDecoder.Fuzz;
using Opc.Ua;

namespace BinaryDecoder.Fuzztools
{

    public static class Testcases
    {
        private static ServiceMessageContext s_messageContext = new ServiceMessageContext();

        private static byte[] CreateReadRequest()
        {
            using (var encoder = new BinaryEncoder(s_messageContext))
            {
                var nodeId = new NodeId(1000);
                var readRequest = new ReadRequest {
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
                return encoder.CloseAndReturnBuffer();
            }
        }

        private static byte[] CreateReadResponse()
        {
            var now = DateTime.UtcNow;
            using (var encoder = new BinaryEncoder(s_messageContext))
            {
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
                        Value = new Variant(new byte[] { 0,1,2,3,4,5,6}),
                        ServerTimestamp = now,
                        SourceTimestamp = now.AddMinutes(1),
                        StatusCode = StatusCodes.Good,
                    },
                    new DataValue {
                        Value = new Variant((byte)42),
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
                return encoder.CloseAndReturnBuffer();
            }
        }

        public static void Run(string directoryPath)
        {
            var readRequest = CreateReadRequest();
            FuzzTestcase(readRequest);
            File.WriteAllBytes(Path.Combine(directoryPath, "readrequest.bin"), readRequest);
            var readResponse = CreateReadResponse();
            FuzzTestcase(readResponse);
            File.WriteAllBytes(Path.Combine(directoryPath, "readresponse.bin"), readResponse);
        }

        public static void FuzzTestcase(byte[] message)
        {
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.FuzzTarget(stream);
            }
        }
    }

#if mist
    public static class FuzzableCode
    {
        //public static void FuzzTargetMethod(ReadOnlySpan<byte> input)
        public static void FuzzTargetMethod(byte[] input)
        {
            try
            {
                var messageContext = new ServiceMessageContext();
                using (var decoder = new BinaryDecoder(input, messageContext))
                {
                    decoder.DecodeMessage(null);
                }
            }
            catch (Exception ex) when (ex is ServiceResultException)
            {
                // This is an example. You should filter out any
                // *expected* exception(s) from your code here,
                // but it’s an anti-pattern to catch *all* Exceptions,
                // as you might suppress legitimate problems, such as 
                // your code throwing a NullReferenceException.
            }
        }
    }
#endif
}
