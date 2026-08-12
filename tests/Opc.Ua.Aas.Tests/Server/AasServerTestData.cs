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

using NUnit.Framework;
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Server
{
    internal static class AasServerTestData
    {
        public const string SubmodelId = "urn:test:submodel";
        public const string PropertyName = "Temperature";
        public const string OperationName = "Calibrate";

        public static AasEnvironment CreateEnvironment()
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(
                    new ArrayOf<AasSubmodel>(new[]
                    {
                        new AasSubmodel
                        {
                            Id = SubmodelId,
                            IdShort = AasOptional<string>.Present("sm"),
                            SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                                new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                                {
                                    CreateProperty(PropertyName, "42"),
                                    CreateOperation()
                                }))
                        }
                    }))
            };
        }

        public static AasProperty CreateProperty(string idShort, string value)
        {
            return new AasProperty
            {
                IdShort = AasOptional<string>.Present(idShort),
                ValueType = AASDataTypeDefXsdDataType.String,
                Value = AasOptional<Variant>.Present(Variant.From(value))
            };
        }

        public static AasOperation CreateOperation()
        {
            return new AasOperation
            {
                IdShort = AasOptional<string>.Present(OperationName),
                InputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                    {
                        CreateProperty("Input", "in")
                    })),
                OutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                    {
                        CreateProperty("Output", "out")
                    })),
                InoutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                    new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                    {
                        CreateProperty("Inoutput", "both")
                    }))
            };
        }

        public static NodeId ElementNodeId(string idShortPath)
        {
            return new NodeId(
                AasNodeIdEncoding.CreateElementId(SubmodelId, idShortPath),
                namespaceIndex: 1);
        }

        public static NodeId MemberNodeId(NodeId parent, string browseName)
        {
            Assert.That(parent.TryGetValue(out string? identifier), Is.True);
            return new NodeId(identifier + "." + AasNodeIdEncoding.Escape(browseName), parent.NamespaceIndex);
        }
    }
}
