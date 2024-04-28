

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SharpFuzz;

public static class FuzzMethods
{
    // signatures supported by fuzzers
    public delegate void AflFuzzStream(Stream stream);
    public delegate void AflFuzzString(string text);
    public delegate void LibFuzzSpan(ReadOnlySpan<byte> bytes);

    public static readonly Type[] Delegates = new Type[] { typeof(AflFuzzStream), typeof(AflFuzzString), typeof(LibFuzzSpan) };

    public static readonly Dictionary<Type, Type> FuzzMethodsToParameterType = new Dictionary<Type, Type>
    {
        { typeof(AflFuzzStream), typeof(Stream) },
        { typeof(AflFuzzString), typeof(string) },
        { typeof(LibFuzzSpan), typeof(ReadOnlySpan<byte>) }
    };

    /// <summary>
    /// Finds all fuzzing methods for specified delegate.
    /// </summary>
    public static List<Delegate> FindFuzzMethods(TextWriter errorOutput, Type delegateType)
    {
        List<Delegate> fuzzMethods = new List<Delegate>();
        Type delegateParameterType;
        Type type = typeof(FuzzableCode);
        if (FuzzMethodsToParameterType.TryGetValue(delegateType, out delegateParameterType))
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                // Determine the target signature
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == delegateParameterType)
                {
                    fuzzMethods.Add(method.CreateDelegate(delegateType));
                }
            }
        }
        return fuzzMethods;
    }

    /// <summary>
    /// Finds a fuzzing method by name and returns a delegate to call it.
    /// </summary>
    public static Delegate FindFuzzMethod(TextWriter errorOutput, string fuzzingFunction)
    {
        // find the function to fuzz based on the first argument using reflection
        Type type = typeof(FuzzableCode);
        MethodInfo method = type.GetMethod(fuzzingFunction, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (method != null)
        {
            // Determine the target signature
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1)
            {
                // afl-fuzz targets
                if (parameters[0].ParameterType == typeof(Stream))
                {
                    return (AflFuzzStream)method.CreateDelegate(typeof(AflFuzzStream));
                }
                else if (parameters[0].ParameterType == typeof(string))
                {
                    return (AflFuzzString)method.CreateDelegate(typeof(AflFuzzString));
                }
                // libfuzzer span target
                else if (parameters[0].ParameterType == typeof(ReadOnlySpan<byte>))
                {
                    return (LibFuzzSpan)method.CreateDelegate(typeof(LibFuzzSpan));
                }
            }

            errorOutput.WriteLine("The fuzzing function {0} does not have the correct signature {1}.", fuzzingFunction, parameters[0].ParameterType);
        }
        else
        {
            errorOutput.WriteLine("The fuzzing function {0} was not found.", fuzzingFunction);
        }

        return null;
    }

    /// <summary>
    /// Runs the fuzzing method with the given delegate.
    /// </summary>
    public static void RunFuzzMethod(Delegate fuzzingMethod, bool outOfProcess = false)
    {
        // find the function to fuzz method based on the type
        if (fuzzingMethod is AflFuzzStream aflFuzzStreamMethod)
        {
            if (outOfProcess)
            {
                Fuzzer.OutOfProcess.Run(stream => aflFuzzStreamMethod(stream));
            }
            else
            {
                Fuzzer.Run(stream => aflFuzzStreamMethod(stream));
            }
            return;
        }
        else if (fuzzingMethod is AflFuzzString aflFuzzStringMethod)
        {
            if (outOfProcess)
            {
                Fuzzer.OutOfProcess.Run(text => aflFuzzStringMethod(text));
            }
            else
            {
                Fuzzer.Run(text => aflFuzzStringMethod(text));
            }
            return;
        }
        // libfuzzer span target
        else if (fuzzingMethod is LibFuzzSpan libFuzzSpanMethod)
        {
            Fuzzer.LibFuzzer.Run(bytes => libFuzzSpanMethod(bytes));
            return;
        }
    }
}
