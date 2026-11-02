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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Verifies that the instances created by the runtime expose the mandatory
    /// children and type definitions required by the LADS NodeSet.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsAddressSpaceTests
    {
        [Test]
        public async Task DeviceExposesTheMandatoryChildrenAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceExposesTheMandatoryChildrenAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            await AssertChildrenAsync(server, device.NodeId, s_deviceChildren).ConfigureAwait(false);
            await AssertTypeAsync(server, device.NodeId, "FunctionalUnitSet", ObjectTypeIds.FunctionalUnitSetType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, device.NodeId, "DeviceState", ObjectTypeIds.LADSDeviceStateMachineType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, device.NodeId, "Maintenance", ObjectTypeIds.MaintenanceSetType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, device.NodeId, "Components", ObjectTypeIds.LADSComponentsType)
                .ConfigureAwait(false);
            NodeId deviceState = await server.ChildAsync(device.NodeId, "DeviceState").ConfigureAwait(false);
            await AssertChildrenAsync(server, deviceState, "CurrentState").ConfigureAwait(false);

            NodeId components = await server.ChildAsync(device.NodeId, "Components").ConfigureAwait(false);
            await AssertTypeAsync(server, components, "Shaker", ObjectTypeIds.LADSComponentType)
                .ConfigureAwait(false);
            NodeId shaker = await server.ChildAsync(components, "Shaker").ConfigureAwait(false);
            await AssertChildrenAsync(server, shaker, s_componentChildren).ConfigureAwait(false);

            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            await AssertChildrenAsync(server, task.NodeId, "MaintenanceState").ConfigureAwait(false);
        }

        [Test]
        public async Task FunctionalUnitAndFunctionsExposeTheMandatoryChildrenAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitAndFunctionsExposeTheMandatoryChildrenAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            NodeId units = await server.ChildAsync(device.NodeId, "FunctionalUnitSet").ConfigureAwait(false);
            await AssertTypeAsync(server, units, LadsIncubator.UnitName, ObjectTypeIds.FunctionalUnitType)
                .ConfigureAwait(false);

            await AssertChildrenAsync(server, unit.NodeId, s_unitChildren).ConfigureAwait(false);
            NodeId unitState = await server.ChildAsync(unit.NodeId, "FunctionalUnitState").ConfigureAwait(false);
            await AssertChildrenAsync(server, unitState, "CurrentState").ConfigureAwait(false);

            NodeId functions = await server.ChildAsync(unit.NodeId, "FunctionSet").ConfigureAwait(false);
            await AssertTypeAsync(server, functions, "Temperature", ObjectTypeIds.AnalogControlFunctionType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, functions, "CO2", ObjectTypeIds.AnalogScalarSensorFunctionType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, functions, "Door", ObjectTypeIds.CoverFunctionType)
                .ConfigureAwait(false);
            await AssertTypeAsync(server, functions, "Timer", ObjectTypeIds.TimerControlFunctionType)
                .ConfigureAwait(false);

            NodeId temperature = await server.ChildAsync(functions, "Temperature").ConfigureAwait(false);
            await AssertChildrenAsync(server, temperature, s_analogControlChildren).ConfigureAwait(false);
            NodeId controlState = await server.ChildAsync(temperature, "ControlFunctionState")
                .ConfigureAwait(false);
            await AssertChildrenAsync(server, controlState, "CurrentState").ConfigureAwait(false);
            NodeId co2 = await server.ChildAsync(functions, "CO2").ConfigureAwait(false);
            await AssertChildrenAsync(server, co2, s_sensorChildren).ConfigureAwait(false);
            NodeId door = await server.ChildAsync(functions, "Door").ConfigureAwait(false);
            await AssertChildrenAsync(server, door, "IsEnabled", "Operational", "CoverState").ConfigureAwait(false);
            NodeId timer = await server.ChildAsync(functions, "Timer").ConfigureAwait(false);
            await AssertChildrenAsync(server, timer, "IsEnabled", "Operational").ConfigureAwait(false);
        }

        [Test]
        public async Task ProgramManagerTemplatesAndResultsExposeTheMandatoryChildrenAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ProgramManagerTemplatesAndResultsExposeTheMandatoryChildrenAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            await AssertTypeAsync(server, unit.NodeId, "ProgramManager", ObjectTypeIds.ProgramManagerType)
                .ConfigureAwait(false);
            NodeId manager = await server.ChildAsync(unit.NodeId, "ProgramManager").ConfigureAwait(false);
            await AssertChildrenAsync(server, manager, s_managerChildren).ConfigureAwait(false);

            NodeId templates = await server.ChildAsync(manager, "ProgramTemplateSet").ConfigureAwait(false);
            await AssertChildrenAsync(server, templates, "NodeVersion").ConfigureAwait(false);
            await AssertTypeAsync(server, templates, LadsIncubator.QuickTemplate, ObjectTypeIds.ProgramTemplateType)
                .ConfigureAwait(false);
            NodeId quick = await server.ChildAsync(templates, LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await AssertChildrenAsync(server, quick, s_templateChildren).ConfigureAwait(false);

            string runId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            NodeId results = await server.ChildAsync(manager, "ResultSet").ConfigureAwait(false);
            await AssertTypeAsync(server, results, result.Name, ObjectTypeIds.ResultType).ConfigureAwait(false);
            await AssertChildrenAsync(server, result.NodeId, s_resultChildren).ConfigureAwait(false);

            NodeId files = await server.ChildAsync(result.NodeId, "FileSet").ConfigureAwait(false);
            await AssertTypeAsync(server, files, "Protocol.csv", ObjectTypeIds.ResultFileType).ConfigureAwait(false);
            NodeId file = await server.ChildAsync(files, "Protocol.csv").ConfigureAwait(false);
            await AssertChildrenAsync(server, file, "MimeType", "Name", "File").ConfigureAwait(false);
            NodeId variables = await server.ChildAsync(result.NodeId, "VariableSet").ConfigureAwait(false);
            await AssertChildrenAsync(server, variables, "Steps").ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that hierarchical instance references do not lead to type declarations.
        /// </summary>
        [Test]
        public async Task InstanceTreeDoesNotReferenceTypeDeclarationsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(InstanceTreeDoesNotReferenceTypeDeclarationsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            string runId = await unit.Programs!.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await unit.Programs.WaitForResultAsync(runId).ConfigureAwait(false);

            ushort instances = device.NodeId.NamespaceIndex;
            var visited = new HashSet<NodeId> { device.NodeId };
            var pending = new Queue<NodeId>(visited);
            var leaks = new List<string>();
            while (pending.Count > 0)
            {
                NodeId current = pending.Dequeue();
                foreach (ReferenceDescription reference in await server.BrowseAsync(current).ConfigureAwait(false))
                {
                    NodeId target = server.ToNodeId(reference.NodeId);
                    if (target.NamespaceIndex != instances)
                    {
                        leaks.Add($"{current} -> {target} ({reference.BrowseName})");
                    }
                    else if (visited.Add(target))
                    {
                        pending.Enqueue(target);
                    }
                }
            }

            Assert.That(visited, Has.Count.GreaterThan(100));
            Assert.That(leaks, Is.Empty, string.Join("\n", leaks));
        }

        /// <summary>
        /// Verifies that Operational organizes the registered state-machine members and values.
        /// </summary>
        [TestCase("Temperature", "ControlFunctionState", "CurrentValue", "TargetValue")]
        [TestCase("Timer", "ControlFunctionState", "CurrentValue", "TargetValue")]
        [TestCase("Door", "CoverState")]
        public async Task OperationalOrganizesTheStateMachineAndItsMethodsAsync(
            string function,
            string stateMachine,
            params string[] variables)
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(OperationalOrganizesTheStateMachineAndItsMethodsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            NodeId node = await server.ChildAsync(unit.NodeId, "FunctionSet", function).ConfigureAwait(false);
            NodeId operational = await server.ChildAsync(node, "Operational").ConfigureAwait(false);
            NodeId machine = await server.ChildAsync(node, stateMachine).ConfigureAwait(false);

            var organized = (await server.BrowseAsync(operational).ConfigureAwait(false))
                .Select(reference => server.ToNodeId(reference.NodeId))
                .ToHashSet();
            var expected = new List<NodeId>
            {
                await server.ChildAsync(machine, "CurrentState").ConfigureAwait(false)
            };
            foreach (ReferenceDescription reference in await server.BrowseAsync(machine).ConfigureAwait(false))
            {
                if (reference.NodeClass == NodeClass.Method)
                {
                    expected.Add(server.ToNodeId(reference.NodeId));
                }
            }
            foreach (string variable in variables)
            {
                expected.Add(await server.ChildAsync(node, variable).ConfigureAwait(false));
            }

            Assert.That(expected, Has.Count.GreaterThan(variables.Length + 3));
            Assert.That(organized, Is.SupersetOf(expected));
            foreach (ReferenceDescription reference in await server.BrowseAsync(operational).ConfigureAwait(false))
            {
                if (reference.NodeClass == NodeClass.Method)
                {
                    Assert.That(expected, Does.Contain(server.ToNodeId(reference.NodeId)), reference.BrowseName.Name);
                }
            }
        }

        /// <summary>
        /// Preserves the Operational members declared by specialized controls and sensors.
        /// </summary>
        [TestCase("Totalizer", "TotalizedValue", "ResetTotalizer")]
        [TestCase("Relative", "IncreaseRate", "DecreaseRate", "ModifyTargetValueBy")]
        [TestCase("Sensor", "SensorValue", "RawValue")]
        public async Task OperationalPreservesTypeSpecificMembersAsync(string function, params string[] members)
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(OperationalPreservesTypeSpecificMembersAsync),
                builder => builder.AddLads().ConfigureLads(context => context.AddDevice(
                    "Device",
                    device => device.FunctionalUnit("Unit", unit => unit
                        .AnalogControl("Totalizer", options => options.Kind = LadsAnalogControlKind.Totalizer)
                        .AnalogControl("Relative", options =>
                        {
                            options.Kind = LadsAnalogControlKind.Relative;
                            options.IncreaseRate = 1;
                            options.DecreaseRate = 1;
                        })
                        .AnalogScalarSensor("Sensor")))),
                "Device").ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync("Device").ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit("Unit");
            NodeId node = await server.ChildAsync(unit.NodeId, "FunctionSet", function).ConfigureAwait(false);
            NodeId operational = await server.ChildAsync(node, "Operational").ConfigureAwait(false);
            var organized = (await server.BrowseAsync(operational, ReferenceTypeIds.Organizes).ConfigureAwait(false))
                .Select(reference => server.ToNodeId(reference.NodeId))
                .ToHashSet();

            foreach (string member in members)
            {
                NodeId expected = await server.ChildAsync(node, member).ConfigureAwait(false);
                Assert.That(organized, Does.Contain(expected), $"{function}/{member}");
                var inverse = (await server.BrowseAsync(
                    expected, ReferenceTypeIds.Organizes, BrowseDirection.Inverse).ConfigureAwait(false))
                    .Select(reference => server.ToNodeId(reference.NodeId));
                Assert.That(inverse, Does.Contain(operational), $"{function}/{member}");
            }
        }

        private static async Task AssertChildrenAsync(
            LadsHostedServer server,
            NodeId nodeId,
            params string[] expected)
        {
            string[] names = await server.ChildNamesAsync(nodeId).ConfigureAwait(false);
            List<string> missing = [.. expected.Where(name => !names.Contains(name))];
            Assert.That(missing, Is.Empty, $"{nodeId} is missing mandatory children.");
        }

        private static async Task AssertTypeAsync(
            LadsHostedServer server,
            NodeId parent,
            string name,
            ExpandedNodeId expectedType)
        {
            IReadOnlyList<ReferenceDescription> references = await server.BrowseAsync(parent).ConfigureAwait(false);
            ReferenceDescription? child = references.FirstOrDefault(r => r.BrowseName.Name == name);
            Assert.That(child, Is.Not.Null, $"{parent}/{name}");
            Assert.That(
                server.ToNodeId(child!.TypeDefinition),
                Is.EqualTo(server.ToNodeId(expectedType)),
                $"{parent}/{name}");
        }

        private static readonly string[] s_deviceChildren =
        [
            "FunctionalUnitSet", "Identification", "DeviceState", "RevisionCounter", "Manufacturer", "Model",
            "DeviceManual", "DeviceRevision", "SoftwareRevision", "SerialNumber", "AssetId", "ComponentName",
            "HardwareRevision", "ProductInstanceUri"
        ];

        private static readonly string[] s_componentChildren = ["Identification", "Manufacturer", "SerialNumber"];

        private static readonly string[] s_unitChildren = ["Lock", "FunctionalUnitState", "FunctionSet"];

        private static readonly string[] s_analogControlChildren =
        [
            "IsEnabled", "Operational", "CurrentValue", "TargetValue", "ControlFunctionState"
        ];

        private static readonly string[] s_sensorChildren = ["IsEnabled", "Operational", "SensorValue", "RawValue"];

        private static readonly string[] s_managerChildren = ["ActiveProgram", "ProgramTemplateSet", "ResultSet"];

        private static readonly string[] s_templateChildren =
        [
            "Author", "Created", "Description", "Modified", "DeviceTemplateId", "Version"
        ];

        private static readonly string[] s_resultChildren =
        [
            "ApplicationUri", "Description", "FileSet", "SupervisoryJobId", "Samples", "Started", "Stopped",
            "ProgramTemplate", "User", "VariableSet", "Properties", "SupervisoryTaskId"
        ];
    }
}
