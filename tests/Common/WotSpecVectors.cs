// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Opc.Ua.SpecTraceability
{
    internal static class WotSpecVectors
    {
        internal static List<JsonElement> Cases(string group)
        {
            var assembly = typeof(WotSpecVectors).Assembly;
            string resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith("wot-binding-vectors.json", StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The specification vectors are not embedded.");
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement cases = document.RootElement.GetProperty("groups").GetProperty(group).GetProperty("cases");
            if (cases.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"The specification vector group '{group}' is empty.");
            }
            return cases.EnumerateArray().Select(value => value.Clone()).ToList();
        }
    }
}
