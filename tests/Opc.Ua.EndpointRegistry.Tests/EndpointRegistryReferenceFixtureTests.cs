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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Tests.NativeTestSupport;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// Replays the specification's example documents and representation vectors. The expected native
    /// records were produced by the specification's reference projection (registry_native_values.py) and are
    /// checked in as Assets/native-fixtures.json by tools/GenerateRegistryNativeCatalog.py.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class EndpointRegistryReferenceFixtureTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (string id in s_fixtures.Value.Cases.Keys)
            {
                yield return new TestCaseData(id).SetArgDisplayNames(id);
            }
        }

        [Test]
        public void FixturesCarryTheirProvenance()
        {
            JsonElement header = s_fixtures.Value.Header;

            Assert.Multiple(() =>
            {
                Assert.That(header.GetProperty("format").GetString(), Is.EqualTo("RegistryNativeFixtures/1.0"));
                Assert.That(header.GetProperty("commit").GetString(), Has.Length.EqualTo(40));
                Assert.That(header.GetProperty("uncommittedSource").ValueKind,
                    Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
                Assert.That(s_fixtures.Value.Cases, Has.Count.GreaterThanOrEqualTo(79));
            });
        }

        public static IEnumerable<TestCaseData> ProviderOwnedCases()
        {
            foreach (KeyValuePair<string, JsonElement> item in s_fixtures.Value.Cases)
            {
                if (item.Value.TryGetProperty("native", out JsonElement native) && HasProviderContent(native))
                {
                    yield return new TestCaseData(item.Key).SetArgDisplayNames(item.Key);
                }
            }
        }

        /// <summary>
        /// Compares every mapper-owned field with the reference record. Inline JSON Schema and Avro content
        /// is owned by the schema format providers; here only its selected DataType and Format are compared.
        /// </summary>
        [TestCaseSource(nameof(Cases))]
        public void ProjectsToTheReferenceRecordAndRestoresExactly(string id)
        {
            JsonElement fixture = s_fixtures.Value.Cases[id];
            string type = fixture.GetProperty("type").GetString()!;
            RegistryValueDataType document = Document(fixture);

            if (!fixture.GetProperty("representable").GetBoolean())
            {
                RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                    () => Mapper.Project(document, type))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                    fixture.GetProperty("error").GetString());
                return;
            }
            RegistryRecordDataType record = Mapper.Project(document, type);

            using JsonDocument actual = JsonDocument.Parse(Render(record));
            Assert.Multiple(() =>
            {
                Assert.That(Difference(fixture.GetProperty("native"), actual.RootElement, type, false), Is.Null);
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(record)),
                    Is.EqualTo(fixture.GetProperty("restoresExactly").GetBoolean()));
            });
        }

        [TestCaseSource(nameof(ProviderOwnedCases))]
        public void ProviderOwnedSchemaContentMatchesTheReferenceRecord(string id)
        {
            JsonElement fixture = s_fixtures.Value.Cases[id];
            RegistryRecordDataType record = Mapper.Project(Document(fixture), fixture.GetProperty("type").GetString()!);

            using JsonDocument actual = JsonDocument.Parse(Render(record));

            Assert.That(Difference(fixture.GetProperty("native"), actual.RootElement, "$", true), Is.Null);
        }

        [Test]
        public void TwoFixturesCarryProviderOwnedSchemaContent()
        {
            Assert.That(ProviderOwnedCases().Count(), Is.EqualTo(2));
        }

        private static RegistryValueDataType Document(JsonElement fixture)
        {
            return RegistryValues.Parse(Encoding.UTF8.GetBytes(fixture.GetProperty("document").GetRawText()));
        }

        private static bool HasProviderContent(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    return (value.TryGetProperty("$type", out JsonElement type) &&
                        s_providerOwned.Contains(type.GetString()!)) ||
                        value.EnumerateObject().Any(member => HasProviderContent(member.Value));
                case JsonValueKind.Array:
                    return value.EnumerateArray().Any(HasProviderContent);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Finds the first difference between two native renderings; field arrays are addressed by field name.
        /// </summary>
        private static string? Difference(JsonElement expected, JsonElement actual, string path, bool providers)
        {
            if (expected.ValueKind != actual.ValueKind)
            {
                return $"{path}: expected {expected.GetRawText()} but was {actual.GetRawText()}";
            }
            switch (expected.ValueKind)
            {
                case JsonValueKind.Object when !providers && expected.TryGetProperty("$type", out JsonElement type) &&
                    s_providerOwned.Contains(type.GetString()!):
                    return actual.TryGetProperty("$type", out JsonElement other) &&
                        string.Equals(type.GetString(), other.GetString(), StringComparison.Ordinal) &&
                        string.Equals(Format(expected), Format(actual), StringComparison.Ordinal)
                        ? null
                        : $"{path}: expected {type.GetString()} {Format(expected)} but was {actual.GetRawText()}";
                case JsonValueKind.Object:
                    JsonProperty[] members = [.. expected.EnumerateObject()];
                    JsonProperty[] others = [.. actual.EnumerateObject()];
                    if (!members.Select(member => member.Name).SequenceEqual(others.Select(member => member.Name)))
                    {
                        return $"{path}: expected {expected.GetRawText()} but was {actual.GetRawText()}";
                    }
                    for (int index = 0; index < members.Length; index++)
                    {
                        string? difference = Difference(members[index].Value, others[index].Value,
                            members[index].Name == "fields" ? path : path + "." + members[index].Name, providers);
                        if (difference is not null)
                        {
                            return difference;
                        }
                    }
                    return null;
                case JsonValueKind.Array:
                    JsonElement[] items = [.. expected.EnumerateArray()];
                    JsonElement[] values = [.. actual.EnumerateArray()];
                    for (int index = 0; index < Math.Min(items.Length, values.Length); index++)
                    {
                        string? difference = Difference(items[index], values[index], path + Step(items[index], index),
                            providers);
                        if (difference is not null)
                        {
                            return difference;
                        }
                    }
                    return items.Length == values.Length
                        ? null
                        : $"{path}: expected {items.Length} items but was {values.Length}";
                case JsonValueKind.String:
                    return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal)
                        ? null
                        : $"{path}: expected {expected.GetRawText()} but was {actual.GetRawText()}";
                case JsonValueKind.Number:
                    return string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal)
                        ? null
                        : $"{path}: expected {expected.GetRawText()} but was {actual.GetRawText()}";
                default:
                    return null;
            }
        }

        private static string? Format(JsonElement structure)
        {
            foreach (JsonElement field in structure.GetProperty("fields").EnumerateArray())
            {
                if (field[0].GetString() == "Format")
                {
                    return field[1].GetString();
                }
            }
            return null;
        }

        private static string Step(JsonElement item, int index)
        {
            return item.ValueKind == JsonValueKind.Array && item.GetArrayLength() == 2 &&
                item[0].ValueKind == JsonValueKind.String
                ? "." + item[0].GetString()
                : "[" + index + "]";
        }

        private static Fixtures Load()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(typeof(EndpointRegistryReferenceFixtureTests).Assembly.Location)!,
                "Assets",
                "native-fixtures.json");
            using JsonDocument file = JsonDocument.Parse(File.ReadAllBytes(path));
            var cases = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonElement item in file.RootElement.GetProperty("cases").EnumerateArray())
            {
                cases.Add(item.GetProperty("id").GetString()!, item.Clone());
            }
            return new Fixtures(file.RootElement.GetProperty("header").Clone(), cases);
        }

        private sealed record Fixtures(JsonElement Header, Dictionary<string, JsonElement> Cases);

        private static readonly Lazy<Fixtures> s_fixtures = new(Load);
        private static readonly HashSet<string> s_providerOwned =
            new(StringComparer.Ordinal) { "JsonSchemaContentDataType", "AvroSchemaContentDataType" };
    }
}
