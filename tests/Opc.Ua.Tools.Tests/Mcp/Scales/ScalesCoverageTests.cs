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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PackML;
using Opc.Ua.Scales;
using Opc.Ua.Tests;
using static Opc.Ua.Tools.Tests.McpCompanion.CompanionCoverageSession;
using ScaleNames = Opc.Ua.Scales.BrowseNames;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises Scales MCP contracts through explicit projections and real clients over a strict wire session.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class ScalesCoverageTests
    {
        /// <summary>
        /// Nameplates retain every optional field, localized text and the original operation date.
        /// </summary>
        [Test]
        public void IdentificationPreservesEveryFieldAndOptionalAbsence()
        {
            var value = new ScaleIdentification
            {
                Manufacturer = new LocalizedText("de-DE", "Waagen"),
                SerialNumber = "serial-9",
                ProductInstanceUri = "urn:scale:9",
                ManufacturerUri = "urn:manufacturer",
                Model = new LocalizedText("en-US", "Precision"),
                ProductCode = "product-4",
                HardwareRevision = "H2",
                SoftwareRevision = "S3",
                DeviceClass = "Laboratory",
                AssetId = string.Empty,
                ComponentName = new LocalizedText("fr-FR", "Balance"),
                Location = "Line 2",
                YearOfConstruction = 2025,
                MonthOfConstruction = 12,
                InitialOperationDate = s_timestamp
            };

            AssertJson(ScalesJson.Identification(value), /*lang=json,strict*/ """
                {
                  "manufacturer":{"text":"Waagen","locale":"de-DE"},
                  "serialNumber":"serial-9","productInstanceUri":"urn:scale:9",
                  "manufacturerUri":"urn:manufacturer","model":{"text":"Precision","locale":"en-US"},
                  "productCode":"product-4","hardwareRevision":"H2","softwareRevision":"S3",
                  "deviceClass":"Laboratory","assetId":"","componentName":{"text":"Balance","locale":"fr-FR"},
                  "location":"Line 2","yearOfConstruction":2025,"monthOfConstruction":12,
                  "initialOperationDate":"2026-02-03T04:05:06Z"
                }
                """);
            AssertJson(ScalesJson.Identification(new ScaleIdentification()), /*lang=json,strict*/ """
                {
                  "manufacturer":null,"serialNumber":null,"productInstanceUri":null,"manufacturerUri":null,
                  "model":null,"productCode":null,"hardwareRevision":null,"softwareRevision":null,
                  "deviceClass":null,"assetId":null,"componentName":null,"location":null,
                  "yearOfConstruction":null,"monthOfConstruction":null,"initialOperationDate":null
                }
                """);
        }

        /// <summary>
        /// Published non-finite weights remain available and keep quality, flags, units and timestamp.
        /// </summary>
        [Test]
        public void WeightPreservesNonFiniteValuesFalseFlagsAndQuality()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var reading = new ScaleReading
            {
                Gross = double.NaN,
                Net = double.PositiveInfinity,
                Tare = double.NegativeInfinity,
                TareMode = TareMode.PresetTare_2,
                Overload = false,
                Underload = true,
                Stable = false,
                InsideZero = false,
                CurrentRangeId = 0,
                WeightId = string.Empty,
                EngineeringUnits = Kilograms(),
                Timestamp = s_timestamp,
                StatusCode = StatusCodes.UncertainLastUsableValue
            };

            JsonObject json = ScalesJson.Weight(reading, wire.Context);

            Assert.That(json["available"]!.GetValue<bool>(), Is.True);
            JsonNode value = json["reading"]!;
            Assert.That(value["gross"]!.GetValue<string>(), Is.EqualTo("NaN"));
            Assert.That(value["net"]!.GetValue<string>(), Is.EqualTo("Infinity"));
            Assert.That(value["tare"]!.GetValue<string>(), Is.EqualTo("-Infinity"));
            Assert.That(value["tareMode"]!.GetValue<string>(), Is.EqualTo("PresetTare_2"));
            Assert.That(value["tareModeValue"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(value["overload"]!.GetValue<bool>(), Is.False);
            Assert.That(value["underload"]!.GetValue<bool>(), Is.True);
            Assert.That(value["stable"]!.GetValue<bool>(), Is.False);
            Assert.That(value["insideZero"]!.GetValue<bool>(), Is.False);
            Assert.That(value["currentRangeId"]!.GetValue<ushort>(), Is.Zero);
            Assert.That(value["weightId"]!.GetValue<string>(), Is.Empty);
            Assert.That(value["sourceTimestamp"]!.GetValue<DateTime>(), Is.EqualTo(s_timestamp));
            Assert.That(value["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.UncertainLastUsableValue.Code));
            Assert.That(value["statusCode"]!.GetValue<string>(), Does.Contain("UncertainLastUsableValue"));
            Assert.That(value["engineeringUnits"]!["UnitId"]!.GetValue<int>(), Is.EqualTo(4933453));
            Assert.That(value["engineeringUnits"]!["DisplayName"]!["Text"]!.GetValue<string>(), Is.EqualTo("kg"));
            AssertJson(ScalesJson.Weight(null, wire.Context),
                /*lang=json,strict*/ """{"available":false,"reading":null}""");

            JsonNode empty = ScalesJson.Weight(new ScaleReading(), wire.Context)["reading"]!;
            Assert.That(empty["gross"]!.GetValue<double>(), Is.Zero);
            Assert.That(empty["net"]!.GetValue<double>(), Is.Zero);
            Assert.That(empty["tare"]!.GetValue<double>(), Is.Zero);
            Assert.That(empty["stable"], Is.Null);
            Assert.That(empty["insideZero"], Is.Null);
            Assert.That(empty["currentRangeId"], Is.Null);
            Assert.That(empty["engineeringUnits"], Is.Null);
        }

        /// <summary>
        /// Range projection does not silently discard infinities or substitute a unit for an absent one.
        /// </summary>
        [Test]
        public void RangePreservesBoundsIntervalsAndUnits()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            JsonObject value = ScalesJson.Range(new WeighingRangeDefinition(
                double.NegativeInfinity, double.PositiveInfinity, 0, double.NaN)
            {
                EngineeringUnits = Kilograms()
            }, wire.Context);

            Assert.That(value["low"]!.GetValue<string>(), Is.EqualTo("-Infinity"));
            Assert.That(value["high"]!.GetValue<string>(), Is.EqualTo("Infinity"));
            Assert.That(value["actualScaleInterval"]!.GetValue<double>(), Is.Zero);
            Assert.That(value["verificationScaleInterval"]!.GetValue<string>(), Is.EqualTo("NaN"));
            Assert.That(value["engineeringUnits"]!["UnitId"]!.GetValue<int>(), Is.EqualTo(4933453));
            AssertJson(ScalesJson.Range(new WeighingRangeDefinition(-2, 20, 0.25, 0.5), wire.Context),
                /*lang=json,strict*/ """
                {"low":-2,"high":20,"actualScaleInterval":0.25,"verificationScaleInterval":0.5,
                 "engineeringUnits":null}
                """);
        }

        /// <summary>
        /// Vendor events and inactive standard alarms retain different identities and optional state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NotificationPreservesVendorIdentityAndInactiveAlarmState(bool vendor)
        {
            var notification = new ScaleNotificationInfo
            {
                SourceNode = Id("scale"),
                SourceName = "Filler",
                Time = s_timestamp,
                Severity = 0,
                Message = new LocalizedText("en-GB", "Cleared"),
                Category = ScaleNotificationCategory.Process,
                NotificationId = vendor ? 5001u : (uint)ScaleNotificationId.LabelFault,
                VendorNotificationId = vendor ? "vendor-label" : null,
                AuxParameters = ["printer-2", string.Empty],
                IsAlarm = !vendor,
                Active = vendor ? null : false
            };

            JsonObject json = ScalesJson.Notification(notification);

            Assert.That(json["sourceNode"]!.GetValue<string>(), Is.EqualTo("ns=1;s=scale"));
            Assert.That(json["sourceName"]!.GetValue<string>(), Is.EqualTo("Filler"));
            Assert.That(json["time"]!.GetValue<DateTime>(), Is.EqualTo(s_timestamp));
            Assert.That(json["severity"]!.GetValue<ushort>(), Is.Zero);
            AssertJson(json["message"], /*lang=json,strict*/ """{"text":"Cleared","locale":"en-GB"}""");
            Assert.That(json["category"]!.GetValue<string>(), Is.EqualTo("Process"));
            Assert.That(json["categoryValue"]!.GetValue<uint>(), Is.EqualTo(1));
            Assert.That(json["notificationId"]!.GetValue<uint>(), Is.EqualTo(vendor ? 5001u : 102u));
            Assert.That(json["definedId"]?.GetValue<string>(), Is.EqualTo(vendor ? null : "LabelFault"));
            Assert.That(json["vendorNotificationId"]?.GetValue<string>(), Is.EqualTo(vendor ? "vendor-label" : null));
            AssertJson(json["auxParameters"], """["printer-2",""]""");
            Assert.That(json["isAlarm"]!.GetValue<bool>(), Is.EqualTo(!vendor));
            Assert.That(json["active"]?.GetValue<bool>(), Is.EqualTo(vendor ? null : (bool?)false));
        }

        /// <summary>
        /// Standard no-argument commands dispatch the selected method exactly once on the supplied owner.
        /// </summary>
        [TestCase(ScalesStandardCommand.DischargeStart, "DischargeStart")]
        [TestCase(ScalesStandardCommand.DischargeStop, "DischargeStop")]
        [TestCase(ScalesStandardCommand.RefillStart, "RefillStart")]
        [TestCase(ScalesStandardCommand.RefillStop, "RefillStop")]
        [TestCase(ScalesStandardCommand.StartLeveling, "StartLeveling")]
        [TestCase(ScalesStandardCommand.StartCalibration, "StartCalibration")]
        [TestCase(ScalesStandardCommand.StartIonisator, "StartIonisator")]
        [TestCase(ScalesStandardCommand.StopIonisator, "StopIonisator")]
        [TestCase(ScalesStandardCommand.ResetTotalizer, "ResetTotalizer")]
        [TestCase(ScalesStandardCommand.ResetGlobalStatistics, "ResetGlobalStatistics")]
        public async Task StandardCommandDispatchesOnlySelectedMethodAsync(
            ScalesStandardCommand command,
            string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("owner");
            MapMethod(wire, owner, method);
            wire.ReplyToCalls();

            JsonObject json = Result(await Commands(provider).StandardCommandAsync(
                owner.ToString(), command, "selected").ConfigureAwait(false));

            AssertCompleted(json, owner);
            AssertCall(wire, owner, 0);
        }

        /// <summary>
        /// Preset commands target ProductionPreset and never acquire a product lock or chain a selection.
        /// </summary>
        [TestCase("RemoveProduct")]
        [TestCase("SelectProduct")]
        [TestCase("DeselectProduct")]
        [TestCase("SwitchProduct")]
        public async Task ProductCommandsPreserveIdentifierWithoutLockingAsync(string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("scale");
            NodeId preset = Id("preset");
            wire.MapPath(owner, preset, ScaleName(wire, "ProductionPreset"));
            MapMethod(wire, preset, method);
            wire.ReplyToCalls();
            ScalesCommandTools tools = Commands(provider);

            CallToolResult result = method switch
            {
                "RemoveProduct" => await tools.RemoveProductAsync(owner.ToString(), "lot-A", "selected")
                    .ConfigureAwait(false),
                "SelectProduct" => await tools.SelectProductAsync(owner.ToString(), "lot-A", "selected")
                    .ConfigureAwait(false),
                "DeselectProduct" => await tools.DeselectProductAsync(owner.ToString(), "lot-A", "selected")
                    .ConfigureAwait(false),
                _ => await tools.SwitchProductAsync(owner.ToString(), "lot-A", "selected").ConfigureAwait(false)
            };

            AssertCompleted(Result(result), owner);
            CallMethodRequest call = AssertCall(wire, preset, 1);
            Assert.That(call.InputArguments[0].TryGetValue(out string product), Is.True);
            Assert.That(product, Is.EqualTo("lot-A"));
            Assert.That(wire.Reads, Is.Empty);
            Assert.That(wire.Paths, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Unit-taking commands keep Double, UInt32 and Float distinct and pass the complete unit structure.
        /// </summary>
        [TestCase("SetPresetTare")]
        [TestCase("SetReferencePieceWeight")]
        [TestCase("SetFeederSpeed")]
        public async Task UnitCommandsPreserveNumericTypesAndEngineeringUnitsAsync(string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("module");
            MapMethod(wire, owner, method);
            wire.ReplyToCalls();
            var unit = new ScalesEngineeringUnit
            {
                NamespaceUri = "urn:vendor:units",
                UnitId = -7,
                DisplayName = "unit",
                Description = "Vendor unit"
            };
            ScalesCommandTools tools = Commands(provider);

            CallToolResult result = method switch
            {
                "SetPresetTare" => await tools.SetPresetTareAsync(owner.ToString(), 1.25, unit, "selected")
                    .ConfigureAwait(false),
                "SetReferencePieceWeight" => await tools.SetReferencePieceWeightAsync(
                    owner.ToString(), uint.MaxValue, unit, "selected").ConfigureAwait(false),
                _ => await tools.SetFeederSpeedAsync(owner.ToString(), 0.5f, unit, "selected").ConfigureAwait(false)
            };

            AssertCompleted(Result(result), owner);
            CallMethodRequest call = AssertCall(wire, owner, 2);
            switch (method)
            {
                case "SetPresetTare":
                    Assert.That(call.InputArguments[0].TryGetValue(out double tare), Is.True);
                    Assert.That(tare, Is.EqualTo(1.25));
                    break;
                case "SetReferencePieceWeight":
                    Assert.That(call.InputArguments[0].TryGetValue(out uint weight), Is.True);
                    Assert.That(weight, Is.EqualTo(uint.MaxValue));
                    break;
                default:
                    Assert.That(call.InputArguments[0].TryGetValue(out float speed), Is.True);
                    Assert.That(speed, Is.EqualTo(0.5f));
                    break;
            }
            Assert.That(call.InputArguments[1].TryGetStructure<EUInformation>(
                wire.Context, out EUInformation? encoded), Is.True);
            Assert.That(encoded!.NamespaceUri, Is.EqualTo("urn:vendor:units"));
            Assert.That(encoded.UnitId, Is.EqualTo(-7));
            Assert.That(encoded.DisplayName.Text, Is.EqualTo("unit"));
            Assert.That(encoded.Description.Text, Is.EqualTo("Vendor unit"));
        }

        /// <summary>
        /// Missing and empty labels use UA null semantics without changing a zero unit identifier.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void EngineeringUnitNormalizesMissingAndEmptyLabelsWithoutChangingZeroId(string? label)
        {
            EUInformation units = new ScalesEngineeringUnit
            {
                UnitId = 0,
                DisplayName = label,
                Description = label
            }.ToUnits();

            Assert.That(units.UnitId, Is.Zero);
            Assert.That(units.NamespaceUri, Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
            Assert.That(units.DisplayName.IsNull, Is.True);
            Assert.That(units.Description.IsNull, Is.True);
        }

        /// <summary>
        /// Reference acquisition and reference-piece counts retain UInt32 boundary inputs.
        /// </summary>
        [TestCase(false, 0u)]
        [TestCase(false, uint.MaxValue)]
        [TestCase(true, 0u)]
        [TestCase(true, uint.MaxValue)]
        public async Task ReferenceCommandsPreserveUnsignedCountAsync(bool start, uint count)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("counter");
            MapMethod(wire, owner, start ? "StartReference" : "SetNumberOfReferencePieces");
            wire.ReplyToCalls();
            ScalesCommandTools tools = Commands(provider);

            CallToolResult result = start
                ? await tools.StartReferenceAsync(owner.ToString(), count, "selected").ConfigureAwait(false)
                : await tools.SetNumberOfReferencePiecesAsync(owner.ToString(), count, "selected")
                    .ConfigureAwait(false);

            AssertCompleted(Result(result), owner);
            CallMethodRequest call = AssertCall(wire, owner, 1);
            Assert.That(call.InputArguments[0].TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(count));
        }

        /// <summary>
        /// Opening and closing shields dispatch different methods but preserve the same selected shield.
        /// </summary>
        [TestCase(false, "OpenDraftShields")]
        [TestCase(true, "CloseDraftShields")]
        public async Task DraftShieldCommandsSelectDirectionAndPreserveShieldAsync(bool close, string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("laboratory");
            MapMethod(wire, owner, method);
            wire.ReplyToCalls();

            JsonObject json = Result(await Commands(provider).SetDraftShieldsAsync(
                owner.ToString(), DraftShieldType.All_3, close, "selected").ConfigureAwait(false));

            AssertCompleted(json, owner);
            CallMethodRequest call = AssertCall(wire, owner, 1);
            Assert.That(call.InputArguments[0].TryGetValue(out int shield), Is.True);
            Assert.That(shield, Is.EqualTo(3));
        }

        /// <summary>
        /// Each vehicle operation carries the vehicle identifier to only its selected method.
        /// </summary>
        [TestCase(ScalesVehicleCommand.Inbound, "InboundWeighing")]
        [TestCase(ScalesVehicleCommand.Outbound, "OutboundWeighing")]
        [TestCase(ScalesVehicleCommand.OnePass, "OnePassWeighing")]
        public async Task VehicleCommandsDispatchOnceWithVehicleIdAsync(ScalesVehicleCommand command, string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("weighbridge");
            MapMethod(wire, owner, method);
            wire.ReplyToCalls();

            JsonObject json = Result(await Commands(provider).WeighVehicleAsync(
                owner.ToString(), command, "truck-7", "selected").ConfigureAwait(false));

            AssertCompleted(json, owner);
            CallMethodRequest call = AssertCall(wire, owner, 1);
            Assert.That(call.InputArguments[0].TryGetValue(out string vehicle), Is.True);
            Assert.That(vehicle, Is.EqualTo("truck-7"));
        }

        /// <summary>
        /// Recipe commands use the scale as owner and the recipe NodeId as their single typed argument.
        /// </summary>
        [TestCase(ScalesRecipeCommand.Start, "StartRecipe")]
        [TestCase(ScalesRecipeCommand.Stop, "StopRecipe")]
        [TestCase(ScalesRecipeCommand.Continue, "ContinueRecipe")]
        [TestCase(ScalesRecipeCommand.SkipCurrentElement, "SkipCurrentRecipeElement")]
        [TestCase(ScalesRecipeCommand.Abort, "AbortRecipe")]
        public async Task RecipeCommandsKeepScaleOwnerAndRecipeArgumentAsync(ScalesRecipeCommand command, string method)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId owner = Id("recipe-scale");
            MapMethod(wire, owner, method);
            wire.ReplyToCalls();

            JsonObject json = Result(await Commands(provider).ProcessRecipeAsync(
                owner.ToString(), Id("recipe").ToString(), command, "selected").ConfigureAwait(false));

            AssertCompleted(json, owner);
            CallMethodRequest call = AssertCall(wire, owner, 1);
            Assert.That(call.InputArguments[0].TryGetValue(out NodeId recipe), Is.True);
            Assert.That(recipe, Is.EqualTo(Id("recipe")));
        }

        /// <summary>
        /// PackML dispatch uses the required nested state-machine owner without automatic transitions.
        /// </summary>
        [TestCase(PackMLCommand.Abort, "state")]
        [TestCase(PackMLCommand.Clear, "state")]
        [TestCase(PackMLCommand.Stop, "machine")]
        [TestCase(PackMLCommand.Start, "execute")]
        public async Task PackMLCommandUsesCorrectStateMachineLevelAsync(PackMLCommand command, string target)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId scale = Id("scale");
            QualifiedName state = ScaleName(wire, "State");
            QualifiedName machine = wire.Name("MachineState", PackML.Namespaces.PackML);
            QualifiedName execute = wire.Name("ExecuteState", PackML.Namespaces.PackML);
            wire.MapPath(scale, Id("state"), state);
            wire.MapPath(scale, Id("machine"), state, machine);
            wire.MapPath(scale, Id("execute"), state, machine, execute);
            wire.MapPath(Id(target), Id("method"), wire.Name(command.ToString(), PackML.Namespaces.PackML));
            wire.ReplyToCalls();

            AssertCompleted(Result(await Commands(provider).PackMLCommandAsync(
                scale.ToString(), command, "selected").ConfigureAwait(false)), scale);

            CallMethodRequest call = AssertCall(wire, Id(target), command == PackMLCommand.Start ? 1 : 0);
            if (command == PackMLCommand.Start)
            {
                Assert.That(call.InputArguments[0].TryGetValue(out ArrayOf<ExtensionObject> inputs), Is.True);
                Assert.That(inputs.IsEmpty, Is.True);
            }
        }

        /// <summary>
        /// Product creation preserves optional type selection and returns the server-created identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AddProductReturnsCreatedNodeWithoutSelectingItAsync(bool customType)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId scale = Id("scale");
            NodeId preset = Id("preset");
            wire.MapPath(scale, preset, ScaleName(wire, "ProductionPreset"));
            MapMethod(wire, preset, "AddProduct");
            wire.ReplyToCalls([Variant.From(Id("created"))]);

            JsonObject json = Result(await Commands(provider).AddProductAsync(
                scale.ToString(), "Flour", "flour-42", customType ? Id("product-type").ToString() : null,
                "selected").ConfigureAwait(false));

            AssertCompleted(json, Id("created"));
            CallMethodRequest call = AssertCall(wire, preset, 3);
            Assert.That(call.InputArguments[0].TryGetValue(out string name), Is.True);
            Assert.That(name, Is.EqualTo("Flour"));
            Assert.That(call.InputArguments[1].TryGetValue(out string productId), Is.True);
            Assert.That(productId, Is.EqualTo("flour-42"));
            Assert.That(call.InputArguments[2].TryGetValue(out NodeId productType), Is.True);
            Assert.That(productType, Is.EqualTo(customType ? Id("product-type") : NodeId.Null));
        }

        /// <summary>
        /// Recipe creation targets Recipes and keeps the supplied locale without starting processing.
        /// </summary>
        [Test]
        public async Task AddRecipePreservesLocalizedNameAndCreatedNodeAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId scale = Id("scale");
            NodeId recipes = Id("recipes");
            wire.MapPath(scale, recipes, ScaleName(wire, "Recipes"));
            MapMethod(wire, recipes, "AddRecipe");
            wire.ReplyToCalls([Variant.From(Id("recipe-created"))]);

            AssertCompleted(Result(await Commands(provider).AddRecipeAsync(
                scale.ToString(), "batch-9", "Mélange", "fr-FR", "selected").ConfigureAwait(false)),
                Id("recipe-created"));

            CallMethodRequest call = AssertCall(wire, recipes, 2);
            Assert.That(call.InputArguments[0].TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("batch-9"));
            Assert.That(call.InputArguments[1].TryGetValue(out LocalizedText name), Is.True);
            Assert.That(name.Text, Is.EqualTo("Mélange"));
            Assert.That(name.Locale, Is.EqualTo("fr-FR"));
        }

        /// <summary>
        /// A successful method with no created identity is an error, not a fictitious created node.
        /// </summary>
        [Test]
        public async Task AddRecipeRejectsSuccessfulResponseWithoutCreatedNodeAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            wire.MapPath(Id("scale"), Id("recipes"), ScaleName(wire, "Recipes"));
            MapMethod(wire, Id("recipes"), "AddRecipe");
            wire.ReplyToCalls();

            JsonObject json = Result(await Commands(provider).AddRecipeAsync(
                Id("scale").ToString(), "recipe", "Recipe", sessionName: "selected").ConfigureAwait(false), true);

            Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(json["message"]!.GetValue<string>(), Does.Contain("created NodeId"));
            Assert.That(json.ContainsKey("completed"), Is.False);
            AssertCall(wire, Id("recipes"), 2);
        }

        /// <summary>
        /// RegisterWeight dispatches once and distinguishes a missing record from the read-back measurement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegisterWeightReadsBackOnlyAfterOneRegistrationAsync(bool published)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId scale = Id("scale");
            MapMethod(wire, scale, "RegisterWeight");
            wire.ReplyToCalls();
            if (published)
            {
                wire.Publish(scale, "registered",
                    Variant.FromStructure(new WeightType { Gross = 4.25, Net = 3, Tare = 1.25 }),
                    ScaleName(wire, "RegisteredWeight"));
            }

            JsonObject json = Result(await Commands(provider).RegisterWeightAsync(
                scale.ToString(), "selected").ConfigureAwait(false));

            Assert.That(json["completed"]!.GetValue<bool>(), Is.True);
            Assert.That(json["available"]!.GetValue<bool>(), Is.EqualTo(published));
            AssertCall(wire, scale, 0);
            if (published)
            {
                Assert.That(json["reading"]!["gross"]!.GetValue<double>(), Is.EqualTo(4.25));
                Assert.That(json["reading"]!["net"]!.GetValue<double>(), Is.EqualTo(3));
                Assert.That(json["reading"]!["tare"]!.GetValue<double>(), Is.EqualTo(1.25));
                Assert.That(wire.Reads.Select(read => read.NodeId), Is.EqualTo([Id("registered")]));
            }
            else
            {
                Assert.That(json["reading"], Is.Null);
                Assert.That(wire.Reads, Is.Empty);
            }
        }

        /// <summary>
        /// A wire refusal is retained and never retried or converted to successful completion.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClearTarePreservesRawRefusalWithoutRetryAsync(bool invalidState)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            MapMethod(wire, Id("scale"), "ClearTare");
            StatusCode status = invalidState ? StatusCodes.BadInvalidState : StatusCodes.BadUserAccessDenied;
            wire.ReplyToCalls(status: status);

            JsonObject json = Result(await Commands(provider).ClearTareAsync(
                Id("scale").ToString(), "selected").ConfigureAwait(false), true);

            Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(status.Code));
            Assert.That(json.ContainsKey("completed"), Is.False);
            AssertCall(wire, Id("scale"), 0);
        }

        /// <summary>
        /// Finite command selectors cannot be used as arbitrary method dispatch.
        /// </summary>
        [TestCase("standard")]
        [TestCase("vehicle")]
        [TestCase("recipe")]
        [TestCase("shield")]
        [TestCase("packml")]
        public async Task UndefinedCommandSelectorsRejectBeforeWireOperationsAsync(string selector)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            ScalesCommandTools tools = Commands(provider);
            string scale = Id("scale").ToString();

            CallToolResult result = selector switch
            {
                "standard" => await tools.StandardCommandAsync(
                    scale, (ScalesStandardCommand)999, "selected").ConfigureAwait(false),
                "vehicle" => await tools.WeighVehicleAsync(
                    scale, (ScalesVehicleCommand)999, "truck", "selected").ConfigureAwait(false),
                "recipe" => await tools.ProcessRecipeAsync(
                    scale, Id("recipe").ToString(), (ScalesRecipeCommand)999, "selected").ConfigureAwait(false),
                "shield" => await tools.SetDraftShieldsAsync(
                    scale, (DraftShieldType)999, false, "selected").ConfigureAwait(false),
                _ => await tools.PackMLCommandAsync(scale, (PackMLCommand)999, "selected").ConfigureAwait(false)
            };

            Assert.That(Result(result, true)["errorType"]!.GetValue<string>(),
                Is.EqualTo(nameof(ArgumentOutOfRangeException)));
            Assert.That(wire.Paths, Is.Empty);
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Schema decoration changes only recipe predecessors to the documented bounded JSON array.
        /// </summary>
        [Test]
        public async Task RecipeSchemaAdvertisesBoundedStringArrayWithoutChangingOtherInputsAsync()
        {
            using var schema = JsonDocument.Parse("""
                {"type":"object","properties":{"input":{"type":"object","properties":{
                  "elementName":{"type":"string"},"previousElements":{"type":"object"}}}}}
                """);
            var recipe = new Tool { Name = "scales_add_recipe_element", InputSchema = schema.RootElement.Clone() };
            var other = new Tool { Name = "scales_set_tare", InputSchema = schema.RootElement.Clone() };
            var response = new ListToolsResult { Tools = [other, recipe] };
            int calls = 0;
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter = ScalesMcpFilters.AddRecipeSchema(
                (_, _) =>
                {
                    calls++;
                    return ValueTask.FromResult(response);
                });

            ListToolsResult result = await filter(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(response));
            Assert.That(calls, Is.EqualTo(1));
            JsonElement properties = recipe.InputSchema.GetProperty("properties")
                .GetProperty("input").GetProperty("properties");
            JsonElement predecessors = properties.GetProperty("previousElements");
            Assert.That(predecessors.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(predecessors.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(predecessors.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(predecessors.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
            Assert.That(properties.GetProperty("elementName").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(other.InputSchema.GetRawText(), Is.EqualTo(schema.RootElement.GetRawText()));
        }

        /// <summary>
        /// A snapshot pages each collection independently and distinguishes absent from published measurements.
        /// </summary>
        [Test]
        public async Task ReadSnapshotPagesEachCollectionAndKeepsMissingRegisteredWeightAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishSnapshot(wire);
            var tools = new ScalesReadTools(provider.GetRequiredService<ScalesClientAccessor>());

            JsonObject json = Result(await tools.ReadAsync(
                Id("scale").ToString(), ScalesReadFacet.Snapshot, 1, 1, "selected").ConfigureAwait(false));

            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=scale"));
            Assert.That(json["facet"]!.GetValue<string>(), Is.EqualTo("Snapshot"));
            Assert.That(json["consistency"]!.GetValue<string>(), Is.EqualTo("live"));
            AssertJson(json["Identification"]!["manufacturer"],
                /*lang=json,strict*/ """{"text":"Factory","locale":"en-US"}""");
            Assert.That(json["CurrentWeight"]!["available"]!.GetValue<bool>(), Is.True);
            Assert.That(json["CurrentWeight"]!["reading"]!["gross"]!.GetValue<double>(), Is.EqualTo(7.5));
            Assert.That(json["CurrentWeight"]!["reading"]!["stable"]!.GetValue<bool>(), Is.False);
            AssertJson(json["RegisteredWeight"], /*lang=json,strict*/ """{"available":false,"reading":null}""");
            foreach (string collection in new[]
            {
                "WeighingRanges", "AllowedEngineeringUnits", "Products", "CurrentProducts"
            })
            {
                JsonNode page = json[collection]!;
                Assert.That(page["items"]!.AsArray(), Has.Count.EqualTo(1), collection);
                Assert.That(page["offset"]!.GetValue<int>(), Is.EqualTo(1), collection);
                Assert.That(page["nextOffset"]!.GetValue<int>(), Is.EqualTo(2), collection);
                Assert.That(page["hasMore"]!.GetValue<bool>(), Is.True, collection);
            }
            JsonNode range = json["WeighingRanges"]!["items"]![0]!;
            Assert.That(range["high"]!.GetValue<double>(), Is.EqualTo(20));
            Assert.That(range["low"]!.GetValue<double>(), Is.Zero);
            Assert.That(range["actualScaleInterval"]!.GetValue<double>(), Is.EqualTo(0.25));
            Assert.That(json["AllowedEngineeringUnits"]!["items"]![0]!["UnitId"]!.GetValue<int>(), Is.EqualTo(2));
            JsonNode product = json["Products"]!["items"]![0]!;
            Assert.That(product["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=product-2"));
            Assert.That(product["productId"]!.GetValue<string>(), Is.EqualTo("P2"));
            Assert.That(product["processing"]!.GetValue<bool>(), Is.False);
            Assert.That(product["lockNodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=actual-lock"));
            Assert.That(json["CurrentProducts"]!["items"]![0]!.GetValue<string>(), Is.EqualTo("P2"));
            Assert.That(json["PackMLState"]!.GetValue<uint>(), Is.EqualTo(PackMLStateNumbers.Execute));
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
            BrowsePath lockPath = wire.Paths.Single(path =>
                path.StartingNode == Id("product-2") && path.RelativePath.Elements[0].TargetName.Name == "Lock");
            Assert.That(lockPath.RelativePath.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(lockPath.RelativePath.Elements[0].TargetName.NamespaceIndex,
                Is.EqualTo(wire.NamespaceIndex(Di.Namespaces.OpcUaDi)));
        }

        /// <summary>
        /// All discovery scopes preserve vendor type identity and page scales separately from scale systems.
        /// </summary>
        [TestCase(ScalesDiscoveryScope.All)]
        [TestCase(ScalesDiscoveryScope.Under)]
        [TestCase(ScalesDiscoveryScope.System)]
        public async Task ListPreservesVendorTypeAndPagesAllDiscoveryScopesAsync(ScalesDiscoveryScope scope)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId vendor = Id("vendor-checkweigher-type");
            NodeId scaleType = new(Scales.ObjectTypes.ScaleDeviceType, wire.NamespaceIndex(Scales.Namespaces.Scales));
            NodeId systemType = new(Scales.ObjectTypes.ScaleSystemType, wire.NamespaceIndex(Scales.Namespaces.Scales));
            NodeId checkweigher = new(
                Scales.ObjectTypes.CheckweigherType, wire.NamespaceIndex(Scales.Namespaces.Scales));
            wire.NodeCache.Setup(cache => cache.IsTypeOfAsync(vendor, systemType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            wire.NodeCache.Setup(cache => cache.IsTypeOfAsync(vendor, scaleType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            wire.NodeCache.Setup(cache => cache.IsTypeOfAsync(systemType, systemType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            wire.NodeCache.Setup(cache => cache.FindSuperTypeAsync(vendor, It.IsAny<CancellationToken>()))
                .ReturnsAsync(checkweigher);
            ArrayOf<ReferenceDescription> entries =
            [
                Reference(Id("scale"), "VendorScale", typeDefinition: vendor),
                Reference(Id("system"), "System", typeDefinition: systemType)
            ];
            wire.SetBrowse(Id("folder"), entries);
            wire.MapPath(Id("system-parent"), Id("folder"), ScaleName(wire, "SubDevices"));
            wire.SetBrowse(new NodeId(Di.Objects.DeviceSet, wire.NamespaceIndex(Di.Namespaces.OpcUaDi)), entries);
            wire.SetBrowse(new NodeId(Machinery.Objects.Machines,
                wire.NamespaceIndex(Machinery.Namespaces.Machinery)), entries);
            string? parent = scope == ScalesDiscoveryScope.All
                ? null
                : (scope == ScalesDiscoveryScope.System ? Id("system-parent") : Id("folder")).ToString();
            var tools = new ScalesReadTools(provider.GetRequiredService<ScalesClientAccessor>());

            JsonObject first = Result(await tools.ListAsync(scope, parent, 0, 1, "selected").ConfigureAwait(false));
            JsonObject second = Result(await tools.ListAsync(scope, parent, 1, 1, "selected").ConfigureAwait(false));

            JsonNode scale = first["items"]![0]!;
            Assert.That(scale["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=scale"));
            Assert.That(scale["browseName"]!.GetValue<string>(), Is.EqualTo("1:VendorScale"));
            AssertJson(scale["displayName"], /*lang=json,strict*/ """{"text":"VendorScale","locale":"de-DE"}""");
            Assert.That(scale["typeDefinition"]!.GetValue<string>(), Is.EqualTo(vendor.ToString()));
            Assert.That(scale["kind"]!.GetValue<string>(), Is.EqualTo("Checkweigher"));
            Assert.That(scale["isScaleSystem"]!.GetValue<bool>(), Is.False);
            Assert.That(first["hasMore"]!.GetValue<bool>(), Is.True);
            Assert.That(first["nextOffset"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(second["items"]!.AsArray(), Has.Count.EqualTo(1));
            JsonNode system = second["items"]![0]!;
            Assert.That(system["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=system"));
            Assert.That(system["typeDefinition"]!.GetValue<string>(), Is.EqualTo(systemType.ToString()));
            Assert.That(system["kind"], Is.Null);
            Assert.That(system["isScaleSystem"]!.GetValue<bool>(), Is.True);
            Assert.That(second["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(second["nextOffset"], Is.Null);
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// A missing product lock is different from a malformed, remote or refused lock translation.
        /// </summary>
        [TestCase("missing", false)]
        [TestCase("empty", false)]
        [TestCase("remote", true)]
        [TestCase("partial", true)]
        [TestCase("multiple", true)]
        [TestCase("refused", true)]
        public async Task ReadProductsDistinguishesMissingLockFromInvalidTargetAsync(string response, bool error)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId product = Id("product");
            wire.MapPath(Id("scale"), Id("products"),
                ScaleName(wire, "ProductionPreset"), ScaleName(wire, "Products"));
            wire.SetBrowse(Id("products"), [Reference(product, "Product", typeDefinition: Id("vendor-product"))]);
            wire.Publish(product, "product-id", Variant.From("P0"), ScaleName(wire, "ProductId"));
            wire.Publish(product, "product-name", Variant.From(new LocalizedText("en", "Empty")),
                ScaleName(wire, "ProductName"));
            wire.TranslateOverride = path =>
            {
                if (path.StartingNode != product || path.RelativePath.Elements[0].TargetName.Name != "Lock")
                {
                    return null;
                }
                return response switch
                {
                    "missing" => new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch },
                    "empty" => new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = [] },
                    "refused" => new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied },
                    "multiple" => new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets =
                        [
                            new BrowsePathTarget { TargetId = Id("lock"), RemainingPathIndex = uint.MaxValue },
                            new BrowsePathTarget { TargetId = Id("lock-2"), RemainingPathIndex = uint.MaxValue }
                        ]
                    },
                    _ => new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets =
                        [
                            new BrowsePathTarget
                            {
                                TargetId = response == "remote"
                                    ? new ExpandedNodeId("lock", 1, null, 2)
                                    : Id("lock"),
                                RemainingPathIndex = response == "partial" ? 0 : uint.MaxValue
                            }
                        ]
                    }
                };
            };
            var tools = new ScalesReadTools(provider.GetRequiredService<ScalesClientAccessor>());

            JsonObject json = Result(await tools.ReadAsync(
                Id("scale").ToString(), ScalesReadFacet.Products, sessionName: "selected")
                .ConfigureAwait(false), error);

            if (error)
            {
                Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(response == "refused"
                    ? StatusCodes.BadUserAccessDenied.Code : StatusCodes.BadUnexpectedError.Code));
            }
            else
            {
                JsonNode item = json["value"]!["items"]![0]!;
                Assert.That(item["productId"]!.GetValue<string>(), Is.EqualTo("P0"));
                Assert.That(item["processing"], Is.Null);
                Assert.That(item["lockNodeId"], Is.Null);
                Assert.That(json["value"]!["hasMore"]!.GetValue<bool>(), Is.False);
            }
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Unsupported servers and absent optional weights are reported differently.
        /// </summary>
        [Test]
        public async Task ReadDistinguishesUnsupportedNamespaceFromMissingWeightAsync()
        {
            await using ServiceProvider provider = Services();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var unsupported = new CompanionCoverageSession(manager.Telemetry, publishModels: false);
            await manager.RegisterExistingSessionAsync("unsupported", unsupported.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            CompanionCoverageSession supported = await RegisterAsync(provider).ConfigureAwait(false);
            var tools = new ScalesReadTools(provider.GetRequiredService<ScalesClientAccessor>());

            JsonObject unavailable = Result(await tools.ReadAsync(
                Id("scale").ToString(), ScalesReadFacet.CurrentWeight, sessionName: "unsupported")
                .ConfigureAwait(false), true);
            JsonObject missing = Result(await tools.ReadAsync(
                Id("scale").ToString(), ScalesReadFacet.CurrentWeight, sessionName: "selected").ConfigureAwait(false));

            Assert.That(unavailable["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported.Code));
            AssertJson(missing["value"], /*lang=json,strict*/ """{"available":false,"reading":null}""");
            Assert.That(unsupported.Paths, Is.Empty);
            Assert.That(supported.Reads, Is.Empty);
            Assert.That(supported.Paths, Has.Count.EqualTo(9));
        }

        /// <summary>
        /// Invalid selectors, unit namespaces and identifiers do not reach the wire.
        /// </summary>
        [TestCase("facet", "ArgumentOutOfRangeException")]
        [TestCase("scope", "ArgumentOutOfRangeException")]
        [TestCase("node", "ArgumentException")]
        [TestCase("product", "ArgumentException")]
        [TestCase("unit", "ArgumentException")]
        public async Task InvalidReadAndCommandInputsRejectWithoutRequestsAsync(string input, string errorType)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            var reads = new ScalesReadTools(provider.GetRequiredService<ScalesClientAccessor>());
            ScalesCommandTools commands = Commands(provider);

            CallToolResult result = input switch
            {
                "facet" => await reads.ReadAsync(
                    Id("scale").ToString(), (ScalesReadFacet)999, sessionName: "selected").ConfigureAwait(false),
                "scope" => await reads.ListAsync(
                    (ScalesDiscoveryScope)999, sessionName: "selected").ConfigureAwait(false),
                "node" => await reads.ReadAsync(
                    "i=0", ScalesReadFacet.Identification, sessionName: "selected").ConfigureAwait(false),
                "product" => await commands.SelectProductAsync(
                    Id("scale").ToString(), " ", "selected").ConfigureAwait(false),
                _ => await commands.SetPresetTareAsync(
                    Id("scale").ToString(), 1, new ScalesEngineeringUnit { UnitId = 1, NamespaceUri = " " },
                    "selected").ConfigureAwait(false)
            };

            Assert.That(Result(result, true)["errorType"]!.GetValue<string>(), Is.EqualTo(errorType));
            Assert.That(wire.Paths, Is.Empty);
            Assert.That(wire.Reads, Is.Empty);
            Assert.That(wire.Browses, Is.Empty);
            Assert.That(wire.Calls, Is.Empty);
        }

        private static async Task<CompanionCoverageSession> RegisterAsync(ServiceProvider provider)
        {
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var wire = new CompanionCoverageSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("selected", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            return wire;
        }

        private static ScalesCommandTools Commands(ServiceProvider provider)
        {
            return new ScalesCommandTools(provider.GetRequiredService<ScalesClientAccessor>());
        }

        private static QualifiedName ScaleName(CompanionCoverageSession wire, string name)
        {
            return wire.Name(name, Scales.Namespaces.Scales);
        }

        private static void MapMethod(CompanionCoverageSession wire, NodeId owner, string method)
        {
            wire.MapPath(owner, Id("method"), ScaleName(wire, method));
        }

        private static CallMethodRequest AssertCall(CompanionCoverageSession wire, NodeId owner, int argumentCount)
        {
            Assert.That(wire.Calls, Has.Count.EqualTo(1));
            Assert.That(wire.Calls[0].ObjectId, Is.EqualTo(owner));
            Assert.That(wire.Calls[0].MethodId, Is.EqualTo(Id("method")));
            Assert.That(wire.Calls[0].InputArguments.Count, Is.EqualTo(argumentCount));
            Assert.That(wire.Writes, Is.Empty);
            return wire.Calls[0];
        }

        private static void AssertCompleted(JsonObject result, NodeId node)
        {
            Assert.That(result["completed"]!.GetValue<bool>(), Is.True);
            Assert.That(result["nodeId"]!.GetValue<string>(), Is.EqualTo(node.ToString()));
        }

        private static EUInformation Kilograms()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4933453,
                DisplayName = new LocalizedText("en-US", "kg"),
                Description = new LocalizedText("en-US", "kilogram")
            };
        }

        private static void PublishSnapshot(CompanionCoverageSession wire)
        {
            NodeId scale = Id("scale");
            wire.Publish(scale, "manufacturer", Variant.From(new LocalizedText("en-US", "Factory")),
                wire.Name("Identification", Di.Namespaces.OpcUaDi),
                wire.Name("Manufacturer", Di.Namespaces.OpcUaDi));
            wire.Publish(scale, "current",
                Variant.FromStructure(new WeightType { Gross = 7.5, Net = 5, Tare = 2.5 }),
                ScaleName(wire, ScaleNames.CurrentWeight));
            wire.Publish(scale, "stable", Variant.From(false),
                ScaleName(wire, ScaleNames.CurrentWeight), ScaleName(wire, ScaleNames.WeightStable));
            NodeId rangeType = new(
                Scales.ObjectTypes.WeighingRangeElementType, wire.NamespaceIndex(Scales.Namespaces.Scales));
            wire.SetBrowse(scale,
            [
                Reference(Id("range-3"), "Large", typeDefinition: rangeType),
                Reference(Id("range-1"), "Small", typeDefinition: rangeType),
                Reference(Id("range-2"), "Medium", typeDefinition: rangeType)
            ]);
            for (int index = 1; index <= 3; index++)
            {
                NodeId range = Id($"range-{index}");
                wire.Publish(range, $"range-value-{index}",
                    Variant.FromStructure(new Range { Low = 0, High = index * 10 }),
                    ScaleName(wire, ScaleNames.Range));
                wire.Publish(range, $"interval-{index}", Variant.From(0.25),
                    ScaleName(wire, ScaleNames.ActualScaleInterval));
                wire.Publish(range, $"verification-{index}", Variant.From(0.5),
                    ScaleName(wire, ScaleNames.VerificationScaleInterval));
            }
            ArrayOf<ExtensionObject> units =
            [
                new ExtensionObject(new EUInformation { UnitId = 1 }),
                new ExtensionObject(new EUInformation { UnitId = 2 }),
                new ExtensionObject(new EUInformation { UnitId = 3 })
            ];
            wire.Publish(scale, "units", Variant.From(units), ScaleName(wire, ScaleNames.AllowedEngineeringUnits));
            wire.MapPath(scale, Id("products"),
                ScaleName(wire, ScaleNames.ProductionPreset), ScaleName(wire, ScaleNames.Products));
            wire.SetBrowse(Id("products"),
            [
                Reference(Id("product-1"), "One", typeDefinition: Id("vendor-product")),
                Reference(Id("product-2"), "Two", typeDefinition: Id("vendor-product")),
                Reference(Id("product-3"), "Three", typeDefinition: Id("vendor-product"))
            ]);
            for (int index = 1; index <= 3; index++)
            {
                NodeId product = Id($"product-{index}");
                wire.Publish(product, $"product-id-{index}", Variant.From($"P{index}"),
                    ScaleName(wire, ScaleNames.ProductId));
                wire.Publish(product, $"product-name-{index}", Variant.From(new LocalizedText($"Product {index}")),
                    ScaleName(wire, ScaleNames.ProductName));
                wire.Publish(product, $"processing-{index}", Variant.From(index == 1),
                    ScaleName(wire, ScaleNames.ProductMode));
            }
            wire.MapPath(Id("product-2"), Id("actual-lock"), wire.Name("Lock", Di.Namespaces.OpcUaDi));
            ArrayOf<string> currentProducts = ["P1", "P2", "P3"];
            wire.Publish(scale, "current-products", Variant.From(currentProducts),
                ScaleName(wire, ScaleNames.ProductionPreset), ScaleName(wire, ScaleNames.CurrentProducts));
            QualifiedName state = ScaleName(wire, ScaleNames.State);
            wire.MapPath(scale, Id("state"), state);
            wire.Publish(scale, "outer-state",
                Variant.From(new NodeId(PackML.Objects.PackMLBaseStateMachineType_Cleared,
                    wire.NamespaceIndex(PackML.Namespaces.PackML))),
                state, new QualifiedName("CurrentState"), new QualifiedName("Id"));
            wire.Publish(scale, "inner-state",
                Variant.From(new NodeId(PackML.Objects.PackMLExecuteStateMachineType_Execute,
                    wire.NamespaceIndex(PackML.Namespaces.PackML))),
                state, wire.Name("MachineState", PackML.Namespaces.PackML),
                wire.Name("ExecuteState", PackML.Namespaces.PackML),
                new QualifiedName("CurrentState"), new QualifiedName("Id"));
        }

        private static readonly DateTime s_timestamp = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
    }
}
#endif
