/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
                : base(Microsoft.Extensions.Logging.LoggerFactory.Create(p => p.AddConsole()))
            {
            }
        }
    }
}
