// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua;

namespace UaLens.Tests;

internal static class WorkflowAssertions
{
    internal static T GetEncodeable<T>(ExtensionObject extension)
        where T : class, IEncodeable
    {
        Assert.That(extension.TryGetValue(out T? value), Is.True,
            $"Expected an extension object containing {typeof(T).Name}.");
        return value ?? throw new AssertionException($"The {typeof(T).Name} payload was null.");
    }
}
