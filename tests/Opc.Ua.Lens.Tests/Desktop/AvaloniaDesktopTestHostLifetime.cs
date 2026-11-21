// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Tests.Desktop;

namespace UaLens.Tests;

[SetUpFixture]
public sealed class AvaloniaDesktopTestHostLifetime
{
    [OneTimeTearDown]
    public Task StopDesktopAsync()
    {
        return AvaloniaDesktopTestHost.StopAsync();
    }
}
