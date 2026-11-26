// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Dependency;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Verifies dependency pruning before payload decoding and model materialization.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ModelDependencyResolverTests
    {
        /// <summary>
        /// Every namespace-bearing payload field can introduce a transitive dependency absent from the local imports.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The relationship is not a namespace-bearing field.</exception>
        [TestCase("Symbolic")]
        [TestCase("BaseType")]
        [TestCase("DataType")]
        [TestCase("Field")]
        [TestCase("BrowseName")]
        [TestCase("ChildType")]
        [TestCase("ChildDataType")]
        [TestCase("ReferenceType")]
        [TestCase("MethodState")]
        [TestCase("MethodDeclaration")]
        [TestCase("InputArgument")]
        [TestCase("OutputArgument")]
        [TestCase("Value")]
        [TestCase("Role")]
        [TestCase("RoleId")]
        [TestCase("DefaultRole")]
        public void ResolvesTransitiveMetadataWithoutDecodingUnrelatedModels(string relationship)
        {
            var primary = new ModelDependencyV1 { ModelUri = "urn:primary" };
            var node = new DependencyNode { SymbolicName = "Primary", SymbolicNamespace = primary.ModelUri };
            var child = new DependencyChild { SymbolicName = "Nested", InstanceKind = 4 };
            node.Children = [new DependencyChild { SymbolicName = "Container", Children = [child] }];
            primary.Nodes.Add(node);
            const string transitiveUri = "urn:transitive";
            switch (relationship)
            {
                case "Symbolic":
                    node.SymbolicNamespace = transitiveUri;
                    break;
                case "BaseType":
                    node.BaseTypeNamespace = transitiveUri;
                    break;
                case "DataType":
                    node.DataTypeName = "Value";
                    node.DataTypeNamespace = transitiveUri;
                    node.Kind = DependencyNodeKind.VariableType;
                    break;
                case "Field":
                    node.Fields = [new DependencyDataField("Field", "Value", transitiveUri, -1)];
                    break;
                case "BrowseName":
                    child.BrowseNameNamespace = transitiveUri;
                    break;
                case "ChildType":
                    child.TypeDefinitionNamespace = transitiveUri;
                    break;
                case "ChildDataType":
                    child.DataTypeNamespace = transitiveUri;
                    break;
                case "ReferenceType":
                    child.ReferenceTypeNamespace = transitiveUri;
                    break;
                case "MethodState":
                    child.MethodStateName = "Invoke";
                    child.MethodStateNamespace = transitiveUri;
                    break;
                case "MethodDeclaration":
                    child.MethodDeclarationNamespace = transitiveUri;
                    break;
                case "InputArgument":
                    child.InputArguments = [new DependencyMethodArg("Input", "Value", transitiveUri, -1)];
                    break;
                case "OutputArgument":
                    child.OutputArguments = [new DependencyMethodArg("Output", "Value", transitiveUri, -1)];
                    break;
                case "Value":
                    child.DefaultValueNamespaceUris = [ModelDependencyV1.OpcUaNamespaceUri, transitiveUri];
                    break;
                case "Role":
                    node.Security = new DependencySecurity
                    {
                        RolePermissions = CreatePermissions(transitiveUri)
                    };
                    node.Security.RolePermissions.RolePermission[0].RoleId = null!;
                    break;
                case "RoleId":
                    node.Security = new DependencySecurity
                    {
                        RolePermissions = CreatePermissions(transitiveUri)
                    };
                    node.Security.RolePermissions.RolePermission[0].Role = null!;
                    break;
                case "DefaultRole":
                    child.Security = new DependencySecurity
                    {
                        DefaultRolePermissions = CreatePermissions(transitiveUri)
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(relationship));
            }
            var transitive = new ModelDependencyV1 { ModelUri = transitiveUri };
            transitive.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Cycle",
                SymbolicNamespace = transitiveUri,
                BaseTypeNamespace = primary.ModelUri
            });
            Dictionary<string, ModelDependencyReference> references = CreateReferences(primary, transitive);
            references.Add("urn:unreadable", CreateUnreadableReference());
            var resolver = new ModelDependencyResolver(references);

            string[] resolved = [.. resolver.Resolve(
                [new Namespace { Value = primary.ModelUri }], CancellationToken.None).Select(model => model.ModelUri)];

            Assert.That(resolved, Is.EquivalentTo([primary.ModelUri, transitiveUri]));
        }

        /// <summary>
        /// The local design is authoritative even when the corresponding assembly payload cannot be decoded safely.
        /// </summary>
        [Test]
        public void ExplicitFilesAndBuiltInModelAreNotDecoded()
        {
            ModelDependencyReference unreadable = CreateUnreadableReference();
            var references = new Dictionary<string, ModelDependencyReference>
            {
                [unreadable.ModelUri] = unreadable,
                [ModelDependencyV1.OpcUaNamespaceUri] = unreadable
            };
            var resolver = new ModelDependencyResolver(references);

            Assert.That(resolver.Resolve(
                [
                    new Namespace { Value = unreadable.ModelUri, FilePath = "Explicit.xml" },
                    new Namespace { Value = ModelDependencyV1.OpcUaNamespaceUri }
                ], CancellationToken.None), Is.Empty);
        }

        /// <summary>
        /// Unreadable security still fails when the payload is actually required.
        /// </summary>
        [Test]
        public void RequiredSecurityFailureIsNotSuppressed()
        {
            ModelDependencyReference unreadable = CreateUnreadableReference();
            var resolver = new ModelDependencyResolver(new Dictionary<string, ModelDependencyReference>
            {
                [unreadable.ModelUri] = unreadable
            });

            Assert.That(() => resolver.Resolve(
                [new Namespace { Value = unreadable.ModelUri }], CancellationToken.None).ToArray(),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("cannot preserve security metadata"));
        }

        /// <summary>
        /// Missing, payloadless, malformed, unknown-version and mismatched dependencies retain fallback behavior.
        /// </summary>
        [Test]
        public void InvalidLegacyPayloadsRemainUnresolved()
        {
            Dictionary<string, ModelDependencyReference> references = CreateReferences(
                new ModelDependencyV1 { ModelUri = "urn:other" });
            references["urn:mismatched"] = references["urn:other"];
            references["urn:empty"] = CreateReference("urn:empty", string.Empty);
            references["urn:malformed"] = CreateReference("urn:malformed", "not-base64");
            references["urn:unknown"] = CreateReference("urn:unknown", Convert.ToBase64String([0xAA, 0xC7, 99, 1]));
            var resolver = new ModelDependencyResolver(references);

            Assert.That(resolver.Resolve(
                [
                    new Namespace { Value = "urn:missing" },
                    new Namespace { Value = "urn:mismatched" },
                    new Namespace { Value = "urn:empty" },
                    new Namespace { Value = "urn:malformed" },
                    new Namespace { Value = "urn:unknown" }
                ], CancellationToken.None), Is.Empty);
        }

        /// <summary>
        /// Old values without a producer namespace table conservatively keep the previous dependency set.
        /// </summary>
        [Test]
        public void LegacyValuesWithoutNamespaceTablesKeepAllDependencies()
        {
            var primary = new ModelDependencyV1 { ModelUri = "urn:legacy" };
            primary.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Legacy",
                Children = [new DependencyChild { SymbolicName = "Value", DefaultValueXml = "<Value />" }]
            });
            var resolver = new ModelDependencyResolver(CreateReferences(
                primary, new ModelDependencyV1 { ModelUri = "urn:possibly-used" }));

            Assert.That(resolver.Resolve(
                [new Namespace { Value = primary.ModelUri }], CancellationToken.None).Select(model => model.ModelUri),
                Is.EquivalentTo([primary.ModelUri, "urn:possibly-used"]));
        }

        /// <summary>
        /// Cancelling enumeration interrupts transitive traversal without retaining a cancelled token in the cache.
        /// </summary>
        [Test]
        public void CancellationInterruptsClosureAndCanBeRetried()
        {
            var primary = new ModelDependencyV1 { ModelUri = "urn:primary" };
            primary.Nodes.Add(new DependencyNode { BaseTypeNamespace = "urn:transitive" });
            var resolver = new ModelDependencyResolver(CreateReferences(
                primary, new ModelDependencyV1 { ModelUri = "urn:transitive" }));
            Namespace[] namespaces = [new Namespace { Value = primary.ModelUri }];
            using var cancellation = new CancellationTokenSource();
            using IEnumerator<ModelDependencyV1> iterator = resolver.Resolve(namespaces, cancellation.Token)
                .GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => iterator.MoveNext());
            Assert.That(resolver.Resolve(namespaces, CancellationToken.None).Count(), Is.EqualTo(2));
        }

        /// <summary>
        /// Both input paths skip an unrelated unreadable graph without changing generated model sources.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GenerationPrunesUnrelatedPayloadsAndPreservesOutput(bool nodeSet)
        {
            Dictionary<string, string> expected = Generate(nodeSet, null);
            ModelDependencyReference unreadable = CreateUnreadableReference();
            Dictionary<string, string> actual = Generate(nodeSet, new Dictionary<string, ModelDependencyReference>
            {
                [unreadable.ModelUri] = unreadable
            });

            Assert.That(expected, Is.Not.Empty);
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (KeyValuePair<string, string> file in expected)
            {
                Assert.That(string.Equals(actual[file.Key], file.Value, StringComparison.Ordinal),
                    Is.True, $"Generated source changed: {file.Key}");
            }
        }

        /// <summary>
        /// An unrelated large model does not allocate its expanded graph during closure resolution.
        /// </summary>
        [Test]
        public void PruningAvoidsLargeUnrelatedDecodeAllocations()
        {
            var unused = new ModelDependencyV1 { ModelUri = "urn:unused:" + Guid.NewGuid().ToString("N") };
            unused.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Unused",
                Children = [.. Enumerable.Range(0, 10000).Select(index => new DependencyChild
                {
                    SymbolicName = "Child" + index,
                    TypeDefinitionName = "BaseDataVariableType",
                    TypeDefinitionNamespace = ModelDependencyV1.OpcUaNamespaceUri
                })]
            });
            Dictionary<string, ModelDependencyReference> references = CreateReferences(unused);
            var resolver = new ModelDependencyResolver(references);
            Namespace[] namespaces = [new Namespace { Value = "urn:local", FilePath = "Local.xml" }];
            Assert.That(resolver.Resolve(namespaces, CancellationToken.None).ToArray(), Is.Empty);

            long before = GC.GetAllocatedBytesForCurrentThread();
            ModelDependencyV1[] selected = [.. resolver.Resolve(namespaces, CancellationToken.None)];
            long prunedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            ModelDependencyV1? decoded = references[unused.ModelUri].GetDependencyForGeneration();
            long decodedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(selected, Is.Empty);
            Assert.That(decoded!.Nodes[0].Children, Has.Count.EqualTo(10000));
            Assert.That(prunedBytes, Is.LessThan(decodedBytes / 10),
                $"Pruning allocated {prunedBytes} bytes; decoding allocated {decodedBytes} bytes.");
            GC.KeepAlive(decoded);
        }

        private static Dictionary<string, string> Generate(
            bool nodeSet,
            IReadOnlyDictionary<string, ModelDependencyReference>? references)
        {
            using var files = new VirtualFileSystem();
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Pruning.xml");
            files.Add(path, Encoding.UTF8.GetBytes(nodeSet ? kNodeSet : kDesign));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            var options = new GeneratorOptions { OmitFluentApi = true, EmitDependencyMetadata = false };
            if (nodeSet)
            {
                var nodesets = new NodesetFileCollection(
                    [(path, new NodesetFileOptions())], [], files, telemetry);
                nodesets.GenerateCode(files, string.Empty, telemetry, options, referencedModels: references);
            }
            else
            {
                new DesignFileCollection { Targets = [path] }.GenerateCode(
                    files, string.Empty, telemetry, options, referencedModels: references);
            }
            return files.CreatedFiles.Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
                .ToDictionary(file => file, file => Encoding.UTF8.GetString(files.Get(file)), StringComparer.Ordinal);
        }

        private static RolePermissionSet CreatePermissions(string uri)
        {
            return new RolePermissionSet
            {
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Readers", uri),
                        RoleId = "nsu=" + uri + ";s=Readers",
                        Permission = [Permissions.Read]
                    }
                ]
            };
        }

        private static Dictionary<string, ModelDependencyReference> CreateReferences(params ModelDependencyV1[] models)
        {
            return models.ToDictionary(model => model.ModelUri,
                model => CreateReference(model.ModelUri, model.ToBase64Payload()), StringComparer.Ordinal);
        }

        private static ModelDependencyReference CreateReference(string uri, string payload)
        {
            return new ModelDependencyReference("Producer", uri, "Producer", string.Empty, string.Empty, payload: payload);
        }

        private static ModelDependencyReference CreateUnreadableReference()
        {
            var model = new ModelDependencyV1 { ModelUri = "urn:unreadable" };
            model.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Protected",
                Security = new DependencySecurity { WriteAccess = 1 }
            });
            byte[] payload = Convert.FromBase64String(model.ToBase64Payload());
            using var input = new MemoryStream(payload, 4, payload.Length - 4, writable: false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            using var body = new MemoryStream();
            inflater.CopyTo(body);
            byte[] bytes = body.ToArray();
            int marker = bytes.AsSpan().IndexOf("SEC1"u8);
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            bytes[marker + 4] = byte.MaxValue;
            using var output = new MemoryStream();
            output.Write(payload, 0, 4);
            using (var deflater = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflater.Write(bytes, 0, bytes.Length);
            }
            return CreateReference(model.ModelUri, Convert.ToBase64String(output.ToArray()));
        }

        private const string kDesign =
            """
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/" xmlns:prune="urn:pruning"
              TargetNamespace="urn:pruning" TargetVersion="1.0.0" TargetPublicationDate="2024-01-01T00:00:00Z">
              <opc:Namespaces>
                <opc:Namespace Name="Pruning" Prefix="Pruning">urn:pruning</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="prune:DeviceType" BaseType="ua:BaseObjectType" />
            </opc:ModelDesign>
            """;

        private const string kNodeSet =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:pruning</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="urn:pruning" Version="1.0.0" PublicationDate="2024-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.02"
                    PublicationDate="2022-11-01T00:00:00Z" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1001" BrowseName="1:DeviceType">
                <DisplayName>DeviceType</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
              </UAObjectType>
            </UANodeSet>
            """;
    }
}
