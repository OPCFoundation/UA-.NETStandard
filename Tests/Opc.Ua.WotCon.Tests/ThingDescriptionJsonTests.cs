/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public class ThingDescriptionJsonTests
    {
        private const string SampleTd = """
        {
          "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
          "id": "urn:test:asset001",
          "@type": ["Thing"],
          "name": "asset-001",
          "base": "sim://opcua.test/wot/asset-001",
          "title": "Test Asset",
          "description": "Test asset for unit tests",
          "properties": {
            "Voltage": {
              "type": "number",
              "title": "Line voltage",
              "description": "Phase-to-ground line voltage",
              "readOnly": true,
              "observable": true,
              "unit": "V"
            },
            "Online": {
              "type": "boolean",
              "readOnly": true,
              "observable": false
            },
            "Counter": {
              "type": "integer",
              "readOnly": false,
              "observable": false
            }
          },
          "actions": {
            "Reset": {
              "title": "Reset",
              "description": "Resets the asset",
              "input": {
                "type": "object",
                "properties": {
                  "confirm": { "type": "boolean" }
                }
              }
            },
            "SetTemperature": {
              "title": "Set Temperature",
              "input": {
                "type": "object",
                "properties": {
                  "target": {
                    "type": "number",
                    "minimum": 10.5,
                    "maximum": 30.0,
                    "unit": "degree Celsius",
                    "description": "Target temperature"
                  }
                }
              }
            }
          }
        }
        """;

        [Test]
        public void DeserializeFullThingDescriptionPopulatesPropertiesAndActions()
        {
            ThingDescription? td = JsonSerializer.Deserialize(
                SampleTd,
                ThingDescriptionJsonContext.Default.ThingDescription);

            Assert.That(td, Is.Not.Null);
            Assert.That(td!.Name, Is.EqualTo("asset-001"));
            Assert.That(td.Base, Is.EqualTo("sim://opcua.test/wot/asset-001"));
            Assert.That(td.Title, Is.EqualTo("Test Asset"));
            Assert.That(td.Description, Is.EqualTo("Test asset for unit tests"));

            Assert.That(td.Properties, Is.Not.Null);
            Assert.That(td.Properties!, Has.Count.EqualTo(3));

            // Rec 5: field-value assertions, not just counts/names.
            WotProperty voltage = td.Properties["Voltage"];
            Assert.That(voltage.Type, Is.EqualTo("number"));
            Assert.That(voltage.Title, Is.EqualTo("Line voltage"));
            Assert.That(voltage.Description, Is.EqualTo("Phase-to-ground line voltage"));
            Assert.That(voltage.ReadOnly, Is.True);
            Assert.That(voltage.Observable, Is.True);
            Assert.That(voltage.Unit, Is.EqualTo("V"));

            WotProperty online = td.Properties["Online"];
            Assert.That(online.Type, Is.EqualTo("boolean"));
            Assert.That(online.ReadOnly, Is.True);
            Assert.That(online.Observable, Is.False);

            // G8: assert the default-false branch on readOnly / observable.
            WotProperty counter = td.Properties["Counter"];
            Assert.That(counter.Type, Is.EqualTo("integer"));
            Assert.That(counter.ReadOnly, Is.False);
            Assert.That(counter.Observable, Is.False);

            Assert.That(td.Actions, Is.Not.Null);
            Assert.That(td.Actions!, Has.Count.EqualTo(2));

            WotAction reset = td.Actions["Reset"];
            Assert.That(reset.Title, Is.EqualTo("Reset"));
            Assert.That(reset.Description, Is.EqualTo("Resets the asset"));
            Assert.That(reset.Input?.Properties, Has.Count.EqualTo(1));
            Assert.That(reset.Input!.Properties!["confirm"].Type, Is.EqualTo("boolean"));

            // G7: minimum/maximum/unit/description on action members must round-trip.
            WotAction setTemp = td.Actions["SetTemperature"];
            WotActionMember target = setTemp.Input!.Properties!["target"];
            Assert.That(target.Type, Is.EqualTo("number"));
            Assert.That(target.Minimum, Is.EqualTo(10.5));
            Assert.That(target.Maximum, Is.EqualTo(30.0));
            Assert.That(target.Unit, Is.EqualTo("degree Celsius"));
            Assert.That(target.Description, Is.EqualTo("Target temperature"));
        }

        [Test]
        public void DeserializeEmptyJsonObjectReturnsEmptyTd()
        {
            ThingDescription? td = JsonSerializer.Deserialize(
                "{}",
                ThingDescriptionJsonContext.Default.ThingDescription);

            Assert.That(td, Is.Not.Null);
            Assert.That(td!.Properties, Is.Null);
            Assert.That(td.Actions, Is.Null);
            Assert.That(td.Name, Is.Null);
            Assert.That(td.Base, Is.Null);
            Assert.That(td.Title, Is.Null);
            Assert.That(td.Description, Is.Null);
        }

        [Test]
        public void RoundTripPreservesPropertiesActionsAndMemberMetadata()
        {
            ThingDescription? td = JsonSerializer.Deserialize(
                SampleTd,
                ThingDescriptionJsonContext.Default.ThingDescription);
            Assert.That(td, Is.Not.Null);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                td!,
                ThingDescriptionJsonContext.Default.ThingDescription);

            ThingDescription? roundtrip = JsonSerializer.Deserialize(
                bytes,
                ThingDescriptionJsonContext.Default.ThingDescription);

            // Rec 5: compare projections of every persisted field, not just counts.
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Name, Is.EqualTo(td.Name));
            Assert.That(roundtrip.Base, Is.EqualTo(td.Base));
            Assert.That(roundtrip.Title, Is.EqualTo(td.Title));
            Assert.That(roundtrip.Description, Is.EqualTo(td.Description));

            string[] propertyProjection = ProjectProperties(roundtrip);
            string[] originalProjection = ProjectProperties(td);
            Assert.That(propertyProjection, Is.EqualTo(originalProjection));

            string[] actionProjection = ProjectActions(roundtrip);
            string[] originalActionProjection = ProjectActions(td);
            Assert.That(actionProjection, Is.EqualTo(originalActionProjection));
        }

        // G7/G8: shared helpers that lock the persisted field set in one place.
        private static string[] ProjectProperties(ThingDescription td)
        {
            return td.Properties!
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv =>
                    $"{kv.Key}|type={kv.Value.Type}|title={kv.Value.Title}|" +
                    $"desc={kv.Value.Description}|ro={kv.Value.ReadOnly}|" +
                    $"obs={kv.Value.Observable}|unit={kv.Value.Unit}")
                .ToArray();
        }

        private static string[] ProjectActions(ThingDescription td)
        {
            return td.Actions!
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv =>
                {
                    var memberFields = kv.Value.Input?.Properties?
                        .OrderBy(m => m.Key, StringComparer.Ordinal)
                        .Select(m =>
                            $"{m.Key}:{m.Value.Type}/{m.Value.Minimum}/{m.Value.Maximum}/{m.Value.Unit}/{m.Value.Description}")
                        .ToArray() ??
                        [];
                    return $"{kv.Key}|title={kv.Value.Title}|desc={kv.Value.Description}|" +
                        $"members=[{string.Join(",", memberFields)}]";
                })
                .ToArray();
        }
    }
}
