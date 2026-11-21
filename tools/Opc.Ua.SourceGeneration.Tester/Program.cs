// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.SourceGeneration.Tester
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Opc.Ua.SourceGeneration Tester");
            Console.WriteLine("==============================");

            string output = Path.Combine(Directory.GetCurrentDirectory(), "generated");
            LocalFileSystem fs = LocalFileSystem.Instance;
            Generators.GenerateStack(StackGenerationType.All, fs, output, new Telemetry());
            Console.WriteLine("Stack generation completed.");

            // Only consider XML files that have a matching CSV — these are
            // ModelDesign files. NodeSet2 XMLs without a CSV are skipped
            // entirely (they cannot be targets and cannot be fed as
            // ModelDesign dependencies).
            List<string> designXmlFiles = [.. Directory.EnumerateFiles(
                    Path.Combine(Directory.GetCurrentDirectory(), "Resources"), "*.xml")
                .Where(f => File.Exists(Path.ChangeExtension(f, "csv")))];

            foreach (string file in designXmlFiles)
            {
                string csvFile = Path.ChangeExtension(file, "csv");

                // Every other design file is supplied as a dependency — the
                // validator contributes their nodes to the resolution table
                // but does not re-validate them, so unrelated / reverse deps
                // (models that import the current target) are tolerated.
                List<string> dependencies = [.. designXmlFiles.Where(f => f != file)];

                Generators.GenerateCode(new DesignFileCollection
                {
                    Targets = [file],
                    Dependencies = dependencies,
                    IdentifierFilePath = csvFile,
                    Options = new DesignFileOptions { GenerateNodeManager = true }
                }, fs, output, new Telemetry());
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)} design build completed.");
            }
        }

        private sealed class Telemetry : TelemetryContextBase
        {
            public Telemetry()
#pragma warning disable CA2000 // Dispose objects before losing scope
                : base(Microsoft.Extensions.Logging.LoggerFactory.Create(p => p.AddConsole()))
#pragma warning restore CA2000 // Dispose objects before losing scope
            {
            }
        }
    }
}
