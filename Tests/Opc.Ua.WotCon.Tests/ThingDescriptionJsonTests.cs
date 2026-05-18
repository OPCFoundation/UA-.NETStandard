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

using System.Collections.Generic;
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
          "properties": {
            "Voltage": {
              "type": "number",
              "readOnly": true,
              "observable": true,
              "unit": "V"
            },
            "Online": {
              "type": "boolean",
              "readOnly": true
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
            Assert.That(td.Properties, Is.Not.Null);
            Assert.That(td.Properties!.Keys, Has.Member("Voltage"));
            Assert.That(td.Properties["Voltage"].Type, Is.EqualTo("number"));
            Assert.That(td.Properties["Voltage"].ReadOnly, Is.True);
            Assert.That(td.Properties["Voltage"].Observable, Is.True);
            Assert.That(td.Properties["Voltage"].Unit, Is.EqualTo("V"));
            Assert.That(td.Actions, Is.Not.Null);
            Assert.That(td.Actions!["Reset"].Title, Is.EqualTo("Reset"));
            Assert.That(td.Actions["Reset"].Input?.Properties?.Count, Is.EqualTo(1));
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
        }

        [Test]
        public void RoundTripPreservesProperties()
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
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Name, Is.EqualTo(td.Name));
            Assert.That(roundtrip.Properties?.Count, Is.EqualTo(td.Properties!.Count));
            Assert.That(roundtrip.Actions?.Count, Is.EqualTo(td.Actions!.Count));
        }
    }
}
