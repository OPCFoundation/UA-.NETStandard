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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Hosting registration for the strong distributed historian.
    /// </summary>
    public static class DistributedHistorianBuilderExtensions
    {
        /// <summary>
        /// Registers a protected, leader-write shared historian.
        /// </summary>
        /// <remarks>
        /// The call deliberately provides no process-local store, unprotected
        /// record, or static-leader fallback. The application must register a
        /// cross-process linearizable <see cref="ISharedKeyValueStore"/>, an
        /// <see cref="IRecordProtector"/>, and an <see cref="ILeaderElection"/>.
        /// An explicitly registered <see cref="IHistorianProvider"/> remains
        /// authoritative and is not replaced.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedHistorian(
            this IOpcUaServerBuilder builder,
            Action<SharedKeyValueHistorianOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new SharedKeyValueHistorianOptions();
            configure?.Invoke(options);
            options.Validate();

            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton<IHistorianFencingAuthority>(
                services => new SharedKeyValueHistorianFencingAuthority(
                    services.GetRequiredService<ISharedKeyValueStore>(),
                    services.GetRequiredService<IRecordProtector>(),
                    services.GetRequiredService<ILeaderElection>(),
                    services.GetRequiredService<SharedKeyValueHistorianOptions>()
                        .WriterFenceLeaseDuration,
                    services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton(
                services => new SharedKeyValueHistorianProvider(
                    services.GetRequiredService<ISharedKeyValueStore>(),
                    services.GetRequiredService<IRecordProtector>(),
                    services.GetRequiredService<ILeaderElection>(),
                    services.GetRequiredService<SharedKeyValueHistorianOptions>(),
                    services.GetService<TimeProvider>(),
                    services.GetRequiredService<
                        IHistorianFencingAuthority>()));
            builder.Services.TryAddSingleton<IHistorianProvider>(
                services => services.GetRequiredService<
                    SharedKeyValueHistorianProvider>());
            builder.Services.TryAddSingleton(
                    services =>
                        new SharedKeyValueHistoryContinuationStore(
                            services.GetRequiredService<
                                ISharedKeyValueStore>(),
                            services.GetRequiredService<
                                IRecordProtector>(),
                            maxPayloadBytes: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationMaxPayloadBytes,
                            maxEnvelopesPerSession: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationMaxEnvelopesPerSession,
                            retentionTime: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationRetentionTime,
                            timeProvider: services.GetService<
                                TimeProvider>(),
                            logger: services.GetService<ILogger<
                                SharedKeyValueHistoryContinuationStore>>()));
            builder.Services.TryAddSingleton<
                IHistoryContinuationPointStore>(
                    services => services.GetRequiredService<
                        SharedKeyValueHistoryContinuationStore>());
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStrongKeyspaceProvider>(
                    DistributedHistorianStrongKeyspaceProvider.Instance));
            builder.Services.TryAddSingleton<DistributedHistorianStartupTask>();
            builder.Services.AddSingleton<IServerStartupTask>(
                services => services.GetRequiredService<
                    DistributedHistorianStartupTask>());
            return builder;
        }
    }

    internal sealed class DistributedHistorianStrongKeyspaceProvider :
        IStrongKeyspaceProvider
    {
        public static DistributedHistorianStrongKeyspaceProvider Instance { get; }
            = new();

        public ArrayOf<string> GetStrongKeyPrefixes()
        {
            return
            [
                "historian/v1/",
                "history-continuation/v1/"
            ];
        }
    }
}
