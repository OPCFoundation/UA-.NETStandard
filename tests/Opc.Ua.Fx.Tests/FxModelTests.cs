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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;
using Opc.Ua.Fx.Data;
using Opc.Ua.Tests;

namespace Opc.Ua.Fx.Tests
{
    /// <summary>
    /// Exercises the generated Part 81 model contracts, independently of a connection lifecycle implementation.
    /// </summary>
    [TestFixture]
    [Category("UAFX")]
    public sealed class FxModelTests
    {
        /// <summary>
        /// Model inputs remain byte-identical to the pinned published files on every supported host.
        /// </summary>
        [TestCase("data", "80FFA5EAE396FB2ED6EA7FC8647013DC6BDFBD939B12249A3B711542E4881339")]
        [TestCase("ac", "35586E374EEA321291AEE9E46731D3CA547CC5A73626F99260D20254DA60B941")]
        [TestCase("cm", "6ABFB436BFC07051D7C972DC36A4D8744A760CD1B94A72D37D621A4C272F7929")]
        public void ModelInputsRemainByteIdentical(string model, string expectedSha256)
        {
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Model", $"opc.ua.fx.{model}.nodeset2.xml");
            using var stream = File.OpenRead(path);
#if NET8_0_OR_GREATER
            string actual = CoreUtils.ToHexString(SHA256.HashData(stream));
#else
            using var sha256 = SHA256.Create();
            string actual = CoreUtils.ToHexString(sha256.ComputeHash(stream));
#endif
            Assert.That(actual, Is.EqualTo(expectedSha256));
        }

        /// <summary>
        /// Published declarations retain their NodeIds even when the caller changes namespace indexes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PublishedModelsMaterializeWithRemappedNamespaces(bool reverseNamespaces)
        {
            ServiceMessageContext message = CreateContext(reverseNamespaces);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context).AddOpcUaFxData(context).AddOpcUaFxAc(context).AddOpcUaFxCm(context);

            Assert.Multiple(() =>
            {
                Assert.That(nodes.Any(node => node.NodeId ==
                    NodeId.Create(2, Ac.Namespaces.FxAc, context.NamespaceUris)), Is.True,
                    "AutomationComponentType");
                Assert.That(nodes.Any(node => node.NodeId ==
                    NodeId.Create(1002, Ac.Namespaces.FxAc, context.NamespaceUris)), Is.True,
                    "ConnectionEndpointType");
                Assert.That(nodes.Any(node => node.NodeId ==
                    NodeId.Create(1002, Cm.Namespaces.FxCm, context.NamespaceUris)), Is.True,
                    "ConnectionManagerType");
                Assert.That(nodes.Any(node => node.NodeId ==
                    NodeId.Create(3005, Data.Namespaces.FxData, context.NamespaceUris)), Is.True,
                    "PubSubReserveCommunicationIds2DataType");
                Assert.That(nodes.Any(node => node.NodeId ==
                    NodeId.Create(3013, Data.Namespaces.FxData, context.NamespaceUris)), Is.True,
                    "PubSubReserveCommunicationIdsResult2DataType");
            });
        }

