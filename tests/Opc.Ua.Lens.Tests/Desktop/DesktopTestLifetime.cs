// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Tests.Desktop;

namespace UaLens.Tests;

/// <summary>
/// Drains the lazily started desktop dispatcher after the selected desktop fixtures.
/// Ordinary unit selections do not initialize Avalonia.
/// </summary>
[SetUpFixture]
public sealed class DesktopTestLifetime
{
    [OneTimeTearDown]
    public async Task StopDesktopAsync()
    {
        await DesktopApplication.StopAsync().ConfigureAwait(false);
    }
}
