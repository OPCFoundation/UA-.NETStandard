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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises explicit tool observations through the public builder and an actual opc.tcp session.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolEquipmentTests
    {
        /// <summary>
        /// Tool identification, the standard BaseObject location and Number-valued life retain wire fidelity.
        /// </summary>
        [Test]
        public async Task ToolInventoryPreservesIdentifiersLocationAndIntegralLifeOverTcpAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = Example();
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Tools")
                .WithProvider(provider)
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Equipment test");
                    identity.SerialNumber = "Tools-1";
                    identity.ProductInstanceUri = "urn:equipment-tests:tools";
                }).BuildAsync().ConfigureAwait(false);
            ToolListState list = runtime.Equipment.State.Tools!;
            var tool = (ToolState)list.FindChild(fixture.Context.Context,
                new QualifiedName("T1", fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(tool, Is.Not.Null);
            Assert.That(tool.Identifier!.Value, Is.EqualTo("GLOBAL-123"));
            Assert.That(tool.Location!.TypeDefinitionId, Is.EqualTo(Opc.Ua.ObjectTypeIds.BaseObjectType));
            ushort mt = (ushort)fixture.Context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool);
            var place = (BaseVariableState)tool.Location.FindChild(
                fixture.Context.Context, new QualifiedName(BrowseNames.PlaceNumber, mt))!;
            Assert.That(place.WrappedValue.TryGetValue(out ushort position), Is.True);
            Assert.That(position, Is.EqualTo(65535));
            Assert.That(tool.ControlIdentifier2, Is.Null);
            Assert.That(tool.LastUsage, Is.Null);
            Assert.That(tool.PlannedForOperating, Is.Null);
            var life = (ToolLifeState)tool.ToolLife!.FindChild(fixture.Context.Context,
                new QualifiedName("Parts", fixture.Context.InstanceNamespaceIndex))!;
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "EquipmentClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(life.NodeId).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out ulong count), Is.True);
            Assert.That(count, Is.EqualTo(18_446_744_073_709_551_610ul));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(value.SourceTimestamp, Is.EqualTo(initial.Equipment!.Tools[0].Life[0].Value.SourceTimestamp));
            Assert.That(life.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Number));
            Assert.That(life.Indication!.Value, Is.EqualTo(ToolLifeIndication.NumberOfParts));
            Assert.That(life.IsCountingUp!.Value, Is.True);
            Assert.That(life.EngineeringUnits!.BrowseName, Is.EqualTo(new QualifiedName("EngineeringUnits", mt)));
            Assert.That(life.WarningValue!.WrappedValue.TryGetValue(out ulong warning), Is.True);
            Assert.That(warning, Is.EqualTo(18_446_744_073_709_551_611ul));
            WriteResponse write = await session.WriteAsync(null,
                [new WriteValue { NodeId = life.NodeId, AttributeId = Attributes.Value, Value = new DataValue(1ul) }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(write.Results[0], Is.EqualTo(StatusCodes.BadNotWritable));
            await using var facade = new MachineToolClient(session, NUnitTelemetryContext.Create());
            MachineToolEquipmentReadSnapshot inventory = await facade.ReadEquipmentAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(inventory.Tools.Count, Is.EqualTo(1));
            Assert.That(inventory.Tools[0].NodeId, Is.EqualTo(tool.NodeId));
            Assert.That(inventory.Tools[0].LocationNodeId, Is.EqualTo(tool.Location.NodeId));
            Assert.That(inventory.Tools[0].ControlIdentifier2.IsNull, Is.True);
            Assert.That(inventory.Tools[0].LastUsage.IsNull, Is.True);
            Assert.That(inventory.Tools[0].Life[0].Value.WrappedValue, Is.EqualTo(value.WrappedValue));
            Assert.That(inventory.Tools[0].Life[0].Value.SourceTimestamp, Is.EqualTo(value.SourceTimestamp));
            Assert.That(inventory.Tools[0].Life[0].Value.StatusCode, Is.EqualTo(value.StatusCode));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Supplies independently known UInt64 values above Double's integral precision.
        /// </summary>
        internal static MachineToolSnapshot Example()
        {
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 9, 1, 2, 3, 4, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            return new MachineToolSnapshot
            {
                Equipment = new MachineToolEquipmentObservation
                {
                    Tools =
                    [
                        new MachineToolTool
                        {
                            Key = "T1",
                            Identifier = "GLOBAL-123",
                            Name = "Drill",
                            Location = new MachineToolLocation("Magazine", ushort.MaxValue),
                            Control = new MachineToolToolControl(7, ToolManagement.NumberBased, false,
                                ToolLocked.CapabilityUnavailable),
                            Sample = sample,
                            Life =
                            [
                                new MachineToolToolLife
                                {
                                    Name = "Parts",
                                    Value = Sample(Variant.From(18_446_744_073_709_551_610ul), sample),
                                    StartValue = Sample(Variant.From(0ul), sample),
                                    WarningValue = Sample(Variant.From(18_446_744_073_709_551_611ul), sample),
                                    LimitValue = Sample(Variant.From(ulong.MaxValue), sample),
                                    Indication = ToolLifeIndication.NumberOfParts,
                                    EngineeringUnits = Unit(0x433632, "1", "one"),
                                    IsCountingUp = true,
                                    Sample = sample
                                }
                            ]
                        }
                    ]
                }
            };
        }

        /// <summary>
        /// Creates an explicit sample without converting its numerical representation.
        /// </summary>
        internal static DataValue Sample(Variant value, MachineToolSampleInfo sample)
        {
            return new DataValue(value, sample.StatusCode, sample.SourceTimestamp);
        }

        /// <summary>
        /// Supplies the UNECE unit identity independently of the runtime's validation.
        /// </summary>
        internal static EUInformation Unit(int code, string symbol, string description)
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = code,
                DisplayName = new LocalizedText(symbol),
                Description = new LocalizedText(description)
            };
        }
    }
}
