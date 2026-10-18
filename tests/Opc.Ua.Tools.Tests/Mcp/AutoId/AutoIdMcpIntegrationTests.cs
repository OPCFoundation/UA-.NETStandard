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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.Tools.Tests.Mcp.AutoId
{
    /// <summary>
    /// Drives the AutoID MCP tools through the real MCP transport, binder and session manager against
    /// a real server over opc.tcp.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("AutoID")]
    [NonParallelizable]
    public sealed class AutoIdMcpIntegrationTests
    {
        private static readonly string[] s_defaultLocationTypes = ["WGS84", "LOCAL"];

        private static readonly string[] s_tools =
        [
            "autoid_list_readers",
            "autoid_read_device",
            "autoid_scan",
            "autoid_scan_start",
            "autoid_scan_stop",
            "autoid_observe_scan_events",
            "autoid_read_tag",
            "autoid_get_supported_location_types",
            "autoid_get_location"
        ];

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = await AutoIdMcpFixture.StartAsync().ConfigureAwait(false);
            m_harness = await AutoIdMcpHarness.CreateAsync(m_fixture.Session).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_harness != null)
            {
                await m_harness.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TheCatalogueIsExactlyTheNineTools()
        {
            IList<McpClientTool> tools = await m_harness!.Client.ListToolsAsync().ConfigureAwait(false);

            string[] names = [.. tools.Select(tool => tool.Name).Where(name => name.StartsWith("autoid_", StringComparison.Ordinal))];
            Assert.Multiple(() =>
            {
                Assert.That(names, Is.EquivalentTo(s_tools));
                Assert.That(tools.Select(tool => tool.Name), Is.Unique);
                foreach (McpClientTool tool in tools.Where(tool => tool.Name.StartsWith("autoid_", StringComparison.Ordinal)))
                {
                    Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.Not.True, tool.Name);
                    Assert.That(tool.Description, Is.Not.Empty, tool.Name);
                }
                Assert.That(Annotation("autoid_list_readers", tools).ReadOnlyHint, Is.True);
                Assert.That(Annotation("autoid_read_tag", tools).ReadOnlyHint, Is.True);
                Assert.That(Annotation("autoid_scan", tools).ReadOnlyHint, Is.False);
                Assert.That(Annotation("autoid_scan_start", tools).ReadOnlyHint, Is.False);
                Assert.That(Annotation("autoid_scan_stop", tools).ReadOnlyHint, Is.False);
                Assert.That(
                    names.Any(name => name.Contains("write", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("kill", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("password", StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    "no tool changes a tag");
            });
        }

        [Test]
        public async Task ReadersAreDiscoveredWithTheirKindsAsync()
        {
            JsonElement page = await CallAsync("autoid_list_readers", new JsonObject
            {
                ["maxResults"] = 10
            }).ConfigureAwait(false);

            var kinds = page.GetProperty("items").EnumerateArray().ToDictionary(
                item => item.GetProperty("browseName").GetString()!.Split(':').Last(),
                item => item.GetProperty("kind").GetString()!);
            Assert.Multiple(() =>
            {
                Assert.That(kinds[AutoIdMcpFixture.RfidName], Is.EqualTo("Rfid"));
                Assert.That(kinds[AutoIdMcpFixture.RtlsName], Is.EqualTo("Rtls"));
                Assert.That(kinds[AutoIdMcpFixture.VerifierName], Is.EqualTo("OpticalVerifier"));
                Assert.That(page.GetProperty("consistency").GetString(), Is.EqualTo("live"));
            });
        }

        [Test]
        public async Task ReaderDiscoveryPagesAndAcceptsARootAsync()
        {
            JsonElement first = await CallAsync("autoid_list_readers", new JsonObject { ["maxResults"] = 1 })
                .ConfigureAwait(false);
            JsonElement second = await CallAsync("autoid_list_readers", new JsonObject
            {
                ["maxResults"] = 1,
                ["offset"] = first.GetProperty("nextOffset").GetInt32()
            }).ConfigureAwait(false);
            string deviceSet = new NodeId(Di.Objects.DeviceSet, (ushort)m_fixture!.Session.NamespaceUris.GetIndex(
                Di.Namespaces.OpcUaDi)).ToString();
            JsonElement rooted = await CallAsync("autoid_list_readers", new JsonObject
            {
                ["rootNodeId"] = deviceSet,
                ["maxResults"] = 50
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.GetProperty("hasMore").GetBoolean(), Is.True);
                Assert.That(first.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
                Assert.That(
                    second.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                    Is.Not.EqualTo(first.GetProperty("items")[0].GetProperty("nodeId").GetString()));
                Assert.That(rooted.GetProperty("items").GetArrayLength(), Is.GreaterThanOrEqualTo(3));
            });
        }

        [Test]
        public async Task TheDeviceSnapshotIsProjectedAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);

            JsonElement device = await CallAsync("autoid_read_device", new JsonObject { ["readerNodeId"] = rfid })
                .ConfigureAwait(false);
            JsonElement minimal = await CallAsync("autoid_read_device", new JsonObject
            {
                ["readerNodeId"] = rfid,
                ["includeNameplate"] = false,
                ["includeOptionalProperties"] = false,
                ["includeLastScan"] = false
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(device.GetProperty("deviceName").GetString(), Is.EqualTo(AutoIdMcpFixture.RfidName));
                Assert.That(device.GetProperty("modelVersion").GetString(), Is.EqualTo("1.00"));
                Assert.That(device.GetProperty("deviceStatus").GetString(), Is.EqualTo("Idle"));
                Assert.That(minimal.GetProperty("deviceName").GetString(), Is.EqualTo(AutoIdMcpFixture.RfidName));
                Assert.That(minimal.GetProperty("serialNumber").GetString(), Is.Empty);
                Assert.That(device.GetProperty("deviceStatusId").GetString(), Does.StartWith("ns="));
                Assert.That(
                    device.GetProperty("deviceStatusId").GetString(),
                    Is.Not.EqualTo(device.GetProperty("nodeId").GetString()));
                Assert.That(device.GetProperty("lastScanDataId").GetString(), Does.StartWith("ns="));
                Assert.That(device.GetProperty("lastScanTimestampId").GetString(), Does.StartWith("ns="));
                Assert.That(
                    device.GetProperty("deviceLocationId").ValueKind,
                    Is.EqualTo(JsonValueKind.Null),
                    "the reader publishes no DeviceLocation");
                Assert.That(
                    Names(device.GetProperty("methods")),
                    Does.Contain("Scan").And.Contain("ReadTag").And.Not.Contain("GetDeviceLocation"));
                Assert.That(
                    Names(minimal.GetProperty("methods")),
                    Is.EqualTo(Names(device.GetProperty("methods"))),
                    "the methods do not depend on the read options");
            });
        }

        private static List<string?> Names(JsonElement array)
        {
            var names = new List<string?>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                names.Add(item.GetString());
            }
            return names;
        }

        [Test]
        public async Task ASynchronousScanReturnsTheTagsAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);
            var epc = ByteString.From([0xE2, 0x80, 0x01, 0x02]);
            await m_fixture!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)).ConfigureAwait(false);
            try
            {
                JsonElement scan = await CallAsync("autoid_scan", new JsonObject
                {
                    ["readerNodeId"] = rfid,
                    ["durationMs"] = 500,
                    ["dataAvailable"] = true
                }).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(scan.GetProperty("status").GetString(), Is.EqualTo("SUCCESS"));
                    Assert.That(scan.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(scan.GetProperty("results")[0].GetProperty("CodeType").GetString(), Is.EqualTo("EPC"));
                });
            }
            finally
            {
                await m_fixture.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ScanArgumentsAreValidatedBeforeTheDeviceIsCalledAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);
            (string Tool, JsonObject Arguments)[] bad =
            [
                ("autoid_scan", new JsonObject { ["readerNodeId"] = rfid }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = rfid, ["durationMs"] = -1 }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = rfid, ["durationMs"] = 700_000 }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = rfid, ["cycles"] = -1 }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = rfid, ["cycles"] = 2_000_000 }),
                ("autoid_scan_start", new JsonObject { ["readerNodeId"] = rfid, ["durationMs"] = -1 }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = "ns=0;i=0", ["cycles"] = 1 }),
                ("autoid_scan", new JsonObject { ["readerNodeId"] = "not a node id", ["cycles"] = 1 })
            ];

            foreach ((string tool, JsonObject arguments) in bad)
            {
                JsonElement result = await CallAsync(tool, arguments, expectError: true).ConfigureAwait(false);
                Assert.That(result.GetProperty("error").GetBoolean(), Is.True, tool + " " + arguments.ToJsonString());
            }
        }

        [Test]
        public async Task AVerifierScanIsRefusedAndEventsCarryItsResultsAsync()
        {
            string verifier = await ReaderIdAsync(AutoIdMcpFixture.VerifierName).ConfigureAwait(false);
            m_fixture!.Verifier.Present(new OpticalVerifierScanResult
            {
                CodeType = "RAW:STRING",
                ScanData = new ScanData { SwitchField = ScanDataFields.String, String = "V-7" },
                Timestamp = DateTimeUtc.Now
            });
            try
            {
                JsonElement refused = await CallAsync(
                    "autoid_scan",
                    new JsonObject { ["readerNodeId"] = verifier, ["cycles"] = 1 },
                    expectError: true).ConfigureAwait(false);
                JsonElement started = await CallAsync("autoid_scan_start", new JsonObject
                {
                    ["readerNodeId"] = verifier
                }).ConfigureAwait(false);
                JsonElement observed = await CallAsync("autoid_observe_scan_events", new JsonObject
                {
                    ["readerNodeId"] = verifier,
                    ["durationMs"] = 3000,
                    ["maxItems"] = 1
                }).ConfigureAwait(false);
                JsonElement stopped = await CallAsync("autoid_scan_stop", new JsonObject
                {
                    ["readerNodeId"] = verifier
                }).ConfigureAwait(false);
                JsonElement stoppedAgain = await CallAsync(
                    "autoid_scan_stop",
                    new JsonObject { ["readerNodeId"] = verifier },
                    expectError: true).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(refused.GetProperty("message").GetString(), Does.Contain("WGS84Coordinate"));
                    Assert.That(started.GetProperty("status").GetString(), Is.EqualTo("SUCCESS"));
                    Assert.That(observed.GetProperty("stoppedBy").GetString(), Is.EqualTo("itemLimit"));
                    Assert.That(
                        observed.GetProperty("items")[0].GetProperty("deviceName").GetString(),
                        Is.EqualTo(AutoIdMcpFixture.VerifierName));
                    Assert.That(observed.GetProperty("items")[0].GetProperty("results").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(stopped.GetProperty("stopped").GetBoolean(), Is.True);
                    Assert.That(stoppedAgain.GetProperty("statusCode").GetString(), Does.Contain("BadInvalidState"));
                });
            }
            finally
            {
                m_fixture.Verifier.Clear();
            }
        }

        [Test]
        public async Task ObservationEndsAtTheWindowWhenNothingHappensAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);

            JsonElement observed = await CallAsync("autoid_observe_scan_events", new JsonObject
            {
                ["readerNodeId"] = rfid,
                ["durationMs"] = 300
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observed.GetProperty("stoppedBy").GetString(), Is.EqualTo("durationLimit"));
                Assert.That(observed.GetProperty("items").GetArrayLength(), Is.Zero);
            });
        }

        [Test]
        public async Task TagMemoryIsReadWithTheDeviceStatusAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);
            var epc = ByteString.From([0xE2, 0x80, 0x0A, 0x0B]);
            await m_fixture!.Rfid.PlaceTagAsync(new InMemoryRfidTag(epc)).ConfigureAwait(false);
            try
            {
                JsonElement read = await CallAsync("autoid_read_tag", new JsonObject
                {
                    ["readerNodeId"] = rfid,
                    ["identifierHex"] = "E2800A0B",
                    ["region"] = 1,
                    ["offset"] = 4,
                    ["length"] = 4
                }).ConfigureAwait(false);
                JsonElement unknown = await CallAsync("autoid_read_tag", new JsonObject
                {
                    ["readerNodeId"] = rfid,
                    ["identifierHex"] = "0102",
                    ["codeType"] = "UID"
                }).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(read.GetProperty("status").GetString(), Is.EqualTo("SUCCESS"));
                    Assert.That(read.GetProperty("dataHex").GetString(), Is.EqualTo("E2800A0B"));
                    Assert.That(unknown.GetProperty("status").GetString(), Is.EqualTo("NO_IDENTIFIER"));
                });
            }
            finally
            {
                await m_fixture.Rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TagReadArgumentsAreValidatedAsync()
        {
            string rfid = await ReaderIdAsync(AutoIdMcpFixture.RfidName).ConfigureAwait(false);
            string rtls = await ReaderIdAsync(AutoIdMcpFixture.RtlsName).ConfigureAwait(false);
            (string Reader, JsonObject Extra)[] bad =
            [
                (rfid, new JsonObject { ["identifierHex"] = "zz" }),
                (rfid, new JsonObject { ["identifierHex"] = string.Empty }),
                (rfid, new JsonObject { ["identifierHex"] = new string('A', 126) }),
                (rfid, new JsonObject { ["identifierHex"] = "0102", ["length"] = 0 }),
                (rfid, new JsonObject { ["identifierHex"] = "0102", ["length"] = 4097 }),
                (rfid, new JsonObject { ["identifierHex"] = "0102", ["region"] = -1 }),
                (rfid, new JsonObject { ["identifierHex"] = "0102", ["region"] = 70_000 }),
                (rfid, new JsonObject { ["identifierHex"] = "0102", ["offset"] = 70_000 }),
                (rtls, new JsonObject { ["identifierHex"] = "0102" })
            ];

            foreach ((string reader, JsonObject extra) in bad)
            {
                extra["readerNodeId"] = reader;
                JsonElement result = await CallAsync("autoid_read_tag", extra, expectError: true).ConfigureAwait(false);
                Assert.That(result.GetProperty("error").GetBoolean(), Is.True, extra.ToJsonString());
            }
        }

        [Test]
        public async Task LocationToolsAnswerForTheLocatingSystemAsync()
        {
            string rtls = await ReaderIdAsync(AutoIdMcpFixture.RtlsName).ConfigureAwait(false);
            var id = ByteString.From([0x01, 0x02, 0x03]);
            m_fixture!.Rtls.Locate(id, new RtlsLocationResult
            {
                CodeType = "UID",
                ScanData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = id },
                Timestamp = DateTimeUtc.Now,
                EncodingMask = (uint)ScanResultFields.Location,
                Location = new Location
                {
                    SwitchField = LocationFields.WGS84,
                    WGS84 = new WGS84Coordinate { Latitude = 51.5, Longitude = -0.12 }
                }
            });
            try
            {
                JsonElement types = await CallAsync("autoid_get_supported_location_types", new JsonObject
                {
                    ["readerNodeId"] = rtls
                }).ConfigureAwait(false);
                JsonElement location = await CallAsync("autoid_get_location", new JsonObject
                {
                    ["readerNodeId"] = rtls,
                    ["identifierHex"] = "010203",
                    ["locationType"] = "wgs84"
                }).ConfigureAwait(false);
                JsonElement unknown = await CallAsync(
                    "autoid_get_location",
                    new JsonObject { ["readerNodeId"] = rtls, ["identifierHex"] = "FF", ["locationType"] = "WGS84" },
                    expectError: true).ConfigureAwait(false);
                JsonElement badType = await CallAsync(
                    "autoid_get_location",
                    new JsonObject { ["readerNodeId"] = rtls, ["identifierHex"] = "FF", ["locationType"] = "Mars" },
                    expectError: true).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        types.GetProperty("supportedLocationTypes").EnumerateArray().Select(t => t.GetString()),
                        Is.EqualTo(s_defaultLocationTypes));
                    Assert.That(
                        location.GetProperty("location").GetProperty("Location").ValueKind,
                        Is.Not.EqualTo(JsonValueKind.Null));
                    Assert.That(unknown.GetProperty("statusCode").GetString(), Does.Contain("BadNotFound"));
                    Assert.That(badType.GetProperty("error").GetBoolean(), Is.True);
                });
            }
            finally
            {
                m_fixture.Rtls.Forget(id);
            }
        }

        [Test]
        public async Task ASessionThatIsNotConnectedIsReportedAsAnErrorAsync()
        {
            JsonElement result = await CallAsync(
                "autoid_list_readers",
                new JsonObject { ["sessionName"] = "nobody" },
                expectError: true).ConfigureAwait(false);

            Assert.That(result.GetProperty("error").GetBoolean(), Is.True);
        }

        [Test]
        public async Task PagingLimitsAreEnforcedAsync()
        {
            foreach (JsonObject arguments in BadPages())
            {
                JsonElement result = await CallAsync("autoid_list_readers", arguments, expectError: true)
                    .ConfigureAwait(false);
                Assert.That(result.GetProperty("error").GetBoolean(), Is.True, arguments.ToJsonString());
            }
        }

        private static IEnumerable<JsonObject> BadPages()
        {
            yield return new JsonObject { ["maxResults"] = 0 };
            yield return new JsonObject { ["maxResults"] = 501 };
            yield return new JsonObject { ["offset"] = -1 };
        }

        private async Task<string> ReaderIdAsync(string name)
        {
            JsonElement page = await CallAsync("autoid_list_readers", new JsonObject { ["maxResults"] = 50 })
                .ConfigureAwait(false);
            return page.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("browseName").GetString()!.EndsWith(":" + name, StringComparison.Ordinal))
                .GetProperty("nodeId").GetString()!;
        }

        private async Task<JsonElement> CallAsync(string tool, JsonObject arguments, bool expectError = false)
        {
            CallToolResult result = await m_harness!.CallAsync(tool, arguments).ConfigureAwait(false);
            Assert.That(result.IsError ?? false, Is.EqualTo(expectError), tool);
            return AutoIdMcpHarness.Json(result);
        }

        private static ToolAnnotations Annotation(string name, IEnumerable<McpClientTool> tools)
        {
            return tools.Single(tool => tool.Name == name).ProtocolTool.Annotations!;
        }

        private AutoIdMcpFixture? m_fixture;
        private AutoIdMcpHarness? m_harness;
    }
}
#endif
