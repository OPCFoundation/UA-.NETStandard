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
using System.IO;

public static class Program
{
    // signatures supported by fuzzers
    public delegate void AflFuzzStream(Stream stream);
    public delegate void AflFuzzString(string text);
    public delegate void LibFuzzSpan(ReadOnlySpan<byte> bytes);

    public static void Main(string[] args)
    {
        string fuzzingFunction = string.Empty;

        FuzzableCode.FuzzInfo();
        Console.WriteLine();

        if (args.Length >= 1)
        {
            var fuzzingMethod = FuzzMethods.FindFuzzMethod(Console.Error, args[0]);

            if (fuzzingMethod != null)
            {
                Console.WriteLine($"Run the fuzzing function: {args[0]}");

                FuzzMethods.RunFuzzMethod(fuzzingMethod);

                return;
            }
        }

        Usage(fuzzingFunction);
    }

    private static void Usage(string fuzzingFunction)
    {
        Type type = typeof(FuzzableCode);
        string applicationName = typeof(Program).Assembly.GetName().Name;
        Console.Error.WriteLine("Usage: {0} [fuzzingFunction]", applicationName);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Available fuzzing functions:");

        foreach (Type parameterType in FuzzMethods.Delegates)
        {
            bool writeHeader = true;
            foreach (var method in FuzzMethods.FindFuzzMethods(parameterType))
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

