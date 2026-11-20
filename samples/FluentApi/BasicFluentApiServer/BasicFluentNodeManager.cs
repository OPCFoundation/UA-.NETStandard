/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace BasicFluentApiServer
{
    public sealed class BasicFluentNodeManager : FluentNodeManagerBase
    {
        public const string NamespaceUri = "urn:localhost:OPCFoundation:BasicFluentApiServer";

        private BaseVariableState? lastResult;

        public BasicFluentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, NamespaceUri)
        {
        }

        protected override ValueTask ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            FolderState devices = builder.AddFolder("Devices").Node;

            lastResult = builder
                .AddVariable<int>("LastResult", devices.NodeId)
                .Writable()
                .OnWrite(OnWriteLastResult)
                .AsVariable<int>().Node;

            builder
                .AddMethod("Add", devices.NodeId)
                .OnCall(OnAdd)
                .AddInputArguments(args => args
                    .Add(arg => arg
                        .WithName("a")
                        .WithDataType(DataTypeIds.Int32)
                        .WithValueRank(ValueRanks.Scalar)
                        .WithDescription("The first integer to add."))
                    .Add(arg => arg
                        .WithName("b")
                        .WithDataType(DataTypeIds.Int32)
                        .WithValueRank(ValueRanks.Scalar)
                        .WithDescription("The second integer to add."))
                )
                .AddOutputArguments(arg => arg
                    .WithName("sum")
                    .WithDataType<int>(SystemContext)
                    .WithValueRank(ValueRanks.Scalar)
                    .WithDescription("The sum of the two integers.")
                );

            builder
                .AddMethod("Multiply", devices.NodeId)
                .OnCall(OnMultiply)
                .AddInputArguments(
                    arg => arg
                        .WithName("a")
                        .WithDataType(DataTypeIds.Int32)
                        .WithValueRank(ValueRanks.Scalar)
                        .WithDescription("The first integer to multiply."),
                    arg => arg
                        .WithName("b")
                        .WithDataType(DataTypeIds.Int32)
                        .WithValueRank(ValueRanks.Scalar)
                        .WithDescription("The second integer to multiply.")
                )
                .AddOutputArguments(arg => arg
                    .WithName("product")
                    .WithDataType<int>(SystemContext)
                    .WithValueRank(ValueRanks.Scalar)
                    .WithDescription("The product of the two integers.")
                );

            return default;
        }

        private ServiceResult OnAdd(
            ISystemContext context,
            MethodState method,
            NodeId oid,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count < 2 ||
                !inputArguments[0].TryGetValue(out int a) ||
                !inputArguments[1].TryGetValue(out int b))
            {
                return StatusCodes.BadInvalidArgument;
            }

            int result = a + b;
            outputArguments[0] = result;

            if (lastResult is null)
            {
                return new(StatusCodes.UncertainNotAllNodesAvailable);
            }

            lastResult.Value = result;
            lastResult.Timestamp = DateTime.UtcNow;
            lastResult.StatusCode = StatusCodes.Good;

            lastResult.ClearChangeMasks(context, false);

            return ServiceResult.Good;
        }

        private ServiceResult OnMultiply(
            ISystemContext context,
            MethodState method,
            NodeId oid,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count < 2 ||
                !inputArguments[0].TryGetValue(out int a) ||
                !inputArguments[1].TryGetValue(out int b))
            {
                return StatusCodes.BadInvalidArgument;
            }

            int result = a * b;
            outputArguments[0] = result;

            if (lastResult is null)
            {
                return new(StatusCodes.UncertainNotAllNodesAvailable);
            }

            lastResult.Value = result;
            lastResult.Timestamp = DateTime.UtcNow;
            lastResult.StatusCode = StatusCodes.Good;

            lastResult.ClearChangeMasks(context, false);

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteLastResult(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            try
            {
                if (!value.TryGetValue(out int lastResult) || lastResult == 0)
                {
                    return StatusCodes.BadOutOfRange;
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing LastResult variable.");
            }
        }
    }
}
