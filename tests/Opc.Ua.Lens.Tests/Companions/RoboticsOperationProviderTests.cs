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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using R = Opc.Ua.Robotics;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class RoboticsOperationProviderTests
{
    [Test]
    public async Task ProgressCallbackRevocationIsRevalidatedBeforePublishedDispatchAsync()
    {
        var test = new OperationContext("start-program");
        CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
        var progress = new ImmediateRoboticsProgress(
            () => test.Common.Grant = test.Common.Grant with { Revision = "revoked" });
        await Assert.ThatAsync(() => test.Provider.ExecutePreparedAsync(
            test.Common.Context, test.Target, "start-program", prepared, progress, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase("load-program")]
    [TestCase("start-program")]
    [TestCase("stop-program")]
    [TestCase("unload-program")]
    [TestCase("get-ready")]
    [TestCase("start-system")]
    [TestCase("stop-system")]
    [TestCase("stand-down")]
    public async Task PublishedOperationsUseTheirPinnedGeneratedMethodAndDoNotClaimCompletionAsync(string operation)
    {
        var test = new OperationContext(operation);
        test.Services.CallHandler = (call, _) =>
        {
            Assert.That(call.ObjectId, Is.EqualTo(test.Machine));
            Assert.That(call.MethodId, Is.EqualTo(test.Method));
            if (operation == "load-program")
            {
                Assert.That(call.InputArguments, Has.Count.EqualTo(1));
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From("Home")));
            }
            else if (operation.StartsWith("stop-", StringComparison.Ordinal))
            {
                Assert.That(call.InputArguments, Has.Count.EqualTo(1));
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(1L)));
            }
            else
            {
                Assert.That(call.InputArguments, Is.Empty);
            }
            return new CallMethodResult { OutputArguments = [Variant.From(0)] };
        };
        CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
        CompanionOperationResult result = await test.ExecuteAsync(prepared).ConfigureAwait(false);
        Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(0)));
        Assert.That(result.Summary, Does.Contain("accepted").And.Contain("Read fresh state"));
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.Common.VerifyBorrowed();
    }

    [Test]
    public async Task ProgramInspectionPreservesLoadedNameQualityAndMethodAbsenceAsync()
    {
        var test = new OperationContext("load-program");
        NodeId parameters = test.Services.Id("ParameterSet");
        test.Services.AddObject(parameters, Opc.Ua.ObjectTypeIds.BaseObjectType);
        test.Services.AddChild(test.Target.NodeId, parameters, Opc.Ua.Di.Namespaces.OpcUaDi, "ParameterSet");
        test.Services.AddProperty(
            parameters, R.Namespaces.Robotics, R.BrowseNames.TaskProgramLoaded, Variant.From(true));
        NodeId name = test.Services.AddProperty(parameters, R.Namespaces.Robotics, R.BrowseNames.TaskProgramName,
            Variant.From("Home"));
        test.Services.SetValue(name, Variant.From("Home"), status: StatusCodes.UncertainLastUsableValue);
        CompanionInspection inspection = await test.Provider.InspectAsync(
            test.Common.Context, test.Target, CancellationToken.None).ConfigureAwait(false);
        CompanionValue program = inspection.Values.ToList().Single(value => value.Name == "Program name");
        Assert.That(program.Value, Is.EqualTo(Variant.From("Home")));
        Assert.That(program.Quality, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
        Assert.That(program.SourceNodeId, Is.EqualTo(name));
        Assert.That(inspection.Values.ToList().Single(value => value.Name == "Program loaded").Value,
            Is.EqualTo(Variant.From(true)));
        Assert.That(inspection.Operations.ToList().Select(value => value.Id),
            Is.EquivalentTo(s_programOperations));
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase("unknown-state")]
    [TestCase("bad-quality")]
    [TestCase("changed-state")]
    [TestCase("wrong-target")]
    [TestCase("machine-type")]
    public async Task PublishedStateOrTargetChangesCannotIssueACommandAsync(string change)
    {
        var test = new OperationContext("start-program");
        CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
        CompanionTarget target = test.Target;
        if (change == "wrong-target")
        {
            target = test.Target with { NodeId = test.Common.Controller };
        }
        else if (change == "machine-type")
        {
            test.Services.AddObject(test.Machine, Opc.Ua.ObjectTypeIds.BaseObjectType);
        }
        else
        {
            test.Services.SetValue(test.StateId,
                Variant.From(change == "unknown-state" ? test.Services.Id("unsupported-state") :
                    test.Services.Resolve(R.TaskControlStateMachineTypeIds.StateIds.Executing)),
                status: change == "bad-quality" ? StatusCodes.Uncertain : StatusCodes.Good);
        }
        await Assert.ThatAsync(() => test.Provider.ExecutePreparedAsync(test.Common.Context, target, "start-program",
            prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf(change == "wrong-target" ? typeof(ArgumentException) : typeof(ServiceResultException)))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(42)]
    [TestCase(-1)]
    public async Task PublishedNonzeroStatusIsNotSuccessfulCompletionAsync(int status)
    {
        var test = new OperationContext("load-program");
        test.Services.CallHandler = (_, _) => new CallMethodResult { OutputArguments = [Variant.From(status)] };
        CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared).ConfigureAwait(false);
        Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(status)));
        Assert.That(result.Summary, Does.Contain("not a successful completion"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PublishedMissingStatusIsUnknownAndCannotReplayAsync()
    {
        var test = new OperationContext("start-program");
        test.Services.CallHandler = (_, _) => new CallMethodResult();
        CompanionTaskInput prepared = await test.PrepareAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadDecodingError))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
    }

    private static readonly string[] s_programOperations = ["snapshot", "load-program"];

    /// <summary>
    /// Builds only published generated operation objects, states and method metadata.
    /// </summary>
    private sealed class OperationContext
    {
        public OperationContext(string operation)
        {
            Operation = operation;
            bool system = operation is "get-ready" or "start-system" or "stop-system" or "stand-down";
            NodeId target = Services.Id("published-target");
            Target = new CompanionTarget("robotics", target, "Owned published simulation",
                system ? "SystemOperation" : "TaskControl");
            Services.AddObject(target, system ? R.ObjectTypeIds.SystemOperationType : R.ObjectTypeIds.TaskControlType);
            NodeId parent = target;
            if (!system)
            {
                parent = Services.Id("task-operation");
                Services.AddObject(parent, R.ObjectTypeIds.TaskControlOperationType);
                Services.AddChild(target, parent, R.Namespaces.Robotics, "TaskControlOperation");
            }
            Machine = Services.Id("state-machine");
            Services.AddObject(Machine, system ? R.ObjectTypeIds.SystemOperationStateMachineType :
                R.ObjectTypeIds.TaskControlStateMachineType);
            Services.AddChild(parent, Machine, R.Namespaces.Robotics,
                system ? "SystemOperationStateMachine" : "TaskControlStateMachine");
            NodeId state = Services.Id("current-state");
            Services.AddObject(state, Opc.Ua.ObjectTypeIds.BaseObjectType);
            Services.AddChild(Machine, state, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.CurrentState);
            ExpandedNodeId required = operation switch
            {
                "get-ready" => R.SystemOperationStateMachineTypeIds.StateIds.Idle,
                "start-system" or "stand-down" => R.SystemOperationStateMachineTypeIds.StateIds.Ready,
                "stop-system" => R.SystemOperationStateMachineTypeIds.StateIds.Executing,
                "load-program" => R.TaskControlStateMachineTypeIds.StateIds.Idle,
                "stop-program" => R.TaskControlStateMachineTypeIds.StateIds.Executing,
                _ => R.TaskControlStateMachineTypeIds.StateIds.Ready
            };
            Services.SetValue(state, Variant.From(new LocalizedText(operation == "load-program" ? "Idle" : "Ready")));
            StateId = Services.AddProperty(state, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.Id,
                Variant.From(Services.Resolve(required)));
            string method = operation switch
            {
                "get-ready" => "GetReady",
                "stand-down" => "StandDown",
                "load-program" => "LoadByName",
                "unload-program" => "UnloadProgram",
                "stop-system" or "stop-program" => "Stop",
                _ => "Start"
            };
            Method = Common.AddMethod(Machine, R.Namespaces.Robotics, method,
                operation == "load-program" ? [Opc.Ua.DataTypeIds.String] :
                method == "Stop" ? [Opc.Ua.DataTypeIds.Int64] : [], [Opc.Ua.DataTypeIds.Int32]);
            Inputs = operation == "load-program" ? [new("program", Variant.From("Home"))] :
                method == "Stop" ? [new("stopMode", Variant.From(1L))] : [];
            Provider = new RoboticsCompanionProvider(Common.Policy.Object, Common.Clock);
        }

        public RoboticsTaskTestContext Common { get; } = new();
        public IndustrialCompanionTestSession Services => Common.Services;
        public RoboticsCompanionProvider Provider { get; }
        public CompanionTarget Target { get; }
        public string Operation { get; }
        public NodeId Machine { get; }
        public NodeId StateId { get; }
        public NodeId Method { get; }
        public ArrayOf<CompanionValue> Inputs { get; }

        public ValueTask<CompanionTaskInput> PrepareAsync()
        {
            return Provider.PrepareInputAsync(Common.Context, Target, Operation, Inputs, CancellationToken.None);
        }

        public Task<CompanionOperationResult> ExecuteAsync(CompanionTaskInput input)
        {
            return Provider.ExecutePreparedAsync(Common.Context, Target, Operation, input, null, CancellationToken.None)
                .AsTask();
        }
    }
}
