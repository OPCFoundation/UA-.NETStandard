// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public class NetworkTests : FuzzTargetTestsBase
    {
        [DatapointSource]
        public static readonly FuzzTargetFunction[] FuzzableFunctions =
            CreateFuzzTargetFunctions(typeof(FuzzableCode));

        protected override Type FuzzableCodeType => typeof(FuzzableCode);
    }
}
