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
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Boiler
{
    /// <summary>
    /// Sibling partial that wires per-node callbacks for the
    /// source-generated <see cref="BoilerNodeManager"/> using the fluent
    /// builder.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to
    /// source generation: the generator emits a sibling partial that
    /// derives from <c>CustomNodeManager2</c>, owns the predefined-node
    /// load, and calls back into <see cref="Configure"/> below. No
    /// MSBuild flag is required; the attribute alone selects the class
    /// and (since this project carries a single design) the single
    /// matching design file.
    /// <c>Configure</c> runs once,
    /// <em>after</em> <c>base.CreateAddressSpace</c> has materialized the
    /// predefined Boiler instance, so all browse paths into the
    /// <c>Boilers/Boiler #1</c> sub-tree are addressable here.
    /// </remarks>
    [NodeManager]
    public partial class BoilerNodeManager
    {
        private long m_drumLevelTicks;
        private long m_pipeFlowTicks;

        partial void Configure(INodeManagerBuilder builder)
        {
            builder
                .Node("Boilers/Boiler #1/Drum1001/LevelIndicator/Output")
                .OnRead(GenerateDrumLevel);

            builder
                .Node("Boilers/Boiler #1/Pipe1001/FlowTransmitter1/Output")
                .OnRead(GeneratePipeFlow);

            builder
                .Node("Boilers/Boiler #1")
                .OnNodeAdded((context, node) =>
                {
                    Server.Telemetry.CreateLogger<BoilerNodeManager>()
                        .LogInformation(
                            "Boiler instance materialized: {NodeId} ({BrowseName})",
                            node.NodeId,
                            node.BrowseName);
                });
        }

        private ServiceResult GenerateDrumLevel(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            // Slow saw-tooth between 40% and 60% to give clients something
            // to plot without needing a background timer in this single
            // file. Each Read advances the wave; suitable for a quickstart.
            long t = Interlocked.Increment(ref m_drumLevelTicks);
            value = new Variant(50.0 + 10.0 * Math.Sin(t * 0.05));
            statusCode = StatusCodes.Good;
            timestamp = DateTimeUtc.Now;
            return ServiceResult.Good;
        }

        private ServiceResult GeneratePipeFlow(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            long t = Interlocked.Increment(ref m_pipeFlowTicks);
            value = new Variant(100.0 + 25.0 * Math.Cos(t * 0.07));
            statusCode = StatusCodes.Good;
            timestamp = DateTimeUtc.Now;
            return ServiceResult.Good;
        }
    }
}
