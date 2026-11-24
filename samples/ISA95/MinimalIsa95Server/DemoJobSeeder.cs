// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                Isa95JobExecutionTransition.MarkReady,
                cancellationToken).ConfigureAwait(false);
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
