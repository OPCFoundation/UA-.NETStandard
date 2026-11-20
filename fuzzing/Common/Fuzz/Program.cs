// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace Opc.Ua.Fuzzing
{
    public static class Program
    {
        /// <summary>
        /// signatures supported by fuzzers
        /// </summary>
        /// <param name="stream"></param>
        public delegate void AflFuzzStream(Stream stream);
        public delegate void AflFuzzString(string text);
        public delegate void LibFuzzSpan(ReadOnlySpan<byte> bytes);

        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--list")
            {
                int count = 0;
                foreach (Type delegateType in FuzzMethods.Delegates)
                {
                    foreach (Delegate method in FuzzMethods.FindFuzzMethods(delegateType))
                    {
                        Console.WriteLine(method.Method.Name);
                        count++;
                    }
                }
                if (count == 0)
                {
                    throw new InvalidOperationException("No supported fuzz targets were discovered.");
                }
                return 0;
            }

            if (args.Length == 3 && args[0] == "--replay")
            {
                Delegate? method = FuzzMethods.FindFuzzMethod(Console.Error, args[1]);
                if (method == null)
                {
                    return 2;
                }
                string inputPath = Path.GetFullPath(args[2]);
                string[] files = Directory.Exists(inputPath)
                    ? Directory.GetFiles(inputPath, "*", SearchOption.AllDirectories)
                    : [inputPath];
                if (files.Length == 0)
                {
                    throw new InvalidOperationException($"Replay corpus is empty: {inputPath}");
                }
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    FuzzMethods.Replay(method, File.ReadAllBytes(file));
                }
                return 0;
            }

            if (args.Length == 1 && args[0] == "--help")
            {
                Usage();
                return 0;
            }

            if (args.Length == 1)
            {
                Delegate? method = FuzzMethods.FindFuzzMethod(Console.Error, args[0]);
                if (method != null)
                {
                    FuzzableCode.FuzzInfo();
                    FuzzMethods.RunFuzzMethod(method);
                    return 0;
                }
            }

            Usage();
            return 2;
        }

        private static void Usage()
        {
            string applicationName = typeof(Program).Assembly.GetName().Name!;
            Console.Error.WriteLine(
                "Usage: {0} <fuzzingFunction> | --list | --replay <target> <file-or-corpus>",
                applicationName);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Available fuzzing functions:");

            foreach (Type parameterType in FuzzMethods.Delegates)
            {
                bool writeHeader = true;
                foreach (Delegate method in FuzzMethods.FindFuzzMethods(parameterType))
                {
                    if (writeHeader)
                    {
                        Console.Error.WriteLine();
                        if (parameterType.Name == nameof(AflFuzzStream))
                        {
                            Console.Error.WriteLine("afl-fuzz Stream signature:");
                        }
                        else if (parameterType.Name == nameof(AflFuzzString))
                        {
                            Console.Error.WriteLine("afl-fuzz string signature:");
                        }
                        else if (parameterType.Name == nameof(LibFuzzSpan))
                        {
                            Console.Error.WriteLine("libfuzzer ReadOnlySpan<byte> signature:");
                        }
                        writeHeader = false;
                    }

                    Console.Error.WriteLine("-- {0}", method.Method.Name);
                }
            }
        }
    }
}
