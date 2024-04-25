

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

        if (args.Length == 1)
        {
            // find the function to fuzz based on the first argument using reflection
            Type type = typeof(FuzzableCode);
            fuzzingFunction = args[0];
            MethodInfo method = type.GetMethod(args[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (method != null)
            {
                Console.WriteLine($"Found the fuzzing function: {args[0]}");

                // call the fuzzer target if there is a matching signature
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1)
                {
                    // afl-fuzz targets
                    if (parameters[0].ParameterType == typeof(Stream))
                    {
                        var fuzzMethod = (AflFuzzStream)method.CreateDelegate(typeof(AflFuzzStream));
                        Fuzzer.Run(stream => fuzzMethod(stream));
                        return;
                    }
                    else if (parameters[0].ParameterType == typeof(string))
                    {
                        var fuzzMethod = (AflFuzzString)method.CreateDelegate(typeof(AflFuzzString));
                        Fuzzer.Run(text => fuzzMethod(text));
                        return;
                    }
                    // libfuzzer span target
                    else if (parameters[0].ParameterType == typeof(ReadOnlySpan<byte>))
                    {
                        var fuzzMethod = (LibFuzzSpan)method.CreateDelegate(typeof(LibFuzzSpan));
                        Fuzzer.LibFuzzer.Run(bytes => fuzzMethod(bytes));
                        return;
                    }
                }

                Console.Error.WriteLine("The fuzzing function {0} does not have the correct signature {1}.", fuzzingFunction, parameters[0].ParameterType);
            }
            else
            {
                Console.Error.WriteLine("The fuzzing function {0} was not found.", fuzzingFunction);
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

        foreach (var parameterType in new Type[] { typeof(Stream), typeof(string), typeof(ReadOnlySpan<byte>) })
        {
            bool writeHeader = true;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                ParameterInfo[] parameters = m.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == parameterType)
                {
                    if (writeHeader)
                    {
                        Console.Error.WriteLine();
                        if (parameterType == typeof(Stream))
                        {
                            Console.Error.WriteLine("afl-fuzz Stream signature:");
                        }
                        else if (parameterType == typeof(string))
                        {
                            Console.Error.WriteLine("afl-fuzz string signature:");
                        }
                        else if (parameterType == typeof(ReadOnlySpan<byte>))
                        {
                            Console.Error.WriteLine("libfuzzer: ReadOnlySpan<byte> signature:");
                        }
                        writeHeader = false;
                    }

                    Console.Error.WriteLine("-- {0}", m.Name);
                }
            }
        }
    }
}