        /// <summary>
        /// Standard lifecycle Methods expose their published arguments rather than Data Channel-specific schemas.
        /// </summary>
        [Test]
        public void LifecycleMethodsRetainPublishedArgumentContracts()
        {
            ServiceMessageContext message = CreateContext(true);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaFxAc(context);
            NodeState automationComponent = nodes.Single(node => node.NodeId ==
                NodeId.Create(2, Ac.Namespaces.FxAc, context.NamespaceUris));
            var children = new List<BaseInstanceState>();
            automationComponent.GetChildren(context, children);
            var establish = (EstablishConnectionsMethodState)children.Single(node => node.NodeId ==
                NodeId.Create(292, Ac.Namespaces.FxAc, context.NamespaceUris));
            var close = (CloseConnectionsMethodState)children.Single(node => node.NodeId ==
                NodeId.Create(293, Ac.Namespaces.FxAc, context.NamespaceUris));
            Argument[] inputs = establish.InputArguments!.Value.ToArray()!;
            Argument[] outputs = establish.OutputArguments!.Value.ToArray()!;
            string[] inputNames =
            [
                "CommandMask", "AssetVerifications", "ConnectionEndpointConfigurations",
                "ReserveCommunicationIds", "CommunicationConfigurations"
            ];
            string[] outputNames =
            [
                "AssetVerificationResults", "ConnectionEndpointConfigurationResults",
                "ReserveCommunicationIdsResults", "CommunicationConfigurationResults"
            ];
            string[] closeInputNames = ["ConnectionEndpoints", "Remove"];

            Assert.Multiple(() =>
            {
                Assert.That(inputs.Select(argument => argument.Name), Is.EqualTo(inputNames));
                Assert.That(inputs.Select(argument => argument.ValueRank), Is.EqualTo(new[] { -1, 1, 1, 1, 1 }));
                Assert.That(inputs.Select(argument => argument.DataType), Is.EqualTo(
                    new uint[] { 1024, 1048, 1044, 3017, 1046 }
                        .Select(id => NodeId.Create(id, Data.Namespaces.FxData, context.NamespaceUris))));
                Assert.That(outputs.Select(argument => argument.Name), Is.EqualTo(outputNames));
                Assert.That(outputs.Select(argument => argument.ValueRank), Is.All.EqualTo(ValueRanks.OneDimension));
                Assert.That(outputs.Select(argument => argument.DataType), Is.EqualTo(
                    new uint[] { 1038, 3008, 3019, 1033 }
                        .Select(id => NodeId.Create(id, Data.Namespaces.FxData, context.NamespaceUris))));
                Assert.That(close.InputArguments!.Value.ToArray()!.Select(argument => argument.Name),
                    Is.EqualTo(closeInputNames));
                Assert.That(close.InputArguments.Value.ToArray()!.Select(argument => argument.DataType),
                    Is.EqualTo(new[] { DataTypeIds.NodeId, DataTypeIds.Boolean }));
                Assert.That(close.InputArguments.Value.ToArray()!.Select(argument => argument.ValueRank),
                    Is.EqualTo(new[] { ValueRanks.OneDimension, ValueRanks.Scalar }));
                Assert.That(close.OutputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(close.OutputArguments!.Value[0].Name, Is.EqualTo("Results"));
                Assert.That(close.OutputArguments.Value[0].DataType, Is.EqualTo(DataTypeIds.StatusCode));
            });
        }

        /// <summary>
        /// Reservation contracts preserve standard identifier counts and transport-specific request and result fields.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ReservationContractsRoundTripWithoutReplacingPubSubIdentifiers(bool reverseNamespaces)
        {
            ServiceMessageContext context = CreateContext(reverseNamespaces);
            var request = new PubSubReserveCommunicationIds2DataType
            {
                TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc",
                NumReqWriterGroupIds = 2,
                NumReqDataSetWriterIds = 3,
                RequestTransportSpecificInfo = true
            };
            var result = new PubSubReserveCommunicationIdsResult2DataType
            {
                DefaultPublisherId = new Variant((uint)17),
                WriterGroupIds = [4, 5],
                DataSetWriterIds = [10, 11, 12],
                TransportSpecificInfo = new Variant("opc.tcp://localhost:4840/uafx")
            };

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteExtensionObject("Request", new ExtensionObject(request));
                encoder.WriteExtensionObject("Result", new ExtensionObject(result));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ExtensionObject requestObject = decoder.ReadExtensionObject("Request");
            ExtensionObject resultObject = decoder.ReadExtensionObject("Result");
            Assert.That(requestObject.TryGetValue(out PubSubReserveCommunicationIds2DataType? decodedRequest),
                Is.True);
            Assert.That(resultObject.TryGetValue(out PubSubReserveCommunicationIdsResult2DataType? decodedResult),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(decodedRequest!.TransportProfileUri, Is.EqualTo(request.TransportProfileUri));
                Assert.That(decodedRequest.NumReqWriterGroupIds, Is.EqualTo(2));
                Assert.That(decodedRequest.NumReqDataSetWriterIds, Is.EqualTo(3));
                Assert.That(decodedRequest.RequestTransportSpecificInfo, Is.True);
                Assert.That(decodedResult!.DefaultPublisherId, Is.EqualTo(new Variant((uint)17)));
                Assert.That(decodedResult.WriterGroupIds.ToArray(), Is.EqualTo(new ushort[] { 4, 5 }));
                Assert.That(decodedResult.DataSetWriterIds.ToArray(), Is.EqualTo(new ushort[] { 10, 11, 12 }));
                Assert.That(decodedResult.TransportSpecificInfo,
                    Is.EqualTo(new Variant("opc.tcp://localhost:4840/uafx")));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// Generated structures retain persistence, cleanup and variable associations without runtime admissions.
        /// </summary>
        [TestCase(false, 0)]
        [TestCase(true, 1234.5)]
        public void EndpointParametersPreservePersistenceAndVariableAssociations(bool persistent, double timeout)
        {
            ServiceMessageContext context = CreateContext(false);
            var parameters = new PubSubConnectionEndpointParameterDataType
            {
                Name = "Endpoint",
                ConnectionEndpointTypeId = NodeId.Create(1002, Ac.Namespaces.FxAc, context.NamespaceUris),
                InputVariableIds = [new NodeId("input", 1)],
                OutputVariableIds = [new NodeId("output", 1)],
                IsPersistent = persistent,
                CleanupTimeout = timeout
            };
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                parameters.Encode(encoder);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            var decoded = new PubSubConnectionEndpointParameterDataType();
            decoded.Decode(decoder);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Name, Is.EqualTo("Endpoint"));
                Assert.That(decoded.ConnectionEndpointTypeId, Is.EqualTo(parameters.ConnectionEndpointTypeId));
                Assert.That(decoded.InputVariableIds.ToArray(), Is.EqualTo(parameters.InputVariableIds.ToArray()));
                Assert.That(decoded.OutputVariableIds.ToArray(), Is.EqualTo(parameters.OutputVariableIds.ToArray()));
                Assert.That(decoded.IsPersistent, Is.EqualTo(persistent));
                Assert.That(decoded.CleanupTimeout, Is.EqualTo(timeout));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// Creates an isolated model-aware context with noncanonical namespace ordering.
        /// </summary>
        private static ServiceMessageContext CreateContext(bool reverseNamespaces)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:uafx-tests:application");
            string[] namespaces =
            [
                Di.Namespaces.OpcUaDi,
                Data.Namespaces.FxData,
                Ac.Namespaces.FxAc,
                Cm.Namespaces.FxCm
            ];
            foreach (string uri in reverseNamespaces ? namespaces.Reverse() : namespaces)
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            context.Factory.Builder.AddOpcUaDi().AddOpcUaFxData().AddOpcUaFxAc().AddOpcUaFxCm().Commit();
            return context;
        }
    }
}
