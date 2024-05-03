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
    public static List<Delegate> FindFuzzMethods(Type delegateType)
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
