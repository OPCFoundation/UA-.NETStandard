/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    /// <summary>Correspondence is checked against the independent pinned Python oracle, not C# expectations.</summary>
    [TestFixture]
    public sealed class OracleCorrespondenceTests
    {
        [TestCaseSource(nameof(Cases))]
        public void NativeCorrespondenceMatchesIndependentPythonOracle(string name)
        {
            OracleVector vector = OracleVector.Find(name);
            PubSubConnectionDataType connection = vector.Connection();
            WriterGroupDataType? group = connection.WriterGroups.Count == 0 ? null : connection.WriterGroups[0];
            DataSetWriterDataType? writer = vector.Role == "writer" ? group!.DataSetWriters[0] : null;
            DataSetReaderDataType? reader = vector.Role == "reader" ? connection.ReaderGroups[0].DataSetReaders[0] : null;
            ArrayOf<RegistryDiagnosticDataType> endpoint = PubSubBindingRules.CheckEndpoint(vector.Endpoint,
                connection, group, writer, reader);
            ArrayOf<RegistryDiagnosticDataType> message = PubSubBindingRules.CheckMessage(vector.Message,
                connection, group, reader is null ? group!.DataSetWriters[0] : null, reader, vector.Container);
            Assert.Multiple(() =>
            {
                Assert.That(Codes(endpoint), Is.EqualTo(vector.Expected("endpointIssues")), name + " Endpoint");
                Assert.That(Codes(message), Is.EqualTo(vector.Expected("messageIssues")), name + " Message");
            });
        }

        [TestCase("a/#", "a", true)]
        [TestCase("a/+", "a/", true)]
        [TestCase("+/status", "$sys/status", false)]
        [TestCase("#", "$sys/status", false)]
        [TestCase("$sys/#", "$sys/status", true)]
        [TestCase("a/+/c", "a/b/c", true)]
        [TestCase("a/+", "a/b/c", false)]
        [TestCase("a/#/b", "a/b", false)]
        public void NativeMqttFilterBoundariesArePreserved(string filter, string topic, bool matches)
        {
            Assert.That(PubSubBindingRules.TopicMatches(filter, topic), Is.EqualTo(matches));
        }

        private static IEnumerable<string> Cases()
        {
            foreach (OracleVector vector in OracleVector.All())
            {
                yield return vector.Name;
            }
        }

        internal static string[] Codes(ArrayOf<RegistryDiagnosticDataType> diagnostics)
        {
            var result = new string[diagnostics.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = diagnostics[index].Code!;
            }
            return result;
        }
    }

    internal sealed class OracleVector(JsonElement value)
    {
        public string Name => value.GetProperty("name").GetString()!;

        public string Role => value.GetProperty("role").GetString()!;

        public RegistryObjectValueDataType Endpoint => ReadObject(value.GetProperty("endpoint"));

        public RegistryObjectValueDataType Message => ReadObject(value.GetProperty("message"));

        public RegistryObjectValueDataType? Container => value.GetProperty("container").ValueKind == JsonValueKind.Null
            ? null : ReadObject(value.GetProperty("container"));

        public static OracleVector Find(string name)
        {
            foreach (OracleVector vector in All())
            {
                if (vector.Name == name)
                {
                    return vector;
                }
            }
            throw new ArgumentException("The pinned vector was not found.", nameof(name));
        }

        public static IEnumerable<OracleVector> All()
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Vectors", "correspondence.json")));
            foreach (JsonElement item in document.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return new OracleVector(item.Clone());
            }
        }

        public string[] Expected(string kind)
        {
            var result = new List<string>();
            foreach (JsonElement item in value.GetProperty(kind).EnumerateArray())
            {
                result.Add(item.GetString()!);
            }
            return result.ToArray();
        }

        public PubSubConnectionDataType Connection()
        {
            return Decode<PubSubConnectionDataType>(value.GetProperty("connectionBinary").GetString()!);
        }

        public PubSubConfigurationDataType Configuration()
        {
            var datasets = new List<PublishedDataSetDataType>();
            foreach (JsonElement item in value.GetProperty("publishedDataSetsBinary").EnumerateArray())
            {
                datasets.Add(Decode<PublishedDataSetDataType>(item.GetString()!));
            }
            return new PubSubConfigurationDataType { Connections = [Connection()], PublishedDataSets = datasets.ToArray() };
        }

        private static T Decode<T>(string binary) where T : IEncodeable, new()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:example:factory");
            context.NamespaceUris.Append("urn:example:analytics");
            using var stream = new MemoryStream(Convert.FromBase64String(binary));
            using var decoder = new BinaryDecoder(stream, context, true);
            return decoder.ReadEncodeable<T>(null);
        }

        private static RegistryObjectValueDataType ReadObject(JsonElement element)
        {
            return (RegistryObjectValueDataType)RegistryValues.Parse(
                System.Text.Encoding.UTF8.GetBytes(element.GetRawText()));
        }
    }
}
