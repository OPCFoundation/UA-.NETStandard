// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Locates and loads the bundled <c>.usda</c> example layers that are copied next to the test
    /// assembly. The layers are self-contained so the tests never read from another repository.
    /// </summary>
    internal static class TestAssets
    {
        /// <summary>
        /// Gets the directory containing the copied example layers.
        /// </summary>
        public static string Directory => Path.Combine(AppContext.BaseDirectory, "Assets");

        /// <summary>
        /// Resolves the full path to a named example layer.
        /// </summary>
        public static string PathTo(string name) => Path.Combine(Directory, name);

        /// <summary>
        /// Parses a named example layer into a composed stage (example overlays applied).
        /// </summary>
        public static UsdStage Load(string name) => UsdaReader.ParseFile(PathTo(name));
    }
}
