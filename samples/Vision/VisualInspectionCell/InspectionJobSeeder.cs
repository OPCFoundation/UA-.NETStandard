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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vision.VisualInspectionCell
{
    internal sealed partial class InspectionJobSeeder : IHostedService
    {
        public InspectionJobSeeder(
            InspectionJobControlProvider provider,
            ILogger<InspectionJobSeeder> logger)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_provider.SeedAsync(cancellationToken).ConfigureAwait(false);
            m_logger.JobsSeeded(InspectionJobControlProvider.InspectionOrderId,
                InspectionJobControlProvider.ReworkRejectOrderId);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private readonly InspectionJobControlProvider m_provider;
        private readonly ILogger<InspectionJobSeeder> m_logger;
    }

    internal static partial class InspectionJobSeederLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Isa95 + 1,
            Level = LogLevel.Information,
            Message = "Seeded ISA-95 V2 orders {InspectionOrderId} and {ReworkRejectOrderId}.")]
        public static partial void JobsSeeded(
            this ILogger<InspectionJobSeeder> logger,
            string inspectionOrderId,
            string reworkRejectOrderId);
    }
}
