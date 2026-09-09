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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests for <see cref="WotConversionOutput"/> factory methods and the
    /// <see cref="WotNodeSetDocumentConverter"/> production implementation.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotDocumentConverterTests
    {
        [Test]
        public void FailureOutputSucceededIsFalse()
        {
            WotConversionOutput output = WotConversionOutput.Failure("Something went wrong.");

            Assert.That(output.Succeeded, Is.False);
            Assert.That(output.NodeSet, Is.Null);
            Assert.That(output.Errors, Has.Length.EqualTo(1));
            Assert.That(output.Errors[0], Does.Contain("Something went wrong."));
        }

        [Test]
        public void FailureOutputWithMultipleErrors()
        {
            WotConversionOutput output = WotConversionOutput.Failure("error1", "error2");

            Assert.That(output.Errors, Has.Length.EqualTo(2));
        }

        [Test]
        public void SuccessOutputSucceededIsTrue()
        {
            var nodeSet = new UANodeSet();
            WotConversionOutput output = WotConversionOutput.Success(nodeSet);

            Assert.That(output.Succeeded, Is.True);
            Assert.That(output.NodeSet, Is.SameAs(nodeSet));
            Assert.That(output.Errors, Is.Empty);
        }

        [Test]
        public void ConstructorWithDefaultErrorsIsEmpty()
        {
            var output = new WotConversionOutput(null, default);

            Assert.That(output.Errors, Is.Empty);
            Assert.That(output.Succeeded, Is.False);
        }

        [Test]
        public async Task NodeSetDocumentConverterConvertsValidThingModel()
        {
            var converter = new WotNodeSetDocumentConverter();
            byte[] content = TestMaterialization.Tm("urn:test-tm");

            var version = new WotResourceVersion(
                versionId: "v1",
                digest: WotContentDigest.Compute(content),
                contentLength: content.Length,
                contentType: "application/tm+json",
                format: "WoT-TM/1.0",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingModels,
                resourceId: "test-tm",
                kind: WoTDocumentKindEnum.ThingModel,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();
            WotRegistrySnapshot snapshot = service.Current;

            WotConversionOutput output = await converter
                .ConvertAsync(
                    resource,
                    ByteString.From(content),
                    snapshot,
                    new Dictionary<string, ByteString> { [version.DigestHex] = ByteString.From(content) },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(output, Is.Not.Null);
        }

        [Test]
        public async Task NodeSetDocumentConverterFailsOnInvalidJson()
        {
            var converter = new WotNodeSetDocumentConverter();
            byte[] invalidContent = TestMaterialization.InvalidJson();

            var version = new WotResourceVersion(
                versionId: "v1",
                digest: WotContentDigest.Compute(invalidContent),
                contentLength: invalidContent.Length,
                contentType: "application/td+json",
                format: "WoT-TD/1.1",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingDescriptions,
                resourceId: "bad",
                kind: WoTDocumentKindEnum.ThingDescription,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();
            WotRegistrySnapshot snapshot = service.Current;

            WotConversionOutput output = await converter
                .ConvertAsync(
                    resource,
                    ByteString.From(invalidContent),
                    snapshot,
                    new Dictionary<string, ByteString> { [version.DigestHex] = ByteString.From(invalidContent) },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(output.Succeeded, Is.False);
            Assert.That(output.Errors, Is.Not.Empty);
        }

        [Test]
        public async Task GeneratedInteractionIdentitiesComeFromTheConvertedNodes()
        {
            byte[] content = Encoding.UTF8.GetBytes("""
                {
                  "@type": "uav:object",
                  "id": "urn:generated-runtime",
                  "title": "Device",
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{ "href": "https://device.example/value", "op": ["readproperty", "writeproperty"] }]
                    }
                  },
                  "actions": {
                    "run": { "forms": [{ "href": "https://device.example/run", "op": "invokeaction" }] }
                  },
                  "events": {
                    "alarm": {
                      "data": { "type": "object", "properties": {} },
                      "forms": [{ "href": "https://device.example/alarm", "op": "subscribeevent" }]
                    }
                  }
                }
                """);
            var version = new WotResourceVersion(
                "v1", WotContentDigest.Compute(content), content.Length,
                "application/td+json", "WoT-TD/1.1", default, default);
            var resource = new WotResource(
                WotRegistryGroups.ThingDescriptions, "generated-runtime", WoTDocumentKindEnum.ThingDescription,
                versions: ImmutableArray.Create(version), defaultVersionId: "v1");
            using var registry = new WotRegistryService();
            var converter = new WotNodeSetDocumentConverter();

            WotConversionOutput output = await converter.ConvertAsync(
                resource, ByteString.From(content), registry.Current,
                new Dictionary<string, ByteString> { [version.DigestHex] = ByteString.From(content) },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(output.Succeeded, Is.True, string.Join("; ", output.Errors));
            Assert.That(output.ProjectedAffordances.Count, Is.EqualTo(3));
            var namespaces = new NamespaceTable();
            foreach (string uri in output.NodeSet!.NamespaceUris!)
            {
                namespaces.Append(uri);
            }
            foreach (WotProjectedAffordance local in output.ProjectedAffordances)
            {
                Assert.That(local.NodeId, Is.Not.Empty);
                var expectedClass = local.Kind switch
                {
                    WotAffordanceKind.Property => typeof(UAVariable),
                    WotAffordanceKind.Action => typeof(UAMethod),
                    _ => typeof(UAObjectType)
                };
                NodeId nodeId = ExpandedNodeId.Parse(local.NodeId, namespaces);
                UANode node = output.NodeSet.Items!.Single(item => NodeId.Parse(item.NodeId!) == nodeId);
                Assert.That(node.GetType(), Is.EqualTo(expectedClass));
                Assert.That(local.OwnerNodeId, Is.EqualTo(output.RootNodeId.ToString()));
                Assert.That(local.Definition.ValueKind, Is.EqualTo(JsonValueKind.Object));
                if (local.Kind == WotAffordanceKind.Property)
                {
                    Assert.That(local.Definition.GetProperty("type").GetString(), Is.EqualTo("integer"));
                }
                else if (local.Kind == WotAffordanceKind.Event)
                {
                    Assert.That(local.Definition.GetProperty("data").GetProperty("type").GetString(),
                        Is.EqualTo("object"));
                }
            }

            var runtimeGraph = new WotProjectionBindingRuntimeTestHarness();
            runtimeGraph.Import(output.NodeSet);
            WotProjectedAffordance property = output.ProjectedAffordances.Find(
                declaration => declaration.Kind == WotAffordanceKind.Property)!;
            WotProjectedAffordance action = output.ProjectedAffordances.Find(
                declaration => declaration.Kind == WotAffordanceKind.Action)!;
            var form = new WotCompiledForm(
                new WotBindingIdentity("test", "1.0", "urn:test"),
                WotAffordanceKind.Action, action.Name, action.JsonPointer + "/forms/0",
                WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("test", null, -1, "test://source"),
                new WotAddressingDescriptor("remote-run"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "POST"),
                new WotPayloadDescriptor("application/json", "json"), [], true);
            var channel = new FakeWotBindingChannel(form)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good))
            };
            runtimeGraph.ChannelFactory.SetChannel(form, channel);
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, WotTargetMappingDescriptor.Empty);
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty, WotTargetMappingDescriptor.Empty);
            var reader = new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(43))))
            };
            Variant written = Variant.Null;
            var writer = new FakeWotBindingChannel(writeForm)
            {
                OnWrite = (value, _) =>
                {
                    written = value.WrappedValue;
                    return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.GoodClamped));
                }
            };
            runtimeGraph.ChannelFactory.SetChannel(readForm, reader);
            runtimeGraph.ChannelFactory.SetChannel(writeForm, writer);
            WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan(form, readForm, writeForm)
                .WithProjectedAffordances([property, action]);
            var runtimeFactory = new WotProjectionBindingRuntimeFactory(runtimeGraph.ChannelFactory);
            await using System.IAsyncDisposable? runtime = await runtimeFactory.CreateAsync(
                runtimeGraph.Builder, [plan]).ConfigureAwait(false);
            NodeId methodId = ExpandedNodeId.Parse(action.NodeId, runtimeGraph.Builder.Context.NamespaceUris);
            var method = (MethodState)runtimeGraph.Builder.Node(methodId).Node;
            var outputs = new List<Variant>();

            ServiceResult invocation = await method.CallAsync(
                runtimeGraph.Builder.Context,
                ExpandedNodeId.Parse(action.OwnerNodeId, runtimeGraph.Builder.Context.NamespaceUris),
                [], [], outputs).ConfigureAwait(false);

            Assert.That(invocation.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(channel.InvokeCount, Is.EqualTo(1));
            NodeId variableId = ExpandedNodeId.Parse(property.NodeId, runtimeGraph.Builder.Context.NamespaceUris);
            var variable = (BaseVariableState)runtimeGraph.Builder.Node(variableId).Node;
            (ServiceResult readStatus, DataValue readValue) = await variable.ReadAttributeAsync(
                runtimeGraph.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(readStatus.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(readValue.WrappedValue.TryGetValue(out int received), Is.True);
            Assert.That(received, Is.EqualTo(43));
            ServiceResult writeStatus = await variable.WriteAttributeAsync(
                runtimeGraph.Builder.Context, Attributes.Value, default, new DataValue(new Variant(17)))
                .ConfigureAwait(false);
            Assert.That(writeStatus.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(written.TryGetValue(out int sent), Is.True);
            Assert.That(sent, Is.EqualTo(17));
        }

        [Test]
        public void NodeSetDocumentConverterCanBeInstantiatedWithoutOptions()
        {
            var converter = new WotNodeSetDocumentConverter();
            Assert.That(converter, Is.Not.Null);
        }

        [Test]
        public void NodeSetDocumentConverterCanBeInstantiatedWithOptions()
        {
            var options = new WotNodeSetConverterOptions();
            var converter = new WotNodeSetDocumentConverter(options);
            Assert.That(converter, Is.Not.Null);
        }

        [Test]
        public void SuccessOutputFromConstructorWithNonDefaultErrors()
        {
            var nodeSet = new UANodeSet();
            var errors = ImmutableArray<string>.Empty;
            var output = new WotConversionOutput(nodeSet, errors);

            Assert.That(output.Succeeded, Is.True);
            Assert.That(output.Errors, Is.Empty);
        }

        [Test]
        public void FailureOutputRootNodeIdIsNull()
        {
            WotConversionOutput output = WotConversionOutput.Failure("fail");

            Assert.That(output.RootNodeId.IsNull, Is.True);
        }
    }
}
