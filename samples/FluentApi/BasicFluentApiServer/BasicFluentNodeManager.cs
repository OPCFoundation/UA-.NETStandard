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
using System.Collections.Generic;
using System.Text;
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

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences, CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex);

            Configure(builder);

            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);

            await CompleteConfigureAsync(externalReferences, cancellationToken).ConfigureAwait(false);
        }

        private void Configure(NodeManagerBuilder builder)
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
        }

        private ServiceResult OnAdd(
            ISystemContext context,
            MethodState method,
            NodeId oid,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputArguments[0].TryGetValue(out int a))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputArguments[1].TryGetValue(out int b))
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

        private ServiceResult OnWriteLastResult(ISystemContext context, NodeState node, ref Variant value)
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
