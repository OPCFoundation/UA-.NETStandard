// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Opc.Ua.Fuzzing
{
    internal static class Program
    {
        /// <summary>
        /// Main Method
        /// </summary>
        public static void Main(string[] args)
        {
            IConfig config = ManualConfig
                .Create(DefaultConfig.Instance)
                // need this option because of reference to nunit.framework
                .WithOptions(ConfigOptions.DisableOptimizationsValidator);
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
