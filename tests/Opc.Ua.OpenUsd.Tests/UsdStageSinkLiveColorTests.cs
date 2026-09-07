/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Connector.Viewer;
using OpenUsd;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Verifies that live viewport colour writes use the exact OpenUSD attribute type
    /// authored by the sample assets.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdStageSinkLiveColorTests
    {
        [Test]
        public void DisplayColorBranchCallsColor3fArraySetter()
        {
            List<string> calls = CalledUsdPrimMethods("ApplyValue");

            Assert.That(calls, Does.Contain(nameof(UsdPrim.SetColor3fArray)));
            Assert.That(
                calls,
                Does.Not.Contain(nameof(UsdPrim.SetVec3fArray)),
                "A displayColor regression to SetVec3fArray leaves color3f[] primvars unchanged.");
        }

        [Test]
        public void DisplayColorFileSinkAuthorsColor3fArray()
        {
            string path = NewWorkFile("display-color");
            try
            {
                var sink = new UsdFileSink(path);
                sink.SetAttribute(
                    "/Plant/Pumps/P101/Body",
                    "primvars:displayColor",
                    new Variant((ArrayOf<float>)[1.0f, 0.0f, 0.0f]));

                string layer = File.ReadAllText(path);
                Assert.That(
                    layer,
                    Does.Contain("color3f[] primvars:displayColor = [(1.0000, 0.0000, 0.0000)]"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ShaderColourFileSinkAuthorsScalarColor3f()
        {
            string path = NewWorkFile("shader-color");
            try
            {
                var sink = new UsdFileSink(path);
                sink.SetAttribute(
                    "/Plant/Pumps/P101/Suction/Neck/Mat/Surface",
                    "inputs:diffuseColor",
                    new Variant((ArrayOf<float>)[0.1f, 0.2f, 0.3f]));

                string layer = File.ReadAllText(path);
                Assert.That(layer, Does.Contain("color3f inputs:diffuseColor = (0.1000, 0.2000, 0.3000)"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void PumpBearingTemperatureBindingTargetsAuthoredDisplayColor()
        {
            string representation = ReadRepositoryFile(
                "samples", "DI", "PumpDeviceIntegrationServer", "OpenUsdRepresentation.cs");
            string asset = ReadRepositoryFile(
                "samples", "DI", "PumpDeviceIntegrationServer", "Assets", "Plant.usda");

            Assert.That(
                representation,
                Does.Contain("bearingTemp, primPath + \"/Body\", \"primvars:displayColor\", \"color3f[]\""));
            Assert.That(BlockOf(asset, "Body"), Does.Contain("color3f[] primvars:displayColor"));
        }

        [Test]
        public void GeneratorThermalBindingTargetsAuthoredDisplayColor()
        {
            string bindings = ReadRepositoryFile(
                "samples", "OpenUsd", "GeneratorServer", "OpenUsdBindings.cs");
            string asset = ReadRepositoryFile(
                "samples", "OpenUsd", "GeneratorServer", "Assets", "generator.usda");

            Assert.That(bindings, Does.Contain("prim + \"/Radiator/Core\", \"primvars:displayColor\", \"color3f[]\""));
            Assert.That(BlockOf(asset, "Core"), Does.Contain("color3f[] primvars:displayColor"));
        }

        private static List<string> CalledUsdPrimMethods(string methodName)
        {
            MethodInfo method = typeof(UsdStageSink).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.That(method, Is.Not.Null);
            byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
            var calls = new List<string>();
            Module module = method.Module;
            int index = 0;
            while (index < il.Length)
            {
                OpCode opCode = ReadOpCode(il, ref index);
                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, index);
                    MemberInfo? member = module.ResolveMember(token);
                    if (member?.DeclaringType == typeof(UsdPrim))
                    {
                        calls.Add(member.Name);
                    }
                }
                index += OperandSize(opCode.OperandType, il, index);
            }
            return calls;
        }

        private static OpCode ReadOpCode(byte[] il, ref int index)
        {
            byte value = il[index++];
            if (value != 0xfe)
            {
                return s_singleByteOpCodes[value];
            }
            return s_doubleByteOpCodes[il[index++]];
        }

        private static int OperandSize(OperandType operandType, byte[] il, int index)
        {
            return operandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, index)),
                _ => throw new NotSupportedException($"Unsupported IL operand type {operandType}.")
            };
        }

        private static string BlockOf(string text, string primName)
        {
            string marker = string.Concat("\"", primName, "\"");
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{primName} must exist in the asset.");
            int next = text.IndexOf("\n        def ", start + marker.Length, StringComparison.Ordinal);
            return next < 0 ? text[start..] : text[start..next];
        }

        private static string ReadRepositoryFile(params string[] parts)
        {
            return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));
        }

        private static string NewWorkFile(string name)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "OpenUsdLiveColorTests");
            Directory.CreateDirectory(directory);
            string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            return Path.Combine(directory, string.Concat(name, "-", suffix, ".usda"));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.WorkDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root from the test directory.");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static readonly OpCode[] s_singleByteOpCodes = BuildOpCodeLookup(singleByte: true);

        private static readonly OpCode[] s_doubleByteOpCodes = BuildOpCodeLookup(singleByte: false);

        private static OpCode[] BuildOpCodeLookup(bool singleByte)
        {
            var lookup = new OpCode[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode opCode)
                {
                    continue;
                }
                ushort value = unchecked((ushort)opCode.Value);
                bool isSingleByte = (value & 0xff00) != 0xfe00;
                if (isSingleByte == singleByte)
                {
                    lookup[value & 0xff] = opCode;
                }
            }
            return lookup;
        }
    }
}
#endif
