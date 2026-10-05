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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Schema.Tests.OpenApi
{
    /// <summary>
    /// Validates the messages the REST binding writes against the component
    /// schemas of the generated OpenAPI document, so the document describes
    /// the wire format and not only the names of the types.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApiWireFormat")]
    [Parallelizable]
    public class WebApiOpenApiWireFormatTests
    {
        private static readonly double[] s_doubles = [1.5, 2.5];
        private static readonly int[] s_integers = [1, 2];

        private static readonly Lazy<JsonObject> s_document = new(
            () => new WebApiOpenApiGenerator().Generate(includeSchemas: true));

        private static IEnumerable<WebApiServiceRoute> AllRoutes => WebApiServiceRoutes.Routes;

        [TestCaseSource(nameof(AllRoutes))]
        public void DefaultMessagesOfEveryRouteValidate(WebApiServiceRoute route)
        {
            AssertValid(route.RequestType.Name, Encode((IEncodeable)Activator.CreateInstance(route.RequestType)!));
            AssertValid(route.ResponseType.Name, Encode((IEncodeable)Activator.CreateInstance(route.ResponseType)!));
        }

        [Test]
        public void ReadRequestWithHeaderAndNodesValidates()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    AuthenticationToken = new NodeId(Guid.NewGuid(), 1),
                    Timestamp = DateTimeUtc.Now,
                    RequestHandle = 7,
                    ReturnDiagnostics = 3,
                    AuditEntryId = "audit",
                    TimeoutHint = 10000
                },
                MaxAge = 250.5,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value,
                        IndexRange = "0:1",
                        DataEncoding = new QualifiedName("Default Binary")
                    }
                ]
            };

            JsonNode body = Encode(request);

            Assert.That(body["TimestampsToReturn"]!.GetValueKind(), Is.EqualTo(JsonValueKind.Number));
            AssertValid("ReadRequest", body);
        }

        [Test]
        public void ReadResponseWithVariantsAndStatusValidates()
        {
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTimeUtc.Now,
                    RequestHandle = 7,
                    ServiceResult = StatusCodes.GoodCompletesAsynchronously,
                    StringTable = ["one", "two"]
                },
                Results =
                [
                    new DataValue(Variant.From(42), StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now),
                    new DataValue(Variant.From(s_doubles), StatusCodes.BadNodeIdUnknown, DateTimeUtc.Now),
                    new DataValue(Variant.From("text"), StatusCodes.UncertainInitialValue, DateTimeUtc.Now)
                ],
                DiagnosticInfos =
                [
                    new DiagnosticInfo
                    {
                        SymbolicId = 1,
                        AdditionalInfo = "outer",
                        InnerStatusCode = StatusCodes.BadInternalError,
                        InnerDiagnosticInfo = new DiagnosticInfo { SymbolicId = 2, AdditionalInfo = "inner" }
                    }
                ]
            };

            JsonNode body = Encode(response);

            Assert.That(body["Results"]![1]!["Status"], Is.Not.Null);
            AssertValid("ReadResponse", body);
        }

        [Test]
        public void ADataValueThatNamesItsStatusStatusCodeDoesNotValidate()
        {
            JsonNode body = Encode(new ReadResponse
            {
                Results = [new DataValue(Variant.From(1), StatusCodes.BadNodeIdUnknown)]
            });
            body["Results"]![0]!.AsObject()["StatusCode"] = body["Results"]![0]!["Status"]!.DeepClone();
            body["Results"]![0]!.AsObject().Remove("Status");

            Assert.That(Evaluate("ReadResponse", body).IsValid, Is.False);
        }

        [Test]
        public void BrowseResponseWithReferencesValidates()
        {
            var response = new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = ByteString.From([1, 2, 3]),
                        References =
                        [
                            new ReferenceDescription
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IsForward = true,
                                NodeId = new ExpandedNodeId(ObjectIds.Server),
                                BrowseName = new QualifiedName("Server"),
                                DisplayName = new LocalizedText("en", "Server"),
                                NodeClass = NodeClass.Object,
                                TypeDefinition = new ExpandedNodeId(ObjectTypeIds.ServerType)
                            }
                        ]
                    }
                ]
            };

            AssertValid("BrowseResponse", Encode(response));
        }

        [Test]
        public void ActivateSessionRequestWithAnIdentityTokenExtensionObjectValidates()
        {
            var request = new ActivateSessionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                LocaleIds = ["en", "de"],
                UserIdentityToken = new ExtensionObject(new AnonymousIdentityToken { PolicyId = "anonymous" })
            };

            JsonNode body = Encode(request);

            Assert.That(body["UserIdentityToken"]!["UaTypeId"], Is.Not.Null);
            AssertValid("ActivateSessionRequest", body);
        }

        [Test]
        public void CallRequestWithVariantArgumentsValidates()
        {
            var request = new CallRequest
            {
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = [Variant.From(5u), Variant.From(s_integers), Variant.From("x")]
                    }
                ]
            };

            AssertValid("CallRequest", Encode(request));
        }

        [Test]
        public void PublishResponseWithNotificationDataValidates()
        {
            var notification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = 4,
                        Value = new DataValue(Variant.From(1.25), StatusCodes.Good, DateTimeUtc.Now)
                    }
                ]
            };
            var response = new PublishResponse
            {
                ResponseHeader = new ResponseHeader(),
                SubscriptionId = 9,
                AvailableSequenceNumbers = [1, 2, 3],
                MoreNotifications = true,
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = 2,
                    PublishTime = DateTimeUtc.Now,
                    NotificationData = [new ExtensionObject(notification)]
                },
                Results = [StatusCodes.Good, StatusCodes.BadSequenceNumberUnknown]
            };

            AssertValid("PublishResponse", Encode(response));
        }

        [Test]
        public void GetEndpointsResponseWithEndpointsValidates()
        {
            var response = new GetEndpointsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Endpoints =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "https://localhost/",
                        Server = new ApplicationDescription
                        {
                            ApplicationUri = "urn:test",
                            ApplicationType = ApplicationType.Server,
                            ApplicationName = new LocalizedText("en", "Test")
                        },
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        UserIdentityTokens =
                        [
                            new UserTokenPolicy { PolicyId = "user", TokenType = UserTokenType.UserName }
                        ],
                        SecurityLevel = 3
                    }
                ]
            };

            AssertValid("GetEndpointsResponse", Encode(response));
        }

        [Test]
        public void NullElementsOfArraysValidateAgainstTheNullableItems()
        {
            // Part 6, 5.4.5: a null element of an array is the JSON literal null.
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader { StringTable = ["one", null!, "three"] },
                Results = [default, new DataValue(Variant.From(1), StatusCodes.Good)],
                DiagnosticInfos = [null!, new DiagnosticInfo { SymbolicId = 1 }]
            };

            JsonNode body = Encode(response);

            Assert.Multiple(() =>
            {
                Assert.That(body["ResponseHeader"]!["StringTable"]![1], Is.Null);
                Assert.That(body["Results"]![0], Is.Null);
                Assert.That(body["DiagnosticInfos"]![0], Is.Null);
                AssertValid("ReadResponse", body);
            });
        }

        [Test]
        public void NullStructuresAndExtensionObjectsInArraysValidateAgainstTheNullableItems()
        {
            var request = new ReadRequest
            {
                NodesToRead = [null!, new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime }]
            };
            var response = new PublishResponse
            {
                NotificationMessage = new NotificationMessage { NotificationData = [default] }
            };

            JsonNode requestBody = Encode(request);
            JsonNode responseBody = Encode(response);

            Assert.Multiple(() =>
            {
                Assert.That(requestBody["NodesToRead"]![0], Is.Null);
                Assert.That(responseBody["NotificationMessage"]!["NotificationData"]![0], Is.Null);
                AssertValid("ReadRequest", requestBody);
                AssertValid("PublishResponse", responseBody);
            });
        }

        [Test]
        public void ANullElementOfAnArrayOfNumbersDoesNotValidate()
        {
            JsonNode body = Encode(new PublishResponse { AvailableSequenceNumbers = [1, 2] });
            body["AvailableSequenceNumbers"]!.AsArray()[1] = null;

            Assert.That(Evaluate("PublishResponse", body).IsValid, Is.False);
        }

        [Test]
        public void MatricesAreWrittenAndDescribedAsObjectsWithTheFlattenedArrayAndTheDimensions()
        {
            // The encoder writes a field of a fixed rank of two or more as
            // an inline matrix (Part 6, 5.4.5), also for a structure.
            JsonObject document = new WebApiOpenApiGenerator(new MatrixFieldsResolver()).Generate(includeSchemas: true);
            ServiceMessageContext context = ServiceMessageContext.Create(null);
            using var memory = new MemoryStream();
            using (var encoder = new JsonEncoder(memory, context, JsonEncoderOptions.Compact))
            {
                encoder.WriteInlineMatrixValue(
                    "Strings",
                    Variant.From(new string[] { "a", null!, "c", "d" }.ToArrayOf().ToMatrix(2, 2)));
                encoder.WriteInlineMatrixValue(
                    "Doubles",
                    Variant.From(new double[] { 1, 2, 3, 4, 5, 6, 7, 8 }.ToArrayOf().ToMatrix(2, 2, 2)));
                encoder.WriteEncodeableMatrix(
                    "Structures",
                    new ReadValueId[] { new() { AttributeId = Attributes.Value }, null! }.ToArrayOf().ToMatrix(1, 2));
            }
            JsonNode body = JsonNode.Parse(memory.ToArray())!;

            Assert.Multiple(() =>
            {
                Assert.That(body["Strings"]!["Dimensions"]!.ToJsonString(), Is.EqualTo("[2,2]"));
                Assert.That(body["Strings"]!["Array"]!.AsArray(), Has.Count.EqualTo(4));
                Assert.That(body["Strings"]!["Array"]![1], Is.Null);
                Assert.That(body["Doubles"]!["Dimensions"]!.ToJsonString(), Is.EqualTo("[2,2,2]"));
                Assert.That(body["Structures"]!["Array"]![1], Is.Null);
                Assert.That(Evaluate(document, "ReadRequest", body).IsValid, Is.True, body.ToJsonString());
            });
        }

        [Test]
        public void AMatrixWrittenAsNestedArraysDoesNotValidate()
        {
            JsonObject document = new WebApiOpenApiGenerator(new MatrixFieldsResolver()).Generate(includeSchemas: true);
            JsonNode body = JsonNode.Parse("{\"Doubles\":[[1,2],[3,4]]}")!;

            Assert.That(Evaluate(document, "ReadRequest", body).IsValid, Is.False);
        }

        private static JsonNode Encode(IEncodeable message)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(null);
            byte[] json = WebApiBodyCodec.EncodeBody(message, context, JsonEncoderOptions.Compact);
            return JsonNode.Parse(json)!;
        }

        private static void AssertValid(string messageName, JsonNode body)
        {
            EvaluationResults results = Evaluate(messageName, body);

            Assert.That(results.IsValid, Is.True, body.ToJsonString() + Environment.NewLine + results);
        }

        private static EvaluationResults Evaluate(string messageName, JsonNode body)
        {
            return Evaluate(s_document.Value, messageName, body);
        }

        private static EvaluationResults Evaluate(JsonObject document, string messageName, JsonNode body)
        {
            // The component schemas use the keywords OpenAPI 3.0 shares with
            // JSON Schema 2020-12; only the location of the schemas differs,
            // the enumeration names, an OpenAPI generator extension, go and
            // nullable becomes the union with null.
            var schemas = (JsonObject)ConvertNullable(document["components"]!["schemas"])!;
            foreach (KeyValuePair<string, JsonNode?> component in schemas)
            {
                component.Value!.AsObject().Remove("x-enum-varnames");
            }
            string text = new JsonObject
            {
                ["$ref"] = "#/$defs/" + messageName,
                ["$defs"] = schemas
            }.ToJsonString().Replace("#/components/schemas/", "#/$defs/", StringComparison.Ordinal);

            var schema = JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return schema.Evaluate(
                JsonSerializer.SerializeToElement(body),
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }

        /// <summary>
        /// Returns a copy of the schema in which every schema object with
        /// <c>nullable: true</c> (OpenAPI 3.0) accepts <c>null</c> as well
        /// (<c>anyOf</c> with the type <c>null</c>).
        /// </summary>
        private static JsonNode? ConvertNullable(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject schema:
                    bool nullable = false;
                    var copy = new JsonObject();
                    foreach (KeyValuePair<string, JsonNode?> member in schema)
                    {
                        if (member.Key == "nullable" && member.Value is JsonValue flag && flag.GetValue<bool>())
                        {
                            nullable = true;
                            continue;
                        }
                        copy[member.Key] = ConvertNullable(member.Value);
                    }
                    return nullable
                        ? new JsonObject
                        {
                            ["anyOf"] = new JsonArray(copy, new JsonObject { ["type"] = "null" })
                        }
                        : copy;
                case JsonArray array:
                    return new JsonArray([.. array.Select(ConvertNullable)]);
                default:
                    return node?.DeepClone();
            }
        }

        /// <summary>
        /// Resolves the standard types, but gives the ReadRequest the matrix
        /// fields the services of the specification do not have.
        /// </summary>
        private sealed class MatrixFieldsResolver : IDataTypeDefinitionResolver
        {
            public MatrixFieldsResolver()
            {
                m_standard = new EncodeableFactoryDefinitionSource(EncodeableFactory.Create(), new NamespaceTable());
            }

            public bool TryResolve(ExpandedNodeId typeId, [NotNullWhen(true)] out UaTypeDescription? description)
            {
                return m_standard.TryResolve(typeId, out description);
            }

            public bool TryResolve(NodeId typeId, [NotNullWhen(true)] out UaTypeDescription? description)
            {
                return m_standard.TryResolve(typeId, out description);
            }

            public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
            {
                var types = new List<UaTypeDescription>();
                foreach (UaTypeDescription type in m_standard.GetNamespaceTypes(namespaceUri))
                {
                    types.Add(type.Name == "ReadRequest" ? WithMatrixFields(type) : type);
                }
                return types;
            }

            private static UaTypeDescription WithMatrixFields(UaTypeDescription type)
            {
                var definition = new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        SchemaTestData.Field("Strings", DataTypeIds.String, ValueRanks.TwoDimensions),
                        SchemaTestData.Field("Doubles", DataTypeIds.Double, 3),
                        SchemaTestData.Field("Structures", DataTypeIds.ReadValueId, ValueRanks.TwoDimensions)
                    ]
                };
                return new UaTypeDescription(type.TypeId, type.BrowseName, definition, type.NamespaceUri);
            }

            private readonly EncodeableFactoryDefinitionSource m_standard;
        }
    }
}
