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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Opc.Ua.ISA95.JobControl.V2;
using Opc.Ua.ISA95.Server.Providers;

namespace MinimalIsa95Server
{
    public sealed class DemoJobSeeder : IHostedService
    {
        public DemoJobSeeder(
            IIsa95JobOrderReceiverV2 receiver,
            IIsa95JobExecutionController execution)
        {
            m_receiver = receiver;
            m_execution = execution;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            const string jobOrderId = "Demo-Job-1";
            await m_receiver.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                new ISA95JobOrderDataType
                {
                    JobOrderID = jobOrderId,
                    Priority = 1
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await m_execution.TransitionAsync(
                jobOrderId,
                Isa95JobExecutionTransition.BeginExecution,
                cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private readonly IIsa95JobOrderReceiverV2 m_receiver;
        private readonly IIsa95JobExecutionController m_execution;
    }
}
