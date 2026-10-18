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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pumps;
using Opc.Ua.Tests;
using static Opc.Ua.Tools.Tests.McpCompanion.CompanionCoverageSession;
using PumpNames = Opc.Ua.Pumps.BrowseNames;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies complete Pumps MCP projections and semantic read contracts over a strict session boundary.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PumpsCoverageTests
    {
        /// <summary>
        /// All nameplate fields, localized physical address and explicit empty identifiers survive projection.
        /// </summary>
        [Test]
        public void NameplatePreservesAllFieldsAndPhysicalAddress()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var nameplate = new PumpNameplate
            {
                NodeId = Id("nameplate"),
                Manufacturer = new LocalizedText("de-DE", "Pumpen"),
                SerialNumber = "serial-7",
                ManufacturerUri = "urn:manufacturer",
                Model = new LocalizedText("en-US", "Centrifugal"),
                ProductCode = "P2",
                HardwareRevision = "H3",
                SoftwareRevision = "S4",
                DeviceClass = "Pump",
                ProductInstanceUri = "urn:pump:7",
                AssetId = string.Empty,
                ComponentName = new LocalizedText("fr-FR", "Pompe"),
                Location = "Basement",
                InitialOperationDate = s_timestamp,
                YearOfConstruction = 2025,
                MonthOfConstruction = 12,
                DayOfConstruction = 31,
                ArticleNumber = "article",
                OrderProductCode = "order",
                TypeOfProduct = "inline",
                Supplier = "supplier",
                CountryOfOrigin = "DE",
                FabricationNumber = "fabrication",
                GTINCode = "012345",
                NationalStockNumber = "stock",
                PhysicalAddress = new PhysicalAddressDataType
                {
                    EncodingMask = (uint)(PhysicalAddressDataTypeFields.Street |
                        PhysicalAddressDataTypeFields.Number |
                        PhysicalAddressDataTypeFields.City |
                        PhysicalAddressDataTypeFields.PostalCode |
                        PhysicalAddressDataTypeFields.State |
                        PhysicalAddressDataTypeFields.Country),
                    Street = new LocalizedText("en-US", "Main"),
                    Number = new LocalizedText("en-US", "12"),
                    City = new LocalizedText("en-US", "Town"),
                    PostalCode = new LocalizedText("en-US", "12345"),
                    State = new LocalizedText("en-US", "Region"),
                    Country = new LocalizedText("en-US", "Germany")
                },
                MarkingsFolderId = Id("markings")
            };

            AssertJson(PumpsJson.Nameplate(nameplate, wire.Context), /*lang=json,strict*/ """
                {
                  "nodeId":"ns=1;s=nameplate","manufacturer":{"text":"Pumpen","locale":"de-DE"},
                  "serialNumber":"serial-7","manufacturerUri":"urn:manufacturer",
                  "model":{"text":"Centrifugal","locale":"en-US"},"productCode":"P2",
                  "hardwareRevision":"H3","softwareRevision":"S4","deviceClass":"Pump",
                  "productInstanceUri":"urn:pump:7","assetId":"",
                  "componentName":{"text":"Pompe","locale":"fr-FR"},"location":"Basement",
                  "initialOperationDate":"2026-02-03T04:05:06Z",
                  "yearOfConstruction":2025,"monthOfConstruction":12,"dayOfConstruction":31,
                  "articleNumber":"article","orderProductCode":"order","typeOfProduct":"inline",
                  "supplier":"supplier","countryOfOrigin":"DE","fabricationNumber":"fabrication",
                  "gtinCode":"012345","nationalStockNumber":"stock",
                  "physicalAddress":{
                    "Street":{"Locale":"en-US","Text":"Main"},"Number":{"Locale":"en-US","Text":"12"},
                    "City":{"Locale":"en-US","Text":"Town"},"PostalCode":{"Locale":"en-US","Text":"12345"},
                    "State":{"Locale":"en-US","Text":"Region"},"Country":{"Locale":"en-US","Text":"Germany"}
                  },
                  "markingsFolderId":"ns=1;s=markings"
                }
                """);
        }

        /// <summary>
        /// Multi-pump projection retains all settings and distinguishes missing arrays from published empty arrays.
        /// </summary>
        [TestCase(double.NaN, "NaN")]
        [TestCase(double.PositiveInfinity, "Infinity")]
        [TestCase(double.NegativeInfinity, "-Infinity")]
        public void MultiPumpPreservesSettingsNonFiniteIntervalsAndNullableArrays(double interval, string text)
        {
            var configuration = new MultiPumpConfiguration
            {
                NodeId = Id("multi"),
                PumpRole = PumpRoleEnum.Master,
                OperationMode = MultiPumpOperationModeEnum.AdditionOperation,
                DistributionType = DistributionTypeEnum.ConcerningLoadDistribution,
                ExchangeMode = ExchangeModeEnum.ExchangeDisabled,
                ExchangeTime = s_timestamp,
                ExchangeTimeDifference = interval,
                NumberOfPumps = 3,
                MaximumNumberOfPumpsInOperation = 0,
                DistributionPriority = ["P2", "P1"],
                PumpCollectiveIDs = [],
                RedundantPumpIDs = ArrayOf<string>.Null
            };

            JsonNode json = PumpsJson.MultiPump(configuration)!;

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=multi"));
            Assert.That(json["pumpRole"]!.GetValue<string>(), Is.EqualTo("Master"));
            Assert.That(json["operationMode"]!.GetValue<string>(), Is.EqualTo("AdditionOperation"));
            Assert.That(json["distributionType"]!.GetValue<string>(), Is.EqualTo("ConcerningLoadDistribution"));
            Assert.That(json["exchangeMode"]!.GetValue<string>(), Is.EqualTo("ExchangeDisabled"));
            Assert.That(json["exchangeTime"]!.GetValue<DateTime>(), Is.EqualTo(s_timestamp));
            Assert.That(json["exchangeTimeDifference"]!.GetValue<string>(), Is.EqualTo(text));
            Assert.That(json["numberOfPumps"]!.GetValue<uint>(), Is.EqualTo(3));
            Assert.That(json["maximumNumberOfPumpsInOperation"]!.GetValue<uint>(), Is.Zero);
            AssertJson(json["distributionPriority"], """["P2","P1"]""");
            AssertJson(json["pumpCollectiveIds"], "[]");
            Assert.That(json["redundantPumpIds"], Is.Null);
            JsonNode missing = PumpsJson.MultiPump(new MultiPumpConfiguration { NodeId = Id("empty") })!;
            foreach (string property in new[]
            {
                "pumpRole", "operationMode", "distributionType", "exchangeMode", "exchangeTime",
                "exchangeTimeDifference", "numberOfPumps", "maximumNumberOfPumpsInOperation",
                "distributionPriority", "pumpCollectiveIds", "redundantPumpIds"
            })
            {
                Assert.That(missing.AsObject().ContainsKey(property), Is.True, property);
                Assert.That(missing[property], Is.Null, property);
            }
            Assert.That(PumpsJson.MultiPump(configuration with { ExchangeTimeDifference = 0 })!
                ["exchangeTimeDifference"]!.GetValue<double>(), Is.Zero);
        }

        /// <summary>
        /// Nested value sets retain false and zero, raw bad quality, timestamps, units and instrument range.
        /// </summary>
        [Test]
        public void ValueSetPreservesNestedTypedValuesQualityAndMetadata()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var pressure = new PumpValue
            {
                NodeId = Id("pressure"),
                Name = "Pressure",
                Value = Variant.From(double.NaN),
                StatusCode = StatusCodes.BadOutOfService,
                SourceTimestamp = s_timestamp,
                EngineeringUnits = Pascals(),
                EuRange = new Range { Low = -5, High = 250 }
            };
            var nested = new PumpValueSet(Id("vibration"),
                [Value("Peak", Variant.From(0u)), Value("Enabled", Variant.From(false))]);
            var root = new PumpValueSet(Id("measurements"), [pressure],
                [new KeyValuePair<string, PumpValueSet>("Vibration", nested)]);

            JsonObject json = PumpsJson.ValueSet(root, wire.Context);

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=measurements"));
            Assert.That(json["available"]!.GetValue<bool>(), Is.True);
            Assert.That(json["values"]!.AsArray(), Has.Count.EqualTo(1));
            JsonNode value = json["values"]![0]!;
            Assert.That(value["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=pressure"));
            Assert.That(value["name"]!.GetValue<string>(), Is.EqualTo("Pressure"));
            AssertJson(value["value"], /*lang=json,strict*/ """{"UaType":11,"Value":"NaN"}""");
            Assert.That(value["statusCode"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadOutOfService.Code));
            Assert.That(value["sourceTimestamp"]!.GetValue<DateTime>(), Is.EqualTo(s_timestamp));
            Assert.That(value["engineeringUnits"]!["UnitId"]!.GetValue<int>(), Is.EqualTo(20529));
            AssertJson(value["euRange"], /*lang=json,strict*/ """{"Low":-5,"High":250}""");
            JsonNode group = json["groups"]!["Vibration"]!;
            Assert.That(group["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vibration"));
            Assert.That(group["available"]!.GetValue<bool>(), Is.True);
            AssertJson(group["values"]![0]!["value"], /*lang=json,strict*/ """{"UaType":7,"Value":0}""");
            AssertJson(group["values"]![1]!["value"], /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
            Assert.That(group["values"]![0]!["engineeringUnits"], Is.Null);
            Assert.That(group["values"]![0]!["sourceTimestamp"], Is.Null);
            AssertJson(PumpsJson.ValueSet(PumpValueSet.Empty, wire.Context), /*lang=json,strict*/ """
                {"nodeId":"i=0","available":false,"values":[],"groups":{}}
                """);
        }

        /// <summary>
        /// Supervision retains every group but reports only good-quality true booleans as active signals.
        /// </summary>
        [Test]
        public void SupervisionRetainsInactiveAndBadSignalsWithoutReportingThemActive()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var supervision = new PumpSupervisionStatus
            {
                NodeId = Id("events"),
                ProcessFluid = Set("fluid", Value("Cavitation", Variant.From(true))),
                PumpOperation = Set("operation", Value("Overheat", Variant.From(true), StatusCodes.BadNoData)),
                Mechanics = Set("mechanics", Value("Imbalance", Variant.From(false))),
                Hardware = Set("hardware", Value("Power", Variant.From(0))),
                Software = Set("software", Value("Program", Variant.From(false))),
                Electronics = Set("electronics", Value("Voltage", Variant.From(false))),
                AuxiliaryDevice = Set("auxiliary", Value("Sensor", Variant.From(false)))
            };

            JsonNode json = PumpsJson.Supervision(supervision, wire.Context)!;

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=events"));
            Assert.That(json["groups"]!.AsObject().Select(group => group.Key), Is.EquivalentTo(s_supervisionGroups));
            Assert.That(json["hasActiveSignals"]!.GetValue<bool>(), Is.True);
            Assert.That(json["activeSignals"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(json["activeSignals"]![0]!["group"]!.GetValue<string>(), Is.EqualTo("SupervisionProcessFluid"));
            Assert.That(json["activeSignals"]![0]!["signal"]!["name"]!.GetValue<string>(), Is.EqualTo("Cavitation"));
            JsonNode bad = json["groups"]!["SupervisionPumpOperation"]!["values"]![0]!;
            AssertJson(bad["value"], /*lang=json,strict*/ """{"UaType":1,"Value":true}""");
            Assert.That(bad["statusCode"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNoData.Code));
            AssertJson(json["groups"]!["SupervisionMechanics"]!["values"]![0]!["value"],
                                     /*lang=json,strict*/
                                     """{"UaType":1,"Value":false}""");
            JsonNode healthy = PumpsJson.Supervision(supervision with { ProcessFluid = PumpValueSet.Empty },
                wire.Context)!;
            Assert.That(healthy["hasActiveSignals"]!.GetValue<bool>(), Is.False);
            Assert.That(healthy["activeSignals"]!.AsArray(), Is.Empty);
        }

        /// <summary>
        /// Maintenance exposes all four groups and retains false failure state beside typed level and item state.
        /// </summary>
        [Test]
        public void MaintenancePreservesGroupValuesAndFalseFailure()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var maintenance = new PumpMaintenanceData
            {
                NodeId = Id("maintenance"),
                General = Set("general",
                    Value(PumpNames.StateOfTheItem, Variant.From((int)StateOfTheItemEnum.OperatingState)),
                    Value(PumpNames.MaintenanceLevel, Variant.From((int)MaintenanceLevelEnum.Level2))),
                Preventive = Set("preventive", Value("Inspection", Variant.From("scheduled"))),
                ConditionBased = Set("condition", Value("Availability", Variant.From(0.99))),
                Breakdown = Set("breakdown", Value(PumpNames.Failure, Variant.From(false)))
            };

            JsonNode json = PumpsJson.Maintenance(maintenance, wire.Context)!;

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=maintenance"));
            Assert.That(json["groups"]!.AsObject().Select(group => group.Key), Is.EquivalentTo(s_maintenanceGroups));
            Assert.That(json["stateOfTheItem"]!.GetValue<string>(), Is.EqualTo("OperatingState"));
            Assert.That(json["maintenanceLevel"]!.GetValue<string>(), Is.EqualTo("Level2"));
            Assert.That(json["hasFailure"]!.GetValue<bool>(), Is.False);
            Assert.That(json["groups"]!["PreventiveMaintenance"]!["values"]![0]!["value"]!["Value"]!
                .GetValue<string>(), Is.EqualTo("scheduled"));
            Assert.That(json["groups"]!["ConditionBasedMaintenance"]!["values"]![0]!["value"]!["Value"]!
                .GetValue<double>(), Is.EqualTo(0.99));
            JsonNode missing = PumpsJson.Maintenance(new PumpMaintenanceData { NodeId = Id("empty") }, wire.Context)!;
            Assert.That(missing["stateOfTheItem"], Is.Null);
            Assert.That(missing["maintenanceLevel"], Is.Null);
            Assert.That(missing["hasFailure"], Is.Null);
        }

        /// <summary>
        /// Snapshot JSON preserves each distinct configuration/operation group and independently pages typed ports.
        /// </summary>
        [Test]
        public void SnapshotPreservesAllGroupsAndPagesVendorPorts()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var port = new PumpPortDescriptor
            {
                NodeId = Id("port-2"),
                Name = "VendorInlet",
                TypeDefinitionId = Id("vendor-port"),
                Kind = PumpPortKind.Unknown,
                Direction = PortDirectionEnum.InOut,
                Category = "Hydraulic",
                IdCarrier = string.Empty,
                Design = Set("port-design", Value("Nominal", Variant.From(10))),
                Implementation = Set("port-installed", Value("Connected", Variant.From(false))),
                Measurements = Set("port-measured", Value("Pressure", Variant.From(20.5))),
                SystemRequirements = Set("port-required", Value("Minimum", Variant.From(0)))
            };
            var snapshot = new PumpSnapshot
            {
                NodeId = Id("pump"),
                BrowseName = new QualifiedName("VendorPump", 1),
                DisplayName = new LocalizedText("de-DE", "Pumpe"),
                TypeDefinitionId = Id("vendor-pump"),
                Configuration = new PumpConfigurationData
                {
                    NodeId = Id("configuration"),
                    Design = Set("design", Value("Power", Variant.From(1))),
                    Implementation = Set("implementation", Value("Installed", Variant.From(false))),
                    SystemRequirements = Set("requirements", Value("Capacity", Variant.From(3)))
                },
                Operational = new PumpOperationalData
                {
                    NodeId = Id("operational"),
                    Measurements = Set("measurements", Value("Pressure", Variant.From(4))),
                    Signals = Set("signals", Value("Input", Variant.From(false))),
                    Control = Set("control", Value("Gain", Variant.From(6))),
                    PumpActuation = Set("pump-actuation", Value("Enabled", Variant.From(false))),
                    BypassActuation = Set("bypass", Value("Bypass", Variant.From(8))),
                    ThrottleValveActuation = Set("throttle", Value("Valve", Variant.From(9))),
                    MultiPump = new MultiPumpConfiguration { NodeId = Id("multi"), NumberOfPumps = 2 }
                },
                Documentation = Set("documentation", Value("Manual", Variant.From("urn:manual"))),
                Ports =
                [
                    port with { NodeId = Id("port-1") },
                    port,
                    port with { NodeId = Id("port-3") }
                ]
            };

            JsonObject json = PumpsJson.Snapshot(snapshot, wire.Context, 1, 1);

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=pump"));
            Assert.That(json["browseName"]!.GetValue<string>(), Is.EqualTo("1:VendorPump"));
            AssertJson(json["displayName"], /*lang=json,strict*/ """{"text":"Pumpe","locale":"de-DE"}""");
            Assert.That(json["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vendor-pump"));
            Assert.That(json["consistency"]!.GetValue<string>(), Is.EqualTo("live"));
            Assert.That(json["nameplate"], Is.Null);
            Assert.That(json["supervision"], Is.Null);
            Assert.That(json["maintenance"], Is.Null);
            Assert.That(json["configuration"]!["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=configuration"));
            AssertGroup(json["configuration"]!["design"]!, "design", "Power",
                /*lang=json,strict*/ """{"UaType":6,"Value":1}""");
            AssertGroup(json["configuration"]!["implementation"]!, "implementation", "Installed",
                                     /*lang=json,strict*/
                                     """{"UaType":1,"Value":false}""");
            AssertGroup(json["configuration"]!["systemRequirements"]!, "requirements", "Capacity",
                                     /*lang=json,strict*/
                                     """{"UaType":6,"Value":3}""");
            JsonNode operational = json["operational"]!;
            Assert.That(operational["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=operational"));
            AssertGroup(operational["measurements"]!, "measurements", "Pressure",
                /*lang=json,strict*/ """{"UaType":6,"Value":4}""");
            AssertGroup(operational["signals"]!, "signals", "Input",
                /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
            AssertGroup(operational["control"]!, "control", "Gain", /*lang=json,strict*/ """{"UaType":6,"Value":6}""");
            AssertGroup(operational["pumpActuation"]!, "pump-actuation", "Enabled",
                /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
            AssertGroup(operational["bypassActuation"]!, "bypass", "Bypass",
                /*lang=json,strict*/ """{"UaType":6,"Value":8}""");
            AssertGroup(operational["throttleValveActuation"]!, "throttle", "Valve",
                /*lang=json,strict*/ """{"UaType":6,"Value":9}""");
            Assert.That(operational["multiPump"]!["numberOfPumps"]!.GetValue<uint>(), Is.EqualTo(2));
            AssertGroup(json["documentation"]!, "documentation", "Manual",
                /*lang=json,strict*/ """{"UaType":12,"Value":"urn:manual"}""");
            JsonNode page = json["ports"]!;
            Assert.That(page["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(page["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(page["hasMore"]!.GetValue<bool>(), Is.True);
            JsonNode projected = page["items"]![0]!;
            Assert.That(projected["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=port-2"));
            Assert.That(projected["name"]!.GetValue<string>(), Is.EqualTo("VendorInlet"));
            Assert.That(projected["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vendor-port"));
            Assert.That(projected["kind"]!.GetValue<string>(), Is.EqualTo("Unknown"));
            Assert.That(projected["direction"]!.GetValue<string>(), Is.EqualTo("InOut"));
            Assert.That(projected["category"]!.GetValue<string>(), Is.EqualTo("Hydraulic"));
            Assert.That(projected["idCarrier"]!.GetValue<string>(), Is.Empty);
            AssertGroup(projected["design"]!, "port-design", "Nominal",
                /*lang=json,strict*/ """{"UaType":6,"Value":10}""");
            AssertGroup(projected["implementation"]!, "port-installed", "Connected",
                /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
            AssertGroup(projected["measurements"]!, "port-measured", "Pressure",
                /*lang=json,strict*/ """{"UaType":11,"Value":20.5}""");
            AssertGroup(projected["systemRequirements"]!, "port-required", "Minimum",
                /*lang=json,strict*/ """{"UaType":6,"Value":0}""");
        }

        /// <summary>
        /// Absent optional records are JSON null, not empty present groups or invented default measurements.
        /// </summary>
        [Test]
        public void MissingFacetsRemainNullAndEmptySnapshotCollectionsRemainEmpty()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());

            Assert.That(PumpsJson.Nameplate(null, wire.Context), Is.Null);
            Assert.That(PumpsJson.Configuration(null, wire.Context), Is.Null);
            Assert.That(PumpsJson.Operational(null, wire.Context), Is.Null);
            Assert.That(PumpsJson.MultiPump(null), Is.Null);
            Assert.That(PumpsJson.Supervision(null, wire.Context), Is.Null);
            Assert.That(PumpsJson.Maintenance(null, wire.Context), Is.Null);
            JsonObject snapshot = PumpsJson.Snapshot(new PumpSnapshot { NodeId = Id("pump") }, wire.Context, 0, 1);
            Assert.That(snapshot["displayName"], Is.Null);
            Assert.That(snapshot["ports"]!["items"]!.AsArray(), Is.Empty);
            Assert.That(snapshot["ports"]!["nextOffset"], Is.Null);
            Assert.That(snapshot["documentation"]!["available"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// Every finite read facet is routed to its published group and never sends a mutation.
        /// </summary>
        [TestCase(PumpReadFacet.Configuration)]
        [TestCase(PumpReadFacet.Operational)]
        [TestCase(PumpReadFacet.Measurements)]
        [TestCase(PumpReadFacet.Signals)]
        [TestCase(PumpReadFacet.MultiPump)]
        [TestCase(PumpReadFacet.Supervision)]
        [TestCase(PumpReadFacet.Maintenance)]
        [TestCase(PumpReadFacet.Documentation)]
        public async Task ReadGroupReturnsSelectedSemanticDataWithoutMutationAsync(PumpReadFacet facet)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishPump(wire);

            JsonObject json = Result(await Tools(provider).ReadGroupAsync(
                Id("pump").ToString(), facet, false, false, "selected").ConfigureAwait(false));

            switch (facet)
            {
                case PumpReadFacet.Configuration:
                    AssertGroup(json["design"]!, "Design", "RatedPower",
                        /*lang=json,strict*/ """{"UaType":6,"Value":12}""");
                    AssertGroup(json["implementation"]!, "Implementation", "Installed",
                                             /*lang=json,strict*/
                                             """{"UaType":1,"Value":false}""");
                    AssertGroup(json["systemRequirements"]!, "SystemRequirements", "Required",
                                             /*lang=json,strict*/
                                             """{"UaType":6,"Value":20}""");
                    break;
                case PumpReadFacet.Operational:
                    Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=Operational"));
                    Assert.That(json["measurements"]!["values"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Speed"));
                    AssertJson(json["signals"]!["values"]![0]!["value"],
                        /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
                    Assert.That(json["multiPump"]!["numberOfPumps"]!.GetValue<uint>(), Is.EqualTo(2));
                    break;
                case PumpReadFacet.Measurements:
                    Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=Measurements"));
                    AssertJson(json["values"]![0]!["value"], /*lang=json,strict*/ """{"UaType":11,"Value":0}""");
                    Assert.That(json["groups"]!["Vibration"]!["values"]![0]!["name"]!.GetValue<string>(),
                        Is.EqualTo("Peak"));
                    break;
                case PumpReadFacet.Signals:
                    AssertGroup(json, "Signals", "Input", /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
                    break;
                case PumpReadFacet.MultiPump:
                    Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=MultiPump"));
                    Assert.That(json["pumpRole"]!.GetValue<string>(), Is.EqualTo("Master"));
                    Assert.That(json["numberOfPumps"]!.GetValue<uint>(), Is.EqualTo(2));
                    AssertJson(json["pumpCollectiveIds"], """["P1","P2"]""");
                    break;
                case PumpReadFacet.Supervision:
                    Assert.That(json["hasActiveSignals"]!.GetValue<bool>(), Is.True);
                    Assert.That(json["activeSignals"]!.AsArray(), Has.Count.EqualTo(1));
                    Assert.That(json["activeSignals"]![0]!["signal"]!["name"]!.GetValue<string>(),
                        Is.EqualTo("Cavitation"));
                    break;
                case PumpReadFacet.Maintenance:
                    Assert.That(json["stateOfTheItem"]!.GetValue<string>(), Is.EqualTo("OperatingState"));
                    Assert.That(json["maintenanceLevel"]!.GetValue<string>(), Is.EqualTo("Level2"));
                    Assert.That(json["hasFailure"]!.GetValue<bool>(), Is.False);
                    break;
                case PumpReadFacet.Documentation:
                    AssertGroup(json, "Documentation", "Manual",
                        /*lang=json,strict*/ """{"UaType":12,"Value":"urn:manual"}""");
                    break;
            }
            Assert.That(wire.Reads, Is.Not.Empty);
            Assert.That(wire.TimestampRequests, Is.All.EqualTo(TimestampsToReturn.Neither));
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Open value-set pages retain nested identities and honor metadata/timestamp read options independently.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReadValueSetPagesTypedValuesAndHonorsReadOptionsAsync(bool metadata, bool timestamps)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishPump(wire);

            JsonObject json = Result(await Tools(provider).ReadValueSetAsync(
                Id("Measurements").ToString(), 1, 1, metadata, timestamps, "selected").ConfigureAwait(false));

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=Measurements"));
            Assert.That(json["offset"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(json["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(json["hasMore"]!.GetValue<bool>(), Is.True);
            AssertJson(json["groups"], /*lang=json,strict*/ """{"Vibration":"ns=1;s=Vibration"}""");
            JsonNode value = json["items"]![0]!;
            Assert.That(value["name"]!.GetValue<string>(), Is.EqualTo("Enabled"));
            AssertJson(value["value"], /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
            Assert.That(value["statusCode"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.UncertainLastUsableValue.Code));
            Assert.That(value["sourceTimestamp"]?.GetValue<DateTime>(), Is.EqualTo(timestamps ? s_timestamp : null));
            if (metadata)
            {
                Assert.That(value["engineeringUnits"]!["UnitId"]!.GetValue<int>(), Is.EqualTo(20529));
                AssertJson(value["euRange"], /*lang=json,strict*/ """{"Low":-5,"High":250}""");
            }
            else
            {
                Assert.That(value["engineeringUnits"], Is.Null);
                Assert.That(value["euRange"], Is.Null);
            }
            Assert.That(wire.Paths.Any(path =>
                path.RelativePath.Elements[0].TargetName.Name == "EngineeringUnits"), Is.EqualTo(metadata));
            Assert.That(wire.TimestampRequests,
                Is.All.EqualTo(timestamps ? TimestampsToReturn.Source : TimestampsToReturn.Neither));
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// Nameplate reads span the correct namespaces and markings are independently paged vendor metadata.
        /// </summary>
        [Test]
        public async Task ReadNameplateAndListMarkingsPreservePublishedIdentitiesAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishPump(wire);
            wire.SetBrowse(Id("markings"),
            [
                Reference(Id("mark-1"), "First", typeDefinition: Id("vendor-mark-1")),
                Reference(Id("mark-2"), "Approval", NodeClass.Variable, Id("vendor-mark-2")),
                Reference(Id("mark-3"), "Third", typeDefinition: Id("vendor-mark-3"))
            ]);

            JsonObject nameplate = Result(await Tools(provider).ReadNameplateAsync(
                Id("pump").ToString(), "selected").ConfigureAwait(false));
            JsonObject markings = Result(await Tools(provider).ListMarkingsAsync(
                nameplate["markingsFolderId"]!.GetValue<string>(), 1, 1, "selected").ConfigureAwait(false));

            Assert.That(nameplate["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=Identification"));
            AssertJson(nameplate["manufacturer"], /*lang=json,strict*/ """{"text":"Maker","locale":"de-DE"}""");
            Assert.That(nameplate["serialNumber"]!.GetValue<string>(), Is.EqualTo("serial-42"));
            Assert.That(nameplate["location"]!.GetValue<string>(), Is.EqualTo("Cell 7"));
            Assert.That(nameplate["articleNumber"]!.GetValue<string>(), Is.EqualTo("article-5"));
            Assert.That(nameplate["physicalAddress"], Is.Null);
            Assert.That(markings["items"]!.AsArray(), Has.Count.EqualTo(1));
            JsonNode marking = markings["items"]![0]!;
            Assert.That(marking["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=mark-2"));
            Assert.That(marking["browseName"]!.GetValue<string>(), Is.EqualTo("1:Approval"));
            AssertJson(marking["displayName"], /*lang=json,strict*/ """{"text":"Approval","locale":"de-DE"}""");
            Assert.That(marking["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vendor-mark-2"));
            Assert.That(markings["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(markings["hasMore"]!.GetValue<bool>(), Is.True);
            Assert.That(wire.Reads.Any(read => read.NodeId == Id("markings")), Is.False);
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// Port discovery keeps vendor ports and their read-only groups rather than treating them as unsupported.
        /// </summary>
        [Test]
        public async Task ListPortsPreservesVendorMetadataAndPagedMeasurementsAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishPorts(wire);

            JsonObject json = Result(await Tools(provider).ListPortsAsync(
                Id("pump").ToString(), 1, 1, false, false, "selected").ConfigureAwait(false));

            Assert.That(json["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(json["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            JsonNode port = json["items"]![0]!;
            Assert.That(port["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=port-2"));
            Assert.That(port["name"]!.GetValue<string>(), Is.EqualTo("VendorInlet"));
            Assert.That(port["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vendor-port"));
            Assert.That(port["kind"]!.GetValue<string>(), Is.EqualTo("Unknown"));
            Assert.That(port["direction"]!.GetValue<string>(), Is.EqualTo("In"));
            Assert.That(port["category"]!.GetValue<string>(), Is.EqualTo("Hydraulic"));
            Assert.That(port["idCarrier"]!.GetValue<string>(), Is.Empty);
            AssertGroup(port["measurements"]!, "port-measurements", "Pressure",
                /*lang=json,strict*/ """{"UaType":11,"Value":50}""");
            Assert.That(port["implementation"]!["available"]!.GetValue<bool>(), Is.False);
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// The full live snapshot composes actual nameplate, group, supervision, maintenance and port reads.
        /// </summary>
        [Test]
        public async Task ReadSnapshotComposesPublishedFacetsAndKeepsLiveConsistencyAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishPump(wire);
            PublishPorts(wire);
            wire.SetValue(Id("pump"), Variant.From(new QualifiedName("Pump", 1)), Attributes.BrowseName);
            wire.SetValue(Id("pump"), Variant.From(new LocalizedText("en-US", "Main pump")), Attributes.DisplayName);
            wire.SetBrowse(Id("pump"),
                [Reference(Id("vendor-pump"), "VendorType", NodeClass.ObjectType)], ReferenceTypeIds.HasTypeDefinition);

            JsonObject json = Result(await Tools(provider).ReadSnapshotAsync(
                Id("pump").ToString(), 1, 1, false, false, "selected").ConfigureAwait(false));

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=pump"));
            Assert.That(json["browseName"]!.GetValue<string>(), Is.EqualTo("1:Pump"));
            AssertJson(json["displayName"], /*lang=json,strict*/ """{"text":"Main pump","locale":"en-US"}""");
            Assert.That(json["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=vendor-pump"));
            Assert.That(json["nameplate"]!["serialNumber"]!.GetValue<string>(), Is.EqualTo("serial-42"));
            Assert.That(json["configuration"]!["design"]!["values"]![0]!["name"]!.GetValue<string>(),
                Is.EqualTo("RatedPower"));
            Assert.That(json["operational"]!["multiPump"]!["numberOfPumps"]!.GetValue<uint>(), Is.EqualTo(2));
            Assert.That(json["supervision"]!["activeSignals"]![0]!["signal"]!["name"]!.GetValue<string>(),
                Is.EqualTo("Cavitation"));
            Assert.That(json["maintenance"]!["hasFailure"]!.GetValue<bool>(), Is.False);
            AssertGroup(json["documentation"]!, "Documentation", "Manual",
                /*lang=json,strict*/ """{"UaType":12,"Value":"urn:manual"}""");
            Assert.That(json["ports"]!["items"]![0]!["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=port-2"));
            Assert.That(json["ports"]!["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(json["consistency"]!.GetValue<string>(), Is.EqualTo("live"));
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Type classification asks about the supplied type definition, not an instance or the MultiPump group.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClassifyTypeKeepsVendorPumpAndMultiPumpDistinctAsync(bool vendorPump)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId pumpType = new(Pumps.ObjectTypes.PumpType, wire.NamespaceIndex(Pumps.Namespaces.Pumps));
            NodeId candidate = vendorPump
                ? Id("vendor-pump")
                : new NodeId(Pumps.ObjectTypes.MultiPumpType, wire.NamespaceIndex(Pumps.Namespaces.Pumps));
            wire.NodeCache.Setup(cache => cache.IsTypeOfAsync(candidate, pumpType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(vendorPump);

            JsonObject json = Result(await Tools(provider).ClassifyTypeAsync(
                candidate.ToString(), "selected").ConfigureAwait(false));

            Assert.That(json["typeDefinitionId"]!.GetValue<string>(), Is.EqualTo(candidate.ToString()));
            Assert.That(json["isPump"]!.GetValue<bool>(), Is.EqualTo(vendorPump));
            wire.NodeCache.Verify(cache => cache.IsTypeOfAsync(candidate, pumpType, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// Missing groups on a supported server are not errors; an unsupported namespace is an explicit refusal.
        /// </summary>
        [Test]
        public async Task ReadGroupDistinguishesMissingOptionalGroupAndUnsupportedNamespaceAsync()
        {
            await using ServiceProvider provider = Services();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var unsupported = new CompanionCoverageSession(manager.Telemetry, publishModels: false);
            await manager.RegisterExistingSessionAsync("unsupported", unsupported.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            CompanionCoverageSession supported = await RegisterAsync(provider).ConfigureAwait(false);

            JsonObject missing = Result(await Tools(provider).ReadGroupAsync(
                Id("pump").ToString(), PumpReadFacet.Configuration, sessionName: "selected").ConfigureAwait(false));
            JsonObject absentValues = Result(await Tools(provider).ReadGroupAsync(
                Id("pump").ToString(), PumpReadFacet.Signals, sessionName: "selected").ConfigureAwait(false));
            JsonObject refusal = Result(await Tools(provider).ReadGroupAsync(
                Id("pump").ToString(), PumpReadFacet.Configuration, sessionName: "unsupported").ConfigureAwait(false),
                true);

            AssertJson(missing, /*lang=json,strict*/ """{"data":null}""");
            AssertJson(absentValues,
                /*lang=json,strict*/ """{"nodeId":"i=0","available":false,"values":[],"groups":{}}""");
            Assert.That(refusal["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported.Code));
            Assert.That(unsupported.Paths, Is.Empty);
            Assert.That(supported.Reads, Is.Empty);
            Assert.That(supported.Calls, Is.Empty);
        }

        /// <summary>
        /// Invalid facets and null node identities are rejected before any server request.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidGroupSelectorsRejectWithoutReadingAsync(bool invalidFacet)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);

            JsonObject json = Result(await Tools(provider).ReadGroupAsync(
                invalidFacet ? Id("pump").ToString() : "i=0",
                invalidFacet ? (PumpReadFacet)999 : PumpReadFacet.Operational,
                sessionName: "selected").ConfigureAwait(false), true);

            Assert.That(json["errorType"]!.GetValue<string>(),
                Is.EqualTo(invalidFacet ? "ArgumentOutOfRangeException" : "ArgumentException"));
            Assert.That(wire.Paths, Is.Empty);
            Assert.That(wire.Browses, Is.Empty);
            Assert.That(wire.Reads, Is.Empty);
        }

        private static async Task<CompanionCoverageSession> RegisterAsync(ServiceProvider provider)
        {
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var wire = new CompanionCoverageSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("selected", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            return wire;
        }

        private static PumpsTools Tools(ServiceProvider provider)
        {
            return new PumpsTools(provider.GetRequiredService<OpcUaSessionManager>());
        }

        private static PumpValue Value(string name, Variant value, StatusCode status = default)
        {
            return new PumpValue { NodeId = Id(name), Name = name, Value = value, StatusCode = status };
        }

        private static PumpValueSet Set(string group, params PumpValue[] values)
        {
            return new PumpValueSet(Id(group), values);
        }

        private static void AssertGroup(JsonNode group, string node, string name, string value)
        {
            Assert.That(group["nodeId"]!.GetValue<string>(), Is.EqualTo(Id(node).ToString()));
            Assert.That(group["available"]!.GetValue<bool>(), Is.True);
            Assert.That(group["values"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(group["values"]![0]!["name"]!.GetValue<string>(), Is.EqualTo(name));
            AssertJson(group["values"]![0]!["value"], value);
        }

        private static EUInformation Pascals()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 20529,
                DisplayName = new LocalizedText("en-US", "Pa"),
                Description = new LocalizedText("en-US", "pascal")
            };
        }

        private static void MapGroup(
            CompanionCoverageSession wire,
            NodeId parent,
            string name,
            string? identifier = null,
            string? namespaceUri = null)
        {
            wire.MapPath(parent, Id(identifier ?? name), wire.Name(name, namespaceUri ?? Pumps.Namespaces.Pumps));
        }

        private static void PublishValues(
            CompanionCoverageSession wire,
            string group,
            params (string Name, Variant Value)[] values)
        {
            wire.SetBrowse(Id(group), values.Select(value =>
                Reference(Id(group + "/" + value.Name), value.Name, NodeClass.Variable)).ToArrayOf());
            foreach ((string name, Variant value) in values)
            {
                wire.SetValue(Id(group + "/" + name), value);
            }
        }

        private static void PublishPump(CompanionCoverageSession wire)
        {
            NodeId pump = Id("pump");
            MapGroup(wire, pump, "Identification", namespaceUri: Di.Namespaces.OpcUaDi);
            wire.Publish(Id("Identification"), "manufacturer", Variant.From(new LocalizedText("de-DE", "Maker")),
                wire.Name("Manufacturer", Di.Namespaces.OpcUaDi));
            wire.Publish(Id("Identification"), "serial", Variant.From("serial-42"),
                wire.Name("SerialNumber", Di.Namespaces.OpcUaDi));
            wire.Publish(Id("Identification"), "location", Variant.From("Cell 7"),
                wire.Name("Location", Machinery.Namespaces.Machinery));
            wire.Publish(Id("Identification"), "article", Variant.From("article-5"),
                wire.Name("ArticleNumber", Pumps.Namespaces.Pumps));
            MapGroup(wire, Id("Identification"), "Markings", "markings");
            MapGroup(wire, pump, "Configuration", namespaceUri: Di.Namespaces.OpcUaDi);
            foreach (string group in new[] { "Design", "Implementation", "SystemRequirements" })
            {
                MapGroup(wire, Id("Configuration"), group);
            }
            PublishValues(wire, "Design", ("RatedPower", Variant.From(12)));
            PublishValues(wire, "Implementation", ("Installed", Variant.From(false)));
            PublishValues(wire, "SystemRequirements", ("Required", Variant.From(20)));
            MapGroup(wire, pump, "Operational", namespaceUri: Di.Namespaces.OpcUaDi);
            foreach (string group in new[] { "Measurements", "Signals", "MultiPump" })
            {
                MapGroup(wire, Id("Operational"), group);
            }
            PublishValues(wire, "Signals", ("Input", Variant.From(false)));
            ArrayOf<string> collective = ["P1", "P2"];
            PublishValues(wire, "MultiPump",
                ("PumpRole", Variant.From((int)PumpRoleEnum.Master)),
                ("NumberOfPumps", Variant.From(2u)),
                ("PumpCollectiveIDs", Variant.From(collective)));
            wire.SetBrowse(Id("Measurements"),
            [
                Reference(Id("speed"), "Speed", NodeClass.Variable),
                Reference(Id("enabled"), "Enabled", NodeClass.Variable),
                Reference(Id("flow"), "Flow", NodeClass.Variable),
                Reference(Id("Vibration"), "Vibration", typeDefinition: new NodeId(
                    Pumps.ObjectTypes.VibrationMeasurementType, wire.NamespaceIndex(Pumps.Namespaces.Pumps)))
            ]);
            wire.SetValue(Id("speed"), Variant.From(0.0));
            wire.SetDataValue(Id("enabled"),
                new DataValue(Variant.From(false), StatusCodes.UncertainLastUsableValue, s_timestamp));
            wire.SetValue(Id("flow"), Variant.From(double.NaN));
            wire.Publish(Id("enabled"), "enabled-units", Variant.FromStructure(Pascals()),
                new QualifiedName("EngineeringUnits"));
            wire.Publish(Id("enabled"), "enabled-range", Variant.FromStructure(new Range { Low = -5, High = 250 }),
                new QualifiedName("EURange"));
            PublishValues(wire, "Vibration", ("Peak", Variant.From(2.5)));
            MapGroup(wire, pump, "Events");
            MapGroup(wire, Id("Events"), "SupervisionProcessFluid");
            PublishValues(wire, "SupervisionProcessFluid", ("Cavitation", Variant.From(true)));
            MapGroup(wire, pump, "Maintenance", namespaceUri: Di.Namespaces.OpcUaDi);
            MapGroup(wire, Id("Maintenance"), "GeneralMaintenance");
            MapGroup(wire, Id("Maintenance"), "BreakdownMaintenance");
            PublishValues(wire, "GeneralMaintenance",
                ("StateOfTheItem", Variant.From((int)StateOfTheItemEnum.OperatingState)),
                ("MaintenanceLevel", Variant.From((int)MaintenanceLevelEnum.Level2)));
            PublishValues(wire, "BreakdownMaintenance", ("Failure", Variant.From(false)));
            MapGroup(wire, pump, "Documentation");
            PublishValues(wire, "Documentation", ("Manual", Variant.From("urn:manual")));
        }

        private static void PublishPorts(CompanionCoverageSession wire)
        {
            MapGroup(wire, Id("pump"), "Ports");
            wire.SetBrowse(Id("Ports"),
            [
                Reference(Id("port-1"), "Drive", typeDefinition: new NodeId(
                    Pumps.ObjectTypes.DrivePortType, wire.NamespaceIndex(Pumps.Namespaces.Pumps))),
                Reference(Id("port-2"), "VendorInlet", typeDefinition: Id("vendor-port")),
                Reference(Id("port-3"), "Outlet", typeDefinition: new NodeId(
                    Pumps.ObjectTypes.OutletConnectionPortType, wire.NamespaceIndex(Pumps.Namespaces.Pumps)))
            ]);
            PublishValues(wire, "port-2",
                ("Direction", Variant.From((int)PortDirectionEnum.In)),
                ("Category", Variant.From("Hydraulic")),
                ("IdCarrier", Variant.From(string.Empty)));
            MapGroup(wire, Id("port-2"), "Measurements", "port-measurements");
            PublishValues(wire, "port-measurements", ("Pressure", Variant.From(50.0)));
        }

        private static readonly DateTime s_timestamp = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        private static readonly string[] s_supervisionGroups =
        [
            "SupervisionProcessFluid", "SupervisionPumpOperation", "SupervisionMechanics",
            "SupervisionHardware", "SupervisionSoftware", "SupervisionElectronics", "SupervisionAuxiliaryDevice"
        ];

        private static readonly string[] s_maintenanceGroups =
        [
            "GeneralMaintenance", "PreventiveMaintenance", "ConditionBasedMaintenance", "BreakdownMaintenance"
        ];
    }
}
#endif
