// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace UaLens.Tests.Companions;

internal static class OpenUsdTestPaths
{
    public static string NewDestination()
    {
        return Path.Combine(
            ResolveDirectory(new DirectoryInfo(Environment.CurrentDirectory)), $"ualens-export-{Guid.NewGuid():N}");
    }

    private static string ResolveDirectory(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            if (directory.ResolveLinkTarget(returnFinalTarget: true) is not DirectoryInfo target)
            {
                throw new IOException("The test working directory link does not resolve to a directory.");
            }
            return ResolveDirectory(target);
        }

        return directory.Parent is { } parent
            ? Path.Combine(ResolveDirectory(parent), directory.Name)
            : directory.FullName;
    }
}
