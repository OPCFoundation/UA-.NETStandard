/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Wot.Exporter
{
    /// <summary>
    /// Runs the installed export command.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Exports models and returns the process exit code.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            using var cancellation = new CancellationTokenSource();
            void handler(object? _, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cancellation.Cancel();
            }
            Console.CancelKeyPress += handler;
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IWotUnitMappingProvider, WotQudtUnitMappingProvider>();
                services.AddSingleton(sp =>
                    new ExportCommand(Console.Out, Console.Error, sp.GetRequiredService<IWotUnitMappingProvider>()));
                using ServiceProvider provider = services.BuildServiceProvider();
                return await provider.GetRequiredService<ExportCommand>()
                    .RunAsync(args, cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }
    }
}
