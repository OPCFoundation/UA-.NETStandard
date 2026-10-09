/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Text;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    internal static class FederationTestSupport
    {
        public const string Application = "urn:test:provider";
        public const string Locator = "opc.tcp://localhost:4840";
        public const string Xid = "/messagegroups/g/messages/m";
        public static ExpandedNodeId Root { get; } = new("registry", 0, Namespaces.EndpointRegistry);
        public static ExpandedNodeId Target { get; } = new("actual-node-not-derived-from-xid", 0, Namespaces.EndpointRegistry);
        public static ServiceMessageContext Context { get; } = CreateContext();
        public static RegistryRecordMapper Mapper { get; } = EndpointRegistryNativeCatalog.CreateMapper(Context,
            [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()]);

        public static RegistryObjectValueDataType Json(string value) =>
            (RegistryObjectValueDataType)RegistryValues.Parse(Encoding.UTF8.GetBytes(value));

        public static RegistryEntityReferenceDataType Source(string origin = "urn:test:one", string xid = Xid,
            string locator = Locator, bool ua = true) => new()
        {
            OriginUri = origin,
            Xid = xid,
            Role = xid.Contains("/versions/", System.StringComparison.Ordinal) ? "MetadataVersion" : "MetadataResource",
            Locator = locator,
            HasNativeTarget = ua,
            NativeTarget = ua ? Target : ExpandedNodeId.Null
        };

        public static FederationTrustBinding Binding(RegistryEntityReferenceDataType source,
            string application = Application, ExpandedNodeId root = default, ArrayOf<string> locators = default) =>
            new(source, application, application.Length == 0 ? ExpandedNodeId.Null : root.IsNull ? Root : root,
                locators.Count == 0 ? [source.Locator!] : locators);

        public static FederationNodeObservation RootEvidence(ExpandedNodeId node = default,
            ArrayOf<ExpandedNodeId> types = default) => new(node.IsNull ? Root : node, NodeClass.Object,
                "RegistryRoot", types.Count == 0 ? [XRegistry.ObjectTypeIds.RegistryType, ObjectTypeIds.EndpointRegistryType] : types);

        public static FederationNodeObservation TargetEvidence(ExpandedNodeId node = default,
            ArrayOf<ExpandedNodeId> types = default, NodeClass nodeClass = NodeClass.Object, string role = "MetadataResource") =>
            new(node.IsNull ? Target : node, nodeClass, role,
                types.Count == 0 ? [XRegistry.ObjectTypeIds.MetadataResourceType, ObjectTypeIds.MessageDefinitionType] : types);

        public static FederationMetadataObservation Evidence(RegistryEntityReferenceDataType source,
            string application = Application, FederationNodeObservation? root = null,
            FederationNodeObservation? target = null, ExpandedNodeId owner = default, string logicalXid = Xid,
            string version = "1", bool hasDocument = false, uint maxVersions = 1) =>
            new(source, source.Locator!, application,
                application.Length == 0 ? null : root ?? RootEvidence(),
                application.Length == 0 ? null : target ?? TargetEvidence(),
                application.Length == 0 ? ExpandedNodeId.Null : owner.IsNull ? Root : owner,
                logicalXid, version, hasDocument, maxVersions);

        public static EndpointRegistryMessageObservation Observation(RegistryEntityReferenceDataType source,
            string json = """{"messageid":"m","x-n":1.00,"x-null":null}""", uint epoch = 3, string version = "1") =>
            new() { Source = source, Metadata = Json(json), Epoch = epoch, VersionId = version };

        public static MessageResolutionRequestDataType Request(RegistryEntityReferenceDataType source,
            string? uri = null, RegistryEntityReferenceDataType? context = null) => new()
        {
            Reference = uri ?? source.Xid!,
            References =
            [
                new MessageReferenceBindingDataType
                {
                    Context = context ?? source,
                    ReferenceUri = uri ?? source.Xid!,
                    Target = source
                }
            ]
        };

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(SchemaRegistry.Namespaces.SchemaRegistry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            context.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().AddOpcUaEndpointRegistry().Commit();
            return context;
        }
    }
}
