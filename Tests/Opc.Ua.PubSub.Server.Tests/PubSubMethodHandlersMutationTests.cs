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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    [TestFixture]
    [TestSpec("9.1.3.4", Summary = "AddConnection handler")]
    [TestSpec("9.1.3.5", Summary = "RemoveConnection handler")]
    [TestSpec("9.1.6", Summary = "Configuration methods")]
    [TestSpec("9.1.4.3", Summary = "PublishedDataItemsType variable methods")]
    [TestSpec("9.1.4.5", Summary = "DataSetFolderType dataset methods")]
    public class PubSubMethodHandlersMutationTests
    {
        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnection_ValidInput_ReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "handler-conn",
                TransportProfileUri =
                    "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var inputs = BuildArray(Variant.From(new ExtensionObject(connCfg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out NodeId id), Is.True);
                Assert.That(id.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.3.5")]
        public void OnRemoveConnection_ValidInput_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "to-remove",
                TransportProfileUri =
                    "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var addInputs = BuildArray(Variant.From(new ExtensionObject(connCfg)));
            var addOutputs = new List<Variant>();
            handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: addInputs, outputArguments: addOutputs);
            Assert.That(addOutputs[0].TryGetValue(out NodeId connId), Is.True);

            var removeInputs = BuildArray(Variant.From(connId));
            var removeOutputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveConnection(
                BuildContext(), method: null!, inputArguments: removeInputs,
                outputArguments: removeOutputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnGetConfiguration_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnGetConfiguration(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfiguration_ValidInput_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var cfg = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var inputs = BuildArray(Variant.From(new ExtensionObject(cfg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.4.5")]
        public void OnAddPublishedDataItems_RegistersPublishedDataItemsDataSet()
        {
            IPubSubApplication application = CreateApplication();
            PubSubMethodHandlers handlers = CreateHandlers(application);
            var outputs = new List<Variant>();
            var variable = new PublishedVariableDataType
            {
                PublishedVariable = new NodeId(Variables.Server_ServerStatus_CurrentTime),
                AttributeId = Attributes.Value
            };

            ServiceResult result = handlers.OnAddPublishedDataItems(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(
                    Variant.From("items"),
                    Variant.From(new ArrayOf<string>(s_currentTimeAlias)),
                    Variant.From(Array.Empty<DataSetFieldFlags>()),
                    Variant.FromStructure(new ArrayOf<PublishedVariableDataType>(
                        new[] { variable }))),
                outputArguments: outputs);

            PubSubConfigurationDataType configuration = application.GetConfiguration();
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(3));
                Assert.That(configuration.PublishedDataSets, Has.Count.EqualTo(1));
                Assert.That(configuration.PublishedDataSets[0].DataSetSource.TryGetValue(
                    out PublishedDataItemsDataType? _), Is.True);
            });
        }

        [Test]
        [TestSpec("9.1.4.5")]
        public void OnAddPublishedDataItemsTemplate_UsesProvidedMetaData()
        {
            IPubSubApplication application = CreateApplication();
            PubSubMethodHandlers handlers = CreateHandlers(application);
            var outputs = new List<Variant>();
            var variable = new PublishedVariableDataType
            {
                PublishedVariable = new NodeId(Variables.Server_ServerStatus_CurrentTime),
                AttributeId = Attributes.Value
            };
            var metaData = new DataSetMetaDataType
            {
                Name = "template",
                Fields = new ArrayOf<FieldMetaData>(new[]
                {
                    new FieldMetaData
                    {
                        Name = "FromTemplate",
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                })
            };

            ServiceResult result = handlers.OnAddPublishedDataItemsTemplate(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(
                    Variant.From("template"),
                    Variant.From(new ExtensionObject(metaData)),
                    Variant.FromStructure(new ArrayOf<PublishedVariableDataType>(
                        new[] { variable }))),
                outputArguments: outputs);

            PubSubConfigurationDataType configuration = application.GetConfiguration();
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(2));
                Assert.That(configuration.PublishedDataSets[0].Name, Is.EqualTo("template"));
                Assert.That(configuration.PublishedDataSets[0].DataSetSource.TryGetValue(
                    out PublishedDataItemsDataType? _), Is.True);
            });
        }

        [Test]
        [TestSpec("9.1.4.5")]
        public void OnAddPublishedEvents_RegistersPublishedEventsDataSet()
        {
            IPubSubApplication application = CreateApplication();
            PubSubMethodHandlers handlers = CreateHandlers(application);
            var outputs = new List<Variant>();
            var selectedField = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = new ArrayOf<QualifiedName>(new[] { new QualifiedName(BrowseNames.EventId) }),
                AttributeId = Attributes.Value
            };

            ServiceResult result = handlers.OnAddPublishedEvents(
                BuildContext(),
                method: NewPublishedDataItemsMethod("items", "AddVariables"),
                inputArguments: BuildArray(
                    Variant.From("events"),
                    Variant.From(ObjectIds.Server),
                    Variant.From(new ArrayOf<string>(s_eventIdAlias)),
                    Variant.From(Array.Empty<DataSetFieldFlags>()),
                    Variant.FromStructure(new ArrayOf<SimpleAttributeOperand>(
                        new[] { selectedField })),
                    Variant.From(new ExtensionObject(new ContentFilter()))),
                outputArguments: outputs);

            PubSubConfigurationDataType configuration = application.GetConfiguration();
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(3));
                Assert.That(configuration.PublishedDataSets[0].DataSetSource.TryGetValue(
                    out PublishedEventsDataType? _), Is.True);
            });
        }

        [Test]
        [TestSpec("9.1.4.3")]
        public void OnAddVariablesAndRemoveVariables_MutateFieldsAndBumpConfigurationVersion()
        {
            IPubSubApplication application = CreateApplication();
            PubSubMethodHandlers handlers = CreateHandlers(application);
            var addDataSetOutputs = new List<Variant>();
            var variable = new PublishedVariableDataType
            {
                PublishedVariable = new NodeId(Variables.Server_ServerStatus_CurrentTime),
                AttributeId = Attributes.Value
            };
            handlers.OnAddPublishedDataItems(
                BuildContext(),
                method: NewPublishedDataItemsMethod("items", "RemoveVariables"),
                inputArguments: BuildArray(
                    Variant.From("items"),
                    Variant.From(new ArrayOf<string>(s_currentTimeAlias)),
                    Variant.From(Array.Empty<DataSetFieldFlags>()),
                    Variant.FromStructure(new ArrayOf<PublishedVariableDataType>(
                        new[] { variable }))),
                outputArguments: addDataSetOutputs);

            var addedVariable = new PublishedVariableDataType
            {
                PublishedVariable = new NodeId(Variables.Server_ServerStatus_StartTime),
                AttributeId = Attributes.Value
            };
            var addVariableOutputs = new List<Variant>();
            ServiceResult addResult = handlers.OnAddVariables(
                BuildContext(),
                method: NewPublishedDataItemsMethod("items", "AddVariables"),
                inputArguments: BuildArray(
                    addDataSetOutputs[1],
                    Variant.From(new ArrayOf<string>(s_startTimeAlias)),
                    Variant.From(s_notPromotedField),
                    Variant.FromStructure(new ArrayOf<PublishedVariableDataType>(
                        new[] { addedVariable }))),
                outputArguments: addVariableOutputs);
            Assert.That(StatusCode.IsGood(addResult.StatusCode), Is.True);
            Assert.That(addVariableOutputs, Has.Count.EqualTo(2));
            var removeVariableOutputs = new List<Variant>();
            ServiceResult removeResult = handlers.OnRemoveVariables(
                BuildContext(),
                method: NewPublishedDataItemsMethod("items", "RemoveVariables"),
                inputArguments: BuildArray(addVariableOutputs[0], Variant.From(new ArrayOf<uint>(s_firstVariableIndex))),
                outputArguments: removeVariableOutputs);

            PubSubConfigurationDataType configuration = application.GetConfiguration();
            Assert.That(configuration.PublishedDataSets[0].DataSetSource.TryGetValue(
                out PublishedDataItemsDataType? items), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(addResult.StatusCode), Is.True);
                Assert.That(StatusCode.IsGood(removeResult.StatusCode), Is.True);
                Assert.That(items!.PublishedData, Has.Count.EqualTo(1));
                Assert.That(configuration.PublishedDataSets[0].DataSetMetaData.ConfigurationVersion.MinorVersion,
                    Is.GreaterThan(1u));
            });
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnAddDataSetFolder_ReturnsGoodWithNodeId()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var inputs = BuildArray(Variant.From("my-folder"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetFolder(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
            });
        }

        private static PubSubMethodHandlers CreateHandlers()
        {
            return CreateHandlers(CreateApplication());
        }

        private static PubSubMethodHandlers CreateHandlers(IPubSubApplication app)
        {
            var options = new PubSubServerOptions
            {
                ExposeConfigurationMethods = true
            };
            return new PubSubMethodHandlers(
                app, null, options, NUnitTelemetryContext.Create());
        }

        private static IPubSubApplication CreateApplication()
        {
            return new PubSubApplicationBuilder(
                    NUnitTelemetryContext.Create())
                .WithApplicationId("handler-mutation-tests")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static SystemContext BuildContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }

        private static ArrayOf<Variant> BuildArray(params Variant[] values)
        {
            return new ArrayOf<Variant>(values);
        }

        private static MethodState NewPublishedDataItemsMethod(string dataSetName, string methodName)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId($"pubsub:published-data-set:{dataSetName}", 0),
                BrowseName = new QualifiedName(dataSetName)
            };
            var method = new MethodState(parent)
            {
                NodeId = new NodeId($"pubsub:published-data-set:{dataSetName}:{methodName}", 0),
                BrowseName = new QualifiedName(methodName)
            };
            parent.AddChild(method);
            return method;
        }

        private static readonly string[] s_currentTimeAlias = ["CurrentTime"];
        private static readonly string[] s_eventIdAlias = ["EventId"];
        private static readonly string[] s_startTimeAlias = ["StartTime"];
        private static readonly bool[] s_notPromotedField = [false];
        private static readonly uint[] s_firstVariableIndex = [0u];

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = payload;
                _ = topic;
                _ = cancellationToken;
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
