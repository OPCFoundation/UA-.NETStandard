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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server.Tests
{
    [TestFixture]
    [Category("SchemaRegistry")]
    public sealed class SchemaRegistryStoreTests
    {
        [Test]
        public async Task ExactVersionUpdatesDoNotChangeIdentityAndNoOpsRetainOriginalBytesAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            SchemaReferenceDataType reference = Reference("temperature", "1");
            ByteString original = ByteString.From("{ \"type\" : \"number\" }\n"u8.ToArray());
            TypedSchemaReadResultDataType first = await store.RegisterRawAsync(reference, original).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            TypedSchemaReadResultDataType noOp = await store.WriteAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                ExpectedEpoch = 1,
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
            }).ConfigureAwait(false);
            Assert.That(noOp.Document.Epoch, Is.EqualTo(1));
            Assert.That(store.DocumentBytes(reference), Is.EqualTo(original));
            TypedSchemaReadResultDataType changed = await store.WriteAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                ExpectedEpoch = 1,
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"string"}"""u8)
            }).ConfigureAwait(false);
            Assert.That(changed.Document.Epoch, Is.EqualTo(2));
            Assert.That(changed.Document.Reference.SchemaId, Is.Not.EqualTo(first.Document.Reference.SchemaId));
            Assert.That(store.Entries.Count, Is.EqualTo(1));
            TypedSchemaReadResultDataType stale = await store.WriteAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                ExpectedEpoch = 1,
                Content = first.Document.Content
            }).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(stale.Issues[0].Code, Is.EqualTo("E_SCHEMA_EPOCH_CONFLICT"));
            Assert.That(stale.Document, Is.Null);
        }

        [Test]
        public async Task LogicalReadsRequireExplicitDefaultAndExactReadsNeverSubstituteAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            SchemaReferenceDataType first = Reference("s", "1");
            SchemaReferenceDataType second = Reference("s", "2");
            await store.RegisterRawAsync(first, ByteString.From("""{"type":"number"}"""u8.ToArray()))
                .ConfigureAwait(false);
            await store.RegisterRawAsync(second, ByteString.From("""{"type":"string"}"""u8.ToArray()))
                .ConfigureAwait(false);
            SchemaReferenceDataType logical = Logical("s");
            Assert.That(store.Read(logical).Issues[0].Code, Is.EqualTo("E_SCHEMA_NO_DEFAULT"));
            await store.ConfigureDefaultAsync(logical, first.Entity.Xid!).ConfigureAwait(false);
            Assert.That(store.Read(logical).Document.Reference.Entity.Xid, Is.EqualTo(first.Entity.Xid));
            Assert.That(store.Read(Reference("s", "absent")).StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            SchemaReferenceDataType wrongUri = (SchemaReferenceDataType)first.Clone();
            wrongUri.EntityUri = "https://other.example.test/schema";
            Assert.That(store.Read(wrongUri).StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task LegacyCollisionUsesOnlyVisibleDocumentsAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            TypedSchemaReadResultDataType a = await store.RegisterRawAsync(Reference("a", "1"),
                ByteString.From("""{"type":"number"}"""u8.ToArray())).ConfigureAwait(false);
            await store.RegisterRawAsync(Reference("b", "1"),
                ByteString.From("{ \"type\" : \"number\" }"u8.ToArray())).ConfigureAwait(false);
            ServiceResultException ambiguity = Assert.Throws<ServiceResultException>(
                () => store.Resolve(a.Document.Reference.SchemaId))!;
            Assert.That(ambiguity.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(ambiguity.Message, Does.Not.Contain("/schemagroups/"));
            Assert.That(store.Resolve(a.Document.Reference.SchemaId,
                entry => entry.Reference.Entity.Xid == a.Document.Reference.Entity.Xid).Document,
                Is.EqualTo(ByteString.From("""{"type":"number"}"""u8.ToArray())));
        }

        [Test]
        public async Task RestartRestoresExactBytesAndProviderConfiguredDefaultsAsync()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "schema-store-" + Guid.NewGuid().ToString("N"));
            SchemaReferenceDataType reference = Reference("s", "1");
            ByteString original = ByteString.From("{ \"type\": \"number\", \"default\": 1.00 }\n"u8.ToArray());
            try
            {
                await using (var storage = new FileRegistryStateStore(directory))
                {
                    using var store = Store(storage);
                    await store.StartAsync().ConfigureAwait(false);
                    await store.RegisterRawAsync(reference, original).ConfigureAwait(false);
                    await store.ConfigureDefaultAsync(Logical("s"), reference.Entity.Xid!).ConfigureAwait(false);
                }
                await using (var storage = new FileRegistryStateStore(directory))
                {
                    using var store = Store(storage);
                    await store.StartAsync().ConfigureAwait(false);
                    Assert.That(store.DocumentBytes(reference), Is.EqualTo(original));
                    Assert.That(store.Read(Logical("s")).Document.Epoch, Is.EqualTo(1));
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task ReferenceOriginFormatAndFingerprintAreCheckedWithoutFallbackAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            SchemaReferenceDataType reference = Reference("s", "1");
            TypedSchemaReadResultDataType initial = await store.RegisterRawAsync(reference,
                ByteString.From("""{"type":"number"}"""u8.ToArray())).ConfigureAwait(false);
            var wrongOrigin = (SchemaReferenceDataType)reference.Clone();
            wrongOrigin.Entity.OriginUri = "urn:impostor";
            Assert.That(store.Read(wrongOrigin).StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            var wrongHash = (SchemaReferenceDataType)initial.Document.Reference.Clone();
            wrongHash.SchemaId = ByteString.From(new byte[8]);
            Assert.That(store.Read(wrongHash).StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            SchemaReferenceDataType avro = Reference("s", "2");
            avro.Format = "Avro/1.11";
            TypedSchemaReadResultDataType conflict = await store.RegisterRawAsync(avro,
                ByteString.From("\"string\""u8.ToArray())).ConfigureAwait(false);
            Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(store.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task SelectorNeedsAnExplicitProviderUriBindingAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            SchemaReferenceDataType reference = Reference("s", "1");
            await store.RegisterRawAsync(reference,
                ByteString.From("""{"$defs":{"v":{"type":"number"}}}"""u8.ToArray())).ConfigureAwait(false);
            reference.Selector = "/$defs/v";
            reference.SelectedObjectUri = reference.EntityUri + "#/$defs/v";
            Assert.That(store.Read(reference).StatusCode, Is.EqualTo(StatusCodes.Good));
            reference.SelectedObjectUri = reference.EntityUri + "#wrong";
            Assert.That(store.Read(reference).StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task ActivationFailureReportsTheDurableCommitAsUncertainAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            store.Activation = (_, _) => throw new InvalidOperationException("Injected projection failure.");
            SchemaReferenceDataType reference = Reference("s", "1");

            TypedSchemaReadResultDataType result = await store.RegisterRawAsync(reference,
                ByteString.From("""{"type":"number"}"""u8.ToArray())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.UncertainNotAllNodesAvailable));
            Assert.That(result.Issues[0].Code, Is.EqualTo("E_PROJECTION_PENDING"));
            Assert.That(result.Document.Epoch, Is.EqualTo(1));
            Assert.That(store.Read(reference).StatusCode, Is.EqualTo(StatusCodes.Good));
            TypedSchemaReadResultDataType retry = await store.WriteAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                ExpectedEpoch = 1,
                Content = result.Document.Content
            }).ConfigureAwait(false);
            Assert.That(retry.StatusCode, Is.EqualTo(StatusCodes.UncertainNotAllNodesAvailable));
            Assert.That(retry.Issues[0].Code, Is.EqualTo("E_PROJECTION_PENDING"));
            Assert.That((await storage.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(1));
            using var recovered = Store(storage);
            await recovered.StartAsync().ConfigureAwait(false);
            Assert.That(recovered.Read(reference).Document.Epoch, Is.EqualTo(1));
        }

        [Test]
        public async Task RegistrationPersistsSourceIdentitiesAndAnExplicitDefaultInOneCommitAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "http://contoso.org/UA/Pumps/",
                SchemaName = "Temperature",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/temperature-v1",
                ResourceUri = "https://schemas.example.test/temperature",
                MakeDefault = true
            };
            TypedSchemaReadResultDataType result = await store.RegisterAsync(
                new TypedSchemaRegistrationRequestDataType
                {
                    Registration = registration,
                    Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
                }).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Document.Reference.Entity.Xid,
                Is.EqualTo("/schemagroups/org.contoso.UA.Pumps/schemas/Temperature.jsonschema/versions/1"));
            RegistryStoredState committed = await storage.ReadAsync().ConfigureAwait(false);
            Assert.That(committed.Revision, Is.EqualTo(1), "Source identities, Version and default commit together.");
            using var recovered = Store(storage);
            await recovered.StartAsync().ConfigureAwait(false);
            Assert.That(recovered.Entries[0].Registration!.SchemaName, Is.EqualTo("Temperature"));
            SchemaReferenceDataType logical = recovered.ReferenceForXid(
                "/schemagroups/org.contoso.UA.Pumps/schemas/Temperature.jsonschema");
            Assert.That(logical.EntityUri, Is.EqualTo(registration.ResourceUri));
            Assert.That(recovered.Read(logical).Document.Epoch, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedRegistrationPublishesNeitherSubjectNorDefaultAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "urn:new:schemas",
                SchemaName = "NeverPublished",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/v1",
                MakeDefault = true,
                ResourceUri = "not-an-absolute-uri"
            };
            TypedSchemaReadResultDataType result = await store.RegisterAsync(
                new TypedSchemaRegistrationRequestDataType
                {
                    Registration = registration,
                    Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
                }).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(store.Entries.Count, Is.Zero);
            Assert.That((await storage.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        [Test]
        public async Task NativeRegistrationNoOpRetainsRawBytesAndTheCommittedRevisionAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "urn:schemas:example",
                SchemaName = "Temperature",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/v1"
            };
            ByteString original = ByteString.From("{ \"type\" : \"number\" }\n"u8.ToArray());
            TypedSchemaReadResultDataType first = await store.RegisterRawAsync(registration, original)
                .ConfigureAwait(false);
            registration.ExpectedEpoch = first.Document.Epoch;
            TypedSchemaReadResultDataType noOp = await store.RegisterAsync(new TypedSchemaRegistrationRequestDataType
            {
                Registration = registration,
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
            }).ConfigureAwait(false);

            Assert.That(noOp.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(noOp.Document.Epoch, Is.EqualTo(first.Document.Epoch));
            Assert.That(store.DocumentBytes(noOp.Document.Reference), Is.EqualTo(original));
            Assert.That((await storage.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(1));
        }

        [Test]
        public async Task CollidingNamespaceIdentityIsRejectedAcrossDifferentSubjectsAsync()
        {
            await using var storage = new MemoryRegistryStateStore();
            using var store = Store(storage);
            await store.StartAsync().ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "http://contoso.org/UA/Pumps/",
                SchemaName = "Temperature",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/v1"
            };
            TypedSchemaReadResultDataType first = await store.RegisterRawAsync(registration,
                ByteString.From("""{"type":"number"}"""u8.ToArray())).ConfigureAwait(false);
            registration.NamespaceUri = "https://contoso.org/UA/Pumps/";
            registration.SchemaName = "Pressure";
            registration.EntityUri = "https://schemas.example.test/pressure-v1";
            TypedSchemaReadResultDataType collision = await store.RegisterRawAsync(registration,
                ByteString.From("""{"type":"string"}"""u8.ToArray())).ConfigureAwait(false);

            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(collision.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(collision.Issues[0].Detail, Does.Contain("symbolic identity collides"));
            Assert.That(store.Entries.Count, Is.EqualTo(1));
            Assert.That((await storage.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(1));
        }

        private static SchemaRegistryStore Store(IRegistryStateStore storage)
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.SchemaRegistry);
            context.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().Commit();
            return new SchemaRegistryStore(
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider(), new ArrowSchemaFormatProvider()],
                storage, context, new RegistryEntityReferenceDataType { OriginUri = "urn:test:schemas" },
                (uri, selector, _) => uri + "#" + selector);
        }

        private static SchemaReferenceDataType Logical(string name)
        {
            SchemaReferenceDataType reference = Reference(name, "1");
            reference.Entity.Xid = "/schemagroups/g/schemas/" + name;
            reference.Entity.Role = "LogicalResource";
            reference.EntityUri = "https://schemas.example.test" + reference.Entity.Xid;
            reference.SelectedObjectUri = reference.EntityUri;
            return reference;
        }

        private static SchemaReferenceDataType Reference(string name, string version)
        {
            string xid = "/schemagroups/g/schemas/" + name + "/versions/" + version;
            return new SchemaReferenceDataType
            {
                Entity = new RegistryEntityReferenceDataType
                {
                    OriginUri = "urn:test:schemas",
                    Xid = xid,
                    Role = "ExactVersion"
                },
                EntityUri = "https://schemas.example.test" + xid,
                SelectedObjectUri = "https://schemas.example.test" + xid,
                Selector = string.Empty,
                Format = "JsonSchema/2020-12"
            };
        }
    }
}
