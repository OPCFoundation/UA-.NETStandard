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
using System.Text;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        public enum TestCaseEncoders
        {
            Binary = 0,
            Json = 1,
            Xml = 2
        }

        /// <summary>
        /// Run the encoder test cases
        /// </summary>
        /// <param name="workPath"></param>
        /// <param name="telemetry">Unused; retained for the common fuzz-tool signature.</param>
        public static void Run(string workPath, ITelemetryContext telemetry)
        {
            _ = telemetry;
            string pathSuffix = GetTestcaseEncoderSuffix(TestCaseEncoders.Binary);
            string pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(pathTarget);
            foreach (MessageEncoder messageEncoder in GetMessageEncoders())
            {
                byte[] message;
                using (var encoder = new BinaryEncoder(FuzzableCode.MessageContext))
                {
                    messageEncoder(encoder);
                    message = encoder.CloseAndReturnBuffer();
                }

                // Test the fuzz targets with the message.
                FuzzableCode.LibfuzzBinaryDecoder(message);
                FuzzableCode.LibfuzzBinaryEncoder(message);
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.AflfuzzBinaryDecoder(stream);
                }
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.AflfuzzBinaryEncoder(stream);
                }
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.FuzzBinaryDecoderCore(stream, true);
                }

                string fileName = Path.Combine(
                    pathTarget,
                    $"{messageEncoder.Method.Name}.bin".ToLowerInvariant());
                File.WriteAllBytes(fileName, message);
            }

            // Create the Testcases for the json decoder.
            pathSuffix = GetTestcaseEncoderSuffix(TestCaseEncoders.Json);
            pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(pathTarget);
            foreach (MessageEncoder messageEncoder in GetMessageEncoders())
            {
                byte[] message;
                using (var memoryStream = new MemoryStream(0x1000))
                using (var encoder = new JsonEncoder(memoryStream, FuzzableCode.MessageContext))
                {
                    messageEncoder(encoder);
                    encoder.Close();
                    message = memoryStream.ToArray();
                }

                // Test the fuzz targets with the message.
                FuzzableCode.LibfuzzJsonDecoder(message);
                FuzzableCode.LibfuzzJsonEncoder(message);
                string json = Encoding.UTF8.GetString(message);
                FuzzableCode.AflfuzzJsonDecoder(json);
                FuzzableCode.AflfuzzJsonEncoder(json);
                FuzzableCode.FuzzJsonDecoderCore(json);

                string fileName = Path.Combine(
                    pathTarget,
                    $"{messageEncoder.Method.Name}.json".ToLowerInvariant());
                File.WriteAllBytes(fileName, message);
            }

            // Create the Testcases for the xml decoder.
            pathSuffix = GetTestcaseEncoderSuffix(TestCaseEncoders.Xml);
            pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(pathTarget);
            foreach (MessageEncoder messageEncoder in GetMessageEncoders())
            {
                string xml;
                using (var encoder = new XmlEncoder(FuzzableCode.MessageContext))
                {
                    encoder.SetMappingTables(
                        FuzzableCode.MessageContext.NamespaceUris,
                        FuzzableCode.MessageContext.ServerUris);
                    messageEncoder(encoder);
                    xml = encoder.CloseAndReturnText();
                }

                // Test the fuzz targets with the message.
                byte[] message = Encoding.UTF8.GetBytes(xml);
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.AflfuzzXmlDecoder(stream);
                }
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.AflfuzzXmlEncoder(stream);
                }
                using (var stream = new MemoryStream(message))
                {
                    FuzzableCode.FuzzXmlDecoderCore(stream);
                }
                FuzzableCode.LibfuzzXmlDecoder(message);
                FuzzableCode.LibfuzzXmlEncoder(message);

                string fileName = Path.Combine(
                    pathTarget,
                    $"{messageEncoder.Method.Name}.xml".ToLowerInvariant());
                File.WriteAllBytes(fileName, Encoding.UTF8.GetBytes(xml));
            }

            WriteBuiltInTypeTestcases(workPath);
            WriteParserTestcases(workPath);
        }

        private static string GetTestcaseEncoderSuffix(TestCaseEncoders encoder)
        {
            return "." + encoder;
        }

        private delegate void BuiltInEncoder(IEncoder encoder);

        private static IEnumerable<MessageEncoder> GetMessageEncoders()
        {
            foreach (MessageEncoder messageEncoder in MessageEncoders)
            {
                yield return messageEncoder;
            }

            yield return BrowseRequest;
            yield return WriteRequest;
            yield return DataTypeNodeMessage;
            yield return VariableNodeMessage;
        }

        private static void BrowseRequest(IEncoder encoder)
        {
            var browseRequest = new BrowseRequest
            {
                RequestHeader = CreateRequestHeader(),
                RequestedMaxReferencesPerNode = 10,
                NodesToBrowse =
                [
                    new BrowseDescription
                    {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ]
            };

            encoder.EncodeMessage(browseRequest);
        }

        private static void WriteRequest(IEncoder encoder)
        {
            var writeRequest = new WriteRequest
            {
                RequestHeader = CreateRequestHeader(),
                NodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = new NodeId(1000, 2),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            Variant.From(new LocalizedText("en-US", "Hello World")),
                            StatusCodes.Good,
                            DateTimeUtc.Now)
                    }
                ]
            };

            encoder.EncodeMessage(writeRequest);
        }

        private static RequestHeader CreateRequestHeader()
        {
            return new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = 42,
                AdditionalHeader = new ExtensionObject(new ReadValueId
                {
                    NodeId = new NodeId(2253),
                    AttributeId = Attributes.Value
                })
            };
        }

        // The two Node seeds below are permanent regression coverage for
        // https://github.com/OPCFoundation/UA-.NETStandard/issues/3546 — they
        // populate the fields (References, RolePermissions, DataTypeDefinition,
        // multi-dim Value Variant) that the original failure-pattern exercises.
        // Keeping them as seeds means FuzzGoodTestcases (the assertive theory
        // in FuzzTargetTestsBase) covers the IsEqual round-trip end-to-end on
        // every run, not only when the libfuzz pipeline happens to mutate
        // into these shapes.

        private static void DataTypeNodeMessage(IEncoder encoder)
        {
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1234, 2),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("Issue3546DataType", 2),
                DisplayName = new LocalizedText("en", "Issue 3546 DataType"),
                Description = new LocalizedText("en", "Round-trip regression seed"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(
                    new StructureDefinition
                    {
                        DefaultEncodingId = new NodeId(5678, 2),
                        BaseDataType = DataTypeIds.Structure,
                        StructureType = StructureType.Structure,
                        Fields =
                        [
                            new StructureField
                            {
                                Name = "Field0",
                                DataType = DataTypeIds.UInt32,
                                ValueRank = ValueRanks.Scalar,
                                IsOptional = false
                            },
                            new StructureField
                            {
                                Name = "Field1",
                                DataType = DataTypeIds.String,
                                ValueRank = ValueRanks.Scalar,
                                IsOptional = false
                            }
                        ]
                    }),
                References =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IsInverse = true,
                        TargetId = (ExpandedNodeId)DataTypeIds.Structure
                    },
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                        IsInverse = false,
                        TargetId = new ExpandedNodeId(new NodeId(5678, 2))
                    }
                ],
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.Read
                    }
                ],
                UserRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };

            encoder.EncodeMessage(dataTypeNode);
        }

        private static void VariableNodeMessage(IEncoder encoder)
        {
            var variableNode = new VariableNode
            {
                NodeId = new NodeId(4321, 2),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Issue3546Variable", 2),
                DisplayName = new LocalizedText("en", "Issue 3546 Variable"),
                Description = new LocalizedText("en", "Round-trip regression seed"),
                Value = Variant.From(new ExtensionObject(
                    new Argument
                    {
                        Name = "Sample",
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar,
                        Description = new LocalizedText("en", "fuzz #3546 sample")
                    })),
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new ArrayOf<uint>(s_arrayDimensions),
                AccessLevel = AccessLevels.CurrentRead | AccessLevels.CurrentWrite,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 0,
                Historizing = false,
                AccessLevelEx = 0,
                References =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IsInverse = false,
                        TargetId = (ExpandedNodeId)VariableTypeIds.BaseDataVariableType
                    },
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IsInverse = true,
                        TargetId = (ExpandedNodeId)ObjectIds.ObjectsFolder
                    }
                ],
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Read
                    }
                ],
                UserRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };

            encoder.EncodeMessage(variableNode);
        }

        private static readonly uint[] s_arrayDimensions = [1u, 2u];

        private static void WriteBuiltInTypeTestcases(string workPath)
        {
            string builtInPath = workPath + ".BuiltInTypes" + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(builtInPath);

            foreach ((string name, BuiltInEncoder builtInEncoder) in GetBuiltInEncoders())
            {
                string binaryPath = Path.Combine(builtInPath, "Binary");
                Directory.CreateDirectory(binaryPath);
                using (var encoder = new BinaryEncoder(FuzzableCode.MessageContext))
                {
                    builtInEncoder(encoder);
                    File.WriteAllBytes(
                        Path.Combine(binaryPath, name + ".bin"),
                        encoder.CloseAndReturnBuffer() ?? []);
                }

                string jsonPath = Path.Combine(builtInPath, "Json");
                Directory.CreateDirectory(jsonPath);
                using (var memoryStream = new MemoryStream(0x1000))
                using (var encoder = new JsonEncoder(memoryStream, FuzzableCode.MessageContext))
                {
                    builtInEncoder(encoder);
                    encoder.Close();
                    File.WriteAllBytes(Path.Combine(jsonPath, name + ".json"), memoryStream.ToArray());
                }

                string xmlPath = Path.Combine(builtInPath, "Xml");
                Directory.CreateDirectory(xmlPath);
                File.WriteAllText(
                    Path.Combine(xmlPath, name + ".xml"),
                    GetXmlBuiltInSeed(name),
                    Encoding.UTF8);
            }
        }

        private static IEnumerable<(string Name, BuiltInEncoder Encoder)> GetBuiltInEncoders()
        {
            yield return ("nodeid", encoder => encoder.WriteNodeId("Value", new NodeId(1000, 2)));
            yield return ("expandednodeid", encoder => encoder.WriteExpandedNodeId(
                "Value",
                new ExpandedNodeId(new NodeId(1000, 2), "urn:example:namespace", 1)));
            yield return ("variant", encoder => encoder.WriteVariant(
                "Value",
                Variant.From(new LocalizedText("en-US", "Hello World"))));
            yield return ("extensionobject", encoder => encoder.WriteExtensionObject(
                "Value",
                new ExtensionObject(new ReadValueId
                {
                    NodeId = new NodeId(2253),
                    AttributeId = Attributes.Value
                })));
            yield return ("datavalue", encoder => encoder.WriteDataValue(
                "Value",
                new DataValue(
                    Variant.From(new QualifiedName("Temperature", 2)),
                    StatusCodes.Good,
                    DateTimeUtc.Now)));
            yield return ("diagnosticinfo", encoder => encoder.WriteDiagnosticInfo(
                "Value",
                new DiagnosticInfo
                {
                    AdditionalInfo = "seed diagnostic",
                    InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        AdditionalInfo = "inner diagnostic",
                        InnerStatusCode = StatusCodes.BadDecodingError
                    }
                }));
            yield return ("qualifiedname", encoder => encoder.WriteQualifiedName(
                "Value",
                new QualifiedName("Temperature", 2)));
            yield return ("localizedtext", encoder => encoder.WriteLocalizedText(
                "Value",
                new LocalizedText("en-US", "Hello World")));
        }

        private static string GetXmlBuiltInSeed(string name)
        {
            const string ns = " xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"";
            return name switch
            {
                "nodeid" => $"<Value{ns}><Identifier>ns=2;i=1000</Identifier></Value>",
                "expandednodeid" => $"<Value{ns}><Identifier>svr=1;nsu=urn:example:namespace;i=1000" +
                    "</Identifier></Value>",
                "variant" => $"<Value{ns}><Value><String>Hello World</String></Value></Value>",
                "extensionobject" => $"<Value{ns}><TypeId><Identifier>i=628</Identifier></TypeId></Value>",
                "datavalue" => $"<Value{ns}><Value><Value><String>Hello World</String></Value></Value></Value>",
                "diagnosticinfo" => $"<Value{ns}><AdditionalInfo>seed diagnostic</AdditionalInfo></Value>",
                "qualifiedname" => $"<Value{ns}><NamespaceIndex>2</NamespaceIndex><Name>Temperature</Name></Value>",
                "localizedtext" => $"<Value{ns}><Locale>en-US</Locale><Text>Hello World</Text></Value>",
                _ => $"<Value{ns} />"
            };
        }

        private static void WriteParserTestcases(string workPath)
        {
            string parserPath = workPath + ".Parsers" + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(parserPath);

            foreach ((string name, string text) in GetParserSeeds())
            {
                File.WriteAllText(Path.Combine(parserPath, name + ".txt"), text, Encoding.UTF8);
            }
        }

        private static IEnumerable<(string Name, string Text)> GetParserSeeds()
        {
            yield return ("nodeid_numeric", "i=85");
            yield return ("nodeid_string", "ns=2;s=Demo.Node");
            yield return ("nodeid_guid", "ns=2;g=00000000-0000-0000-0000-000000000001");
            yield return ("expandednodeid_uri", "svr=1;nsu=http://opcfoundation.org/UA/;i=85");
            yield return ("expandednodeid_string", "nsu=urn:example:namespace;s=Demo");
            yield return ("relativepath_child", "/2:Block/2:Parameter");
            yield return ("relativepath_property", ".2:EngineeringUnits");
            yield return ("qualifiedname", "2:Temperature");
            yield return ("numericrange_single", "0");
            yield return ("numericrange_matrix", "0:10,1:2");
            yield return ("uuid", "00000000-0000-0000-0000-000000000001");
        }
    }
}
