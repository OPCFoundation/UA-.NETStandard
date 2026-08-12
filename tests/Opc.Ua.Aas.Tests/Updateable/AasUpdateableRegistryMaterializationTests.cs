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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Tests.Updateable
{
    /// <summary>
    /// Tests the updateable AAS registry profile.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasUpdateableRegistryMaterializationTests
    {
        [Test]
        public async Task ShadowGenerationStaysInvisibleUntilAtomicSwitch()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);
            store.Replace(Document("doc", "v2", Environment("two")));
            host.OnShadowPrepared = () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(host.ShadowIsBrowsable, Is.False);
                    Assert.That(host.BrowseGeneration, Is.EqualTo(1u));
                });
            };

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Generation, Is.EqualTo(2u));
                Assert.That(host.BrowseGeneration, Is.EqualTo(2u));
                Assert.That(host.ShadowReloads, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task GracefulRetirementServesExistingMonitoredItemUntilDrain()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);
            host.CreateMonitoredItem();
            store.Replace(Document("doc", "v2", Environment("two")));

            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(host.ReadExistingMonitoredItem(), Is.EqualTo(ServiceResult.Good));
                Assert.That(host.OldGenerationDraining, Is.True);
            });
            host.Drain();
            Assert.That(host.OldGenerationDraining, Is.False);
        }

        [Test]
        public async Task ImmediateRetirementInvalidatesExistingMonitoredItem()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(
                store,
                host,
                retirementPolicy: AasProjectionRetirementPolicy.Immediate);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);
            host.CreateMonitoredItem();
            store.Replace(Document("doc", "v2", Environment("two")));

            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);

            Assert.That(host.ReadExistingMonitoredItem().StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task UnchangedDigestSkipsProjectionWhileForceRebuilds()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);

            AasMaterializeResult unchanged = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);
            AasMaterializeResult forced = await coordinator.MaterializeAsync(
                new AasMaterializeRequest { Force = true }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(unchanged.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Unchanged));
                Assert.That(unchanged.Generation, Is.EqualTo(1u));
                Assert.That(forced.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Materialized));
                Assert.That(forced.Generation, Is.EqualTo(2u));
                Assert.That(host.ShadowReloads, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task MissingClosureMemberActivatesNothing()
        {
            var store = new InMemoryDocumentStore(ShellDocument("shell", "v1", "missingSubmodel"));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Failed));
                Assert.That(host.Adds, Is.Zero);
                Assert.That(store.States[0].LoadState, Is.EqualTo(AasLoadState.Failed));
            });
        }

        [Test]
        public async Task CyclicClosureReportsDeterministicDiagnosticAndActivatesNothing()
        {
            AasMaterializationDocument left = Document("left", "v1", Environment("left"));
            AasMaterializationDocument right = Document("right", "v1", Environment("right"));
            left.RequiredDocumentIds = new ArrayOf<string>(s_rightOnly);
            right.RequiredDocumentIds = new ArrayOf<string>(s_leftOnly);
            var store = new InMemoryDocumentStore(left, right);
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Results, Has.Count.EqualTo(2));
                Assert.That(result.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Failed));
                Assert.That(result.Results[0].Diagnostic, Does.Contain("left -> right -> left"));
                Assert.That(host.Adds, Is.Zero);
            });
        }

        [Test]
        public async Task ValidationFailureKeepsPreviousGenerationAndDivergesVersions()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);
            store.Replace(new AasMaterializationDocument
            {
                Xid = "doc",
                VersionId = "bad",
                Content = ByteString.From(new byte[] { (byte)'{' }),
                Format = "aas/3.0+json"
            });

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Generation, Is.EqualTo(1u));
                Assert.That(result.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Failed));
                Assert.That(store.States[0].DesiredVersionId, Is.EqualTo("bad"));
                Assert.That(store.States[0].ActiveVersionId, Is.EqualTo("v1"));
            });
        }

        [Test]
        public async Task ModelChangeEventIsCommittedOnly()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            int events = 0;
            uint generation = 0;
            coordinator.ModelChangeCommitted += (_, e) =>
            {
                events++;
                generation = e.Generation;
            };

            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(events, Is.EqualTo(1));
                Assert.That(generation, Is.EqualTo(1u));
            });
        }

        [Test]
        public async Task MaterializeReturnsOneResultPerConsideredDocument()
        {
            var store = new InMemoryDocumentStore(
                Document("left", "v1", Environment("left")),
                Document("right", "v1", Environment("right")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.That(result.Results, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task BoundsRejectOversizedDocumentWithDiagnostic()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(
                store,
                host,
                new AasMaterializationBounds
                {
                    MaxDocumentBytes = 8,
                    MaxElements = 10,
                    MaxNestingDepth = 10,
                    MaxShadowGenerations = 1
                });

            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Failed));
                Assert.That(result.Results[0].Diagnostic, Does.Contain("size bound"));
                Assert.That(host.Adds, Is.Zero);
            });
        }

        [Test]
        public async Task ValueWriteBackBumpsVersionWithoutRedundantMaterialization()
        {
            var store = new InMemoryDocumentStore(Document("doc", "v1", Environment("one")));
            var host = new RecordingProjectionHost();
            using var coordinator = new AasMaterializationCoordinator(store, host);
            await coordinator.MaterializeAsync(new AasMaterializeRequest()).ConfigureAwait(false);

            await coordinator.WriteBackValueAsync(new AasValueWriteBackRequest
            {
                Xid = "doc",
                ElementPath = "property",
                MemberName = "Value",
                Value = new Variant("updated"),
                SourceGeneration = 1
            }).ConfigureAwait(false);
            AasMaterializeResult result = await coordinator.MaterializeAsync(new AasMaterializeRequest())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(store.VersionBumps, Is.EqualTo(1));
                Assert.That(result.Results[0].Outcome, Is.EqualTo(AasMaterializationOutcome.Unchanged));
                Assert.That(host.ShadowReloads, Is.Zero);
            });
        }

        [Test]
        public async Task EnvironmentExportIsFilteredPerSessionWithNoDigest()
        {
            var policy = new Mock<IAasEnvironmentExportAccessPolicy>(MockBehavior.Strict);
            policy.Setup(p => p.CanRead(null, It.Is<string>(path => !path.EndsWith("secret", StringComparison.Ordinal))))
                .Returns(true);
            policy.Setup(p => p.CanRead(null, It.Is<string>(path => path.EndsWith("secret", StringComparison.Ordinal))))
                .Returns(false);
            var exporter = new AasEnvironmentExporter(policy.Object);

            AasEnvironmentExportResult result = await exporter.ExportAsync(
                Environment("public", "secret"),
                new AasEnvironmentExportRequest(),
                CancellationToken.None).ConfigureAwait(false);

            string json = System.Text.Encoding.UTF8.GetString(result.Content.Memory.ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(result.Filtered, Is.True);
                Assert.That(result.Digest.IsEmpty, Is.True);
                Assert.That(json, Does.Contain("public"));
                Assert.That(json, Does.Not.Contain("secret"));
            });
        }

        private static readonly string[] s_rightOnly = ["right"];
        private static readonly string[] s_leftOnly = ["left"];

        private static AasMaterializationDocument Document(string xid, string version, AasEnvironment environment)
        {
            return new AasMaterializationDocument
            {
                Xid = xid,
                VersionId = version,
                SourceIdentity = xid,
                Kind = AasMaterializationDocumentKind.Environment,
                Content = ByteString.From(Write(environment)),
                Format = "aas/3.0+json"
            };
        }

        private static AasMaterializationDocument ShellDocument(string xid, string version, string submodelId)
        {
            AasMaterializationDocument document = Document(
                xid,
                version,
                new AasEnvironment
                {
                    AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(new ArrayOf<AasShell>(new[]
                    {
                        new AasShell
                        {
                            Id = "shell",
                            AssetInformation = new AasAssetInformation { AssetKind = AASAssetKindDataType.Instance }
                        }
                    }))
                });
            document.RequiredDocumentIds = new ArrayOf<string>(new[] { submodelId });
            return document;
        }

        private static AasEnvironment Environment(params string[] submodelIds)
        {
            var submodels = new List<AasSubmodel>();
            foreach (string submodelId in submodelIds)
            {
                submodels.Add(new AasSubmodel
                {
                    Id = submodelId,
                    IdShort = AasOptional<string>.Present(submodelId),
                    SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                        new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                        {
                            new AasProperty
                            {
                                IdShort = AasOptional<string>.Present("property"),
                                ValueType = AASDataTypeDefXsdDataType.String,
                                Value = AasOptional<Variant>.Present(new Variant("value"))
                            }
                        }))
                });
            }
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(submodels.ToArray()))
            };
        }

        private static byte[] Write(AasEnvironment environment)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            if (environment.AssetAdministrationShells.IsPresent)
            {
                writer.WriteStartArray("assetAdministrationShells");
                foreach (AasShell shell in environment.AssetAdministrationShells.Value)
                {
                    writer.WriteStartObject();
                    writer.WriteString("modelType", "AssetAdministrationShell");
                    writer.WriteString("id", shell.Id);
                    writer.WriteStartObject("assetInformation");
                    writer.WriteString("assetKind", "Instance");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            if (environment.Submodels.IsPresent)
            {
                writer.WriteStartArray("submodels");
                foreach (AasSubmodel submodel in environment.Submodels.Value)
                {
                    writer.WriteStartObject();
                    writer.WriteString("modelType", "Submodel");
                    writer.WriteString("id", submodel.Id);
                    writer.WriteString("idShort", submodel.IdShort.IsPresent ? submodel.IdShort.Value : submodel.Id);
                    writer.WriteStartArray("submodelElements");
                    if (submodel.SubmodelElements.IsPresent)
                    {
                        foreach (AasSubmodelElement element in submodel.SubmodelElements.Value)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("modelType", "Property");
                            writer.WriteString("idShort", element.IdShort.IsPresent ? element.IdShort.Value : "property");
                            writer.WriteString("valueType", "xs:string");
                            writer.WriteString("value", "value");
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            writer.Flush();
            return stream.ToArray();
        }

        private sealed class InMemoryDocumentStore : IAasMaterializationDocumentStore
        {
            public InMemoryDocumentStore(params AasMaterializationDocument[] documents)
            {
                m_documents.AddRange(documents);
            }

            public List<AasMaterializationDocumentState> States { get; } = [];

            public int VersionBumps { get; private set; }

            public ValueTask<ArrayOf<AasMaterializationDocument>> GetDocumentsAsync(
                ArrayOf<string> targets,
                CancellationToken cancellationToken = default)
            {
                if (targets.Count == 0)
                {
                    return new ValueTask<ArrayOf<AasMaterializationDocument>>(
                        new ArrayOf<AasMaterializationDocument>(m_documents.ToArray()));
                }
                var selected = new List<AasMaterializationDocument>();
                for (int i = 0; i < targets.Count; i++)
                {
                    selected.AddRange(m_documents.FindAll(document =>
                        string.Equals(document.Xid, targets[i], StringComparison.Ordinal)));
                }
                return new ValueTask<ArrayOf<AasMaterializationDocument>>(
                    new ArrayOf<AasMaterializationDocument>(selected.ToArray()));
            }

            public ValueTask ApplyMaterializationAsync(
                ArrayOf<AasMaterializationDocumentState> states,
                CancellationToken cancellationToken = default)
            {
                States.Clear();
                for (int i = 0; i < states.Count; i++)
                {
                    States.Add(states[i]);
                }
                return new ValueTask();
            }

            public ValueTask<AasMaterializationDocument> UpdateValueAsync(
                AasValueWriteBackRequest request,
                CancellationToken cancellationToken = default)
            {
                VersionBumps++;
                AasMaterializationDocument document = m_documents.Find(value =>
                    string.Equals(value.Xid, request.Xid, StringComparison.Ordinal))!;
                document.VersionId = "writeback-" + VersionBumps.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new ValueTask<AasMaterializationDocument>(document);
            }

            public void Replace(AasMaterializationDocument document)
            {
                int index = m_documents.FindIndex(value => string.Equals(value.Xid, document.Xid, StringComparison.Ordinal));
                if (index >= 0)
                {
                    m_documents[index] = document;
                }
                else
                {
                    m_documents.Add(document);
                }
            }

            private readonly List<AasMaterializationDocument> m_documents = [];
        }

        private sealed class RecordingProjectionHost : IAasEnvironmentProjectionHost
        {
            public Action? OnShadowPrepared { get; set; }
            public uint BrowseGeneration { get; private set; }
            public bool ShadowIsBrowsable { get; private set; }
            public int Adds { get; private set; }
            public int ShadowReloads { get; private set; }
            public bool OldGenerationDraining { get; private set; }

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                Adds++;
                BrowseGeneration = 1;
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                ShadowReloads++;
                ShadowIsBrowsable = false;
                OnShadowPrepared?.Invoke();
                OldGenerationDraining = m_hasMonitoredItem;
                BrowseGeneration++;
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                m_immediateRetired = true;
                BrowseGeneration++;
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask RemoveAsync(
                AasEnvironmentProjectionHandle handle,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            public void CreateMonitoredItem()
            {
                m_hasMonitoredItem = true;
            }

            public ServiceResult ReadExistingMonitoredItem()
            {
                return m_immediateRetired ? StatusCodes.BadNodeIdUnknown : ServiceResult.Good;
            }

            public void Drain()
            {
                m_hasMonitoredItem = false;
                OldGenerationDraining = false;
            }

            private static AasEnvironmentProjectionHandle CreateHandle()
            {
#pragma warning disable SYSLIB0050
                // TODO: Replace FormatterServices when NodeManagerRegistration exposes a test handle factory.
                var registration = (NodeManagerRegistration)FormatterServices.GetUninitializedObject(
                    typeof(NodeManagerRegistration));
#pragma warning restore SYSLIB0050
                return new AasEnvironmentProjectionHandle(registration);
            }

            private bool m_hasMonitoredItem;
            private bool m_immediateRetired;
        }
    }
}
