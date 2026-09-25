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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task NativeAliasRetainsReadOnlyVersionAccessAndOriginalResourceIdentityAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"shared-document"}""").ConfigureAwait(false);
            XRegistryResponse created = await m_native.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/alias",
                    /*lang=json,strict*/ """{"meta":{"xref":"/schemagroups/g/schemas/r"}}""")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            NodeId group =
                await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g").ConfigureAwait(false);
            NodeId node = await FindChildEntityAsync(group, "/schemagroups/g/schemas/alias").ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(node);
            uint handle = await file.OpenAsync(1).ConfigureAwait(false);
            ByteString bytes = await file.ReadAsync(handle, 256).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> properties = await XRegistryOpcUaEndpoint.BrowseAsync(
                m_session, node, m_options, CancellationToken.None).ConfigureAwait(false);
            ReferenceDescription writableProperty = properties.ToList().Single(property =>
                property.BrowseName == new QualifiedName("Writable", 0));
            DataValue writable = await m_session.ReadValueAsync(
                ExpandedNodeId.ToNodeId(writableProperty.NodeId, m_session.NamespaceUris)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Utf8(bytes), Is.EqualTo("shared-document"));
                Assert.That(writable.WrappedValue.TryGetValue(out bool value), Is.True);
                Assert.That(value, Is.False);
                Assert.That(created.Metadata.GetProperty("schemaid").GetString(), Is.EqualTo("alias"));
            });
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.OpenAsync(6).ConfigureAwait(false));
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public async Task DanglingNativeAliasHasNoInventedVersionAndRecoversItsStableLogicalNodeAsync()
        {
            XRegistryResponse created = await m_native.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/alias",
                    /*lang=json,strict*/ """{"meta":{"xref":"/schemagroups/g/schemas/missing"}}""")).ConfigureAwait(
                        false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            NodeId group =
                await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g").ConfigureAwait(false);
            NodeId node = await FindChildEntityAsync(group, "/schemagroups/g/schemas/alias").ConfigureAwait(false);
            NodeId versions = await ChildAsync(node, "Versions").ConfigureAwait(false);
            ArrayOf<ReferenceDescription> children = await XRegistryOpcUaEndpoint.BrowseAsync(
                m_session, versions, m_options, CancellationToken.None).ConfigureAwait(false);
            NodeId resourceType = ExpandedNodeId.ToNodeId(ObjectTypeIds.ResourceType, m_session.NamespaceUris);
            ReadResponse version = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = await ChildAsync(node, "VersionId").ConfigureAwait(false),
                    AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(children.ToList().Count(child =>
                    ExpandedNodeId.ToNodeId(child.TypeDefinition, m_session.NamespaceUris) == resourceType), Is.Zero);
                Assert.That(version.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(m_manager.IsProjectionDegraded, Is.False);
                Assert.That(m_manager.IsNotificationDegraded, Is.True);
            });
            await SeedAsync("/schemagroups/g/schemas/missing",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"now-accessible"}""").ConfigureAwait(false);
            NodeId repaired = await FindChildEntityAsync(group, "/schemagroups/g/schemas/alias").ConfigureAwait(false);
            Assert.That(repaired, Is.EqualTo(node));
            XRegistryResponse metadata = await m_native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/alias")).ConfigureAwait(false);
            Assert.That(metadata.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("v1"));
        }

        [Test]
        public async Task NativeExternalDocumentsRemainUrisInsteadOfSuccessfulEmptyFilesAsync()
        {
            XRegistryResponse created = await m_native.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r/versions/v1",
                    /*lang=json,strict*/ """{"schemaurl":"https://not-contacted.example/document"}""")).ConfigureAwait(
                        false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse redirect = await m_native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(redirect.StatusCode, Is.EqualTo(303));
                Assert.That(redirect.Location, Is.EqualTo("https://not-contacted.example/document"));
                Assert.That(redirect.Document.IsNull, Is.True);
            });
        }
    }
}
