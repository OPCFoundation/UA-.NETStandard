/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua.EndpointRegistry;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.SchemaRegistry.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises registry runtime commits and format-owned identities in a native executable.
    /// </summary>
    public sealed class RegistryRuntimeAotTests
    {
        [Test]
        public async Task NativeRegistryCommitsAndSchemaRegistrationsSurviveTrimming()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(SchemaRegistry.Namespaces.SchemaRegistry);
            context.NamespaceUris.GetIndexOrAppend(EndpointRegistry.Namespaces.EndpointRegistry);
            context.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().AddOpcUaEndpointRegistry().Commit();
            RegistryRecordMapper mapper = EndpointRegistryNativeCatalog.CreateMapper(context,
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()]);
            await using var state = new MemoryRegistryStateStore();
            using var host = new RegistryNativeHost(new RegistryNativeHostOptions
            {
                Store = state,
                Mapper = mapper,
                MessageContext = context,
                Collections = ["messagegroups"],
                RegistryRecordType = nameof(EndpointRegistryDocumentDataType),
                GroupRecordType = _ => nameof(MessageGroupDataType),
                ResourceRecordType = nameof(MessageDefinitionDataType),
                InitialDocument = (RegistryObjectValueDataType)RegistryValues.Parse(
                    """{"registryid":"aot","epoch":1,"specversion":"1.0-rc4"}"""u8),
                Validate = value => EndpointRegistryRules.ValidateRegistry(value)
            });
            await host.StartAsync();
            RegistryMutationResultDataType result = await host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/aot",
                Definition = mapper.Canonicalize(new MessageGroupDataType
                {
                    PresentFields = ["MessageGroupId"],
                    MessageGroupId = "aot"
                })
            });
            await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(result.Epoch).IsEqualTo(1u);
            await using var schemaState = new MemoryRegistryStateStore();
            using var schemas = new SchemaRegistryStore(
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider(), new ArrowSchemaFormatProvider()],
                schemaState, context, new RegistryEntityReferenceDataType { OriginUri = "urn:aot:schemas" });
            await schemas.StartAsync();
            TypedSchemaReadResultDataType schema = await schemas.RegisterAsync(
                new TypedSchemaRegistrationRequestDataType
                {
                    Registration = new SchemaRegistrationDataType
                    {
                        NamespaceUri = "http://contoso.org/UA/Pumps/",
                        SchemaName = "Temperature",
                        VersionId = "1",
                        Format = "JsonSchema/2020-12",
                        EntityUri = "urn:aot:temperature-v1",
                        ResourceUri = "urn:aot:temperature",
                        MakeDefault = true
                    },
                    Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
                });
            await Assert.That(schema.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(schema.Document.Reference.Entity.Xid)
                .IsEqualTo("/schemagroups/org.contoso.UA.Pumps/schemas/Temperature.jsonschema/versions/1");
            await Assert.That(schemas.Resolve(schema.Document.Reference.SchemaId).Content.Format)
                .IsEqualTo("JsonSchema/2020-12");
        }
    }
}
