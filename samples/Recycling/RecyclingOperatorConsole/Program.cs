/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Starts the independently owned local workstation host.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs without joining or renewing process membership.
    /// </summary>
    private static async Task Main(string[] args)
    {
        WebApplication application = OperatorConsoleHost.Build(args);
        await using var applicationScope = application.ConfigureAwait(false);
        await application.RunAsync().ConfigureAwait(false);
    }
}
