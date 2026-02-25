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
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.SourceGeneration.Tester
{
#pragma warning disable CA1815 // Override equals and operator equals on value types

    public interface IVariantBuilder<T>
    {
        T GetValue();
    }

    public readonly struct VariantBuilder : IVariantBuilder<uint>, IVariantBuilder<string>
    {
        public static readonly VariantBuilder New;

        string IVariantBuilder<string>.GetValue()
        {
            throw new NotImplementedException();
        }

        uint IVariantBuilder<uint>.GetValue()
        {
            throw new NotImplementedException();
        }
    }

    public readonly struct StructureBuilder<T> : IVariantBuilder<T> where T : IDisposable
    {
        public T GetValue()
        {
            throw new NotImplementedException();
        }
    }

    public readonly struct EnumerationBuilder<T> : IVariantBuilder<T> where T : Enum
    {
        public T GetValue()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class MyDisposabe : IDisposable
    {
        public void Dispose() { }
    }

    public enum MyEnum { Value = 0 }

    public static class P
    {
        public static void Tester()
        {
            var t1 =  VariableState<uint>.With<VariantBuilder>();
            var t1a = VariableState<MyDisposabe>.With<StructureBuilder<MyDisposabe>>();
            var t1b = VariableState<MyEnum>.With<EnumerationBuilder<MyEnum>>();
        }
    }

    public abstract class VariableState<T>
    {
        protected VariableState(NodeState parent) { }

        public abstract T Value { get; }

        public static VariableState<T> With<TBuilder>(NodeState parent = null)
           where TBuilder : struct, IVariantBuilder<T>
        {
            return new VariableState<T>.Implementation<TBuilder>(parent);
        }

        public class Implementation<TBuilder> : VariableState<T>
            where TBuilder : struct, IVariantBuilder<T>
        {
            public Implementation(NodeState parent) : base(parent) { }

            public override T Value => m_v.GetValue();

            public object Clone()
            {
                var copy = new Implementation<TBuilder>(null);
                return copy;
            }
            public TBuilder m_v = new();
        }
    }

    public class ExtendedVariableState : VariableState<string>.Implementation<VariantBuilder>
    {
        protected ExtendedVariableState(NodeState parent) : base(parent) { }
    }

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

            string[] models =
            [
                "TestDataDesign",
                "DemoModel"
            ];
            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(Directory.GetCurrentDirectory(), "Resources"), "*.xml"))
            {
                string csvFile = Path.ChangeExtension(file, "csv");
                if (!File.Exists(csvFile))
                {
                    continue;
                }
                Generators.GenerateCode(new DesignFileCollection
                {
                    DesignFiles = [ file ],
                    IdentifierFilePath = csvFile,
                    Options = new DesignFileOptions()
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
