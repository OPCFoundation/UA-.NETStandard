

using System;
using System.IO;
using System.Reflection;
using SharpFuzz;

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
            foreach (var method in FuzzMethods.FindFuzzMethods(Console.Error, parameterType))
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

