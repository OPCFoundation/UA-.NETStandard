// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

internal sealed class CompanionTemporaryPackage : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "ualens typed package " + Guid.NewGuid().ToString("N") + ".bin");

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.SetAttributes(Path, FileAttributes.Normal);
            File.Delete(Path);
        }
    }
}
