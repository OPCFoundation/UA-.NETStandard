// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection schema reference.
    /// </summary>
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        /// <summary>
        /// Verifies that standard base name and native identity must agree.
        /// </summary>
        /// <param name="nativeId">The native ID used by this test case.</param>
        /// <param name="nameForm">The name form used by this test case.</param>
        /// <param name="agrees">The agrees flag for this test case.</param>
        /// <param name="projection">The projection flag for this test case.</param>
        [Test]
        public async Task StandardBaseNameAndNativeIdentityMustAgree(
            [Values("i=11", "nsu=http://opcfoundation.org/UA/;i=11")] string nativeId,
            [Values("ua", "core", "expanded")] string nameForm,
            [Values] bool agrees,
            [Values] bool projection)
        {
            JsonObject source = Source();
            source["@context"] = DataTypeSource()["@context"]!.DeepClone();
            source["@context"]!.AsArray().Add(new JsonObject { ["core"] = Ua.Namespaces.OpcUa });
            string localName = agrees ? "Double" : "Int32";
            string name = nameForm == "expanded"
                ? "nsu=" + Ua.Namespaces.OpcUa + ";" + localName
                : nameForm + ":" + localName;
            JsonObject definition = SimpleDefinition("urn:dtd:Reading", 3000, "t:Reading");
            definition["uav:dataTypeSubtypeOf"] = new JsonObject
            {
                ["uav:dataTypeName"] = name,
                ["uav:dataTypeId"] = nativeId
            };
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Reading" };

            if (!projection)
            {
                using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(source.ToJsonString()));
                WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(document);
                Assert.That(native.Success, Is.EqualTo(agrees), string.Join("; ", native.Diagnostics));
                if (agrees)
                {
                    AssertStandardBase(native.Value!);
                }
                else
                {
                    Assert.That(native.Diagnostics.Any(
                        item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True);
                }
                return;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument? view = result.Value;
            Assert.That(result.Success, Is.EqualTo(agrees), string.Join("; ", result.Diagnostics));
            if (agrees)
            {
                WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view.Required());
                Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
                AssertStandardBase(native.Value!);
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(
                    item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True);
            }

            static void AssertStandardBase(UANodeSet nodes)
            {
                UADataType type = nodes.Items!.OfType<UADataType>().Single();
                Assert.That(type.NodeId, Does.EndWith("i=3000"));
                Reference parent = type.References!.Single(reference => reference.ReferenceType == "HasSubtype");
                Assert.That(parent.IsForward, Is.False);
                Assert.That(parent.Value, Is.EqualTo("i=11"));
            }
        }

        /// <summary>
        /// Verifies that known custom base with a standard browse name remains definitive.
        /// </summary>
        /// <param name="projection">The projection flag for this test case.</param>
        /// <param name="ambiguous">The ambiguous flag for this test case.</param>
        [Test]
        public async Task KnownCustomBaseWithAStandardBrowseNameRemainsDefinitive(
            [Values] bool projection, [Values] bool ambiguous)
        {
            JsonObject source = Source();
            source["@context"] = DataTypeSource()["@context"]!.DeepClone();
            JsonObject reading = SimpleDefinition("urn:dtd:Reading", 3000, "t:Reading");
            reading["uav:dataTypeSubtypeOf"] = new JsonObject
            {
                ["uav:dataTypeName"] = "ua:Double",
                ["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=3001"
            };
            source["uav:dataTypeDefinitions"] = new JsonArray(
                reading, SimpleDefinition("urn:dtd:CustomDouble", 3001, "ua:Double"));
            if (ambiguous)
            {
                source["uav:dataTypeDefinitions"]!.AsArray().Add(
                    SimpleDefinition("urn:dtd:OtherDouble", 3002, "ua:Double"));
            }
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Reading" };
            if (!projection)
            {
                using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(source.ToJsonString()));
                AssertCustomBase(WotNodeSetConverter.ToNodeSetResult(document), ambiguous ? 3 : 2);
                return;
            }
            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertCustomBase(WotNodeSetConverter.ToNodeSetResult(view), 2);

            static void AssertCustomBase(WotConversionResult<UANodeSet> result, int count)
            {
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
                UADataType[] types = [.. result.Value!.Items!.OfType<UADataType>()];
                Assert.That(types, Has.Length.EqualTo(count));
                UADataType derived = types.Single(type => type.NodeId.Required()
                    .EndsWith("i=3000", StringComparison.Ordinal));
                UADataType custom = types.Single(type => type.NodeId.Required()
                    .EndsWith("i=3001", StringComparison.Ordinal));
                Assert.That(derived.References!.Single(reference => reference.ReferenceType == "HasSubtype").Value,
                    Does.EndWith("i=3001"));
                Assert.That(custom.BrowseName, Is.EqualTo("Double"));
                Assert.That(custom.References!.Single(reference => reference.ReferenceType == "HasSubtype").Value,
                    Is.EqualTo("i=11"));
            }
        }
    }
}
