/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Use a single assembly and module builder instance to build the type system.
    /// </summary>
    public class AssemblyModule
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Dynamic assembly creation is fundamental to ComplexTypes.")]
        public AssemblyModule(string? assemblyName = null)
        {
            m_assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(assemblyName ?? Guid.NewGuid().ToString()),
                AssemblyBuilderAccess.RunAndCollect);
            m_moduleBuilder = m_assemblyBuilder.DefineDynamicModule(kOpcTypesModuleName);
        }

        /// <summary>
        /// Get the module builder instance.
        /// </summary>
        public ModuleBuilder GetModuleBuilder()
        {
            return m_moduleBuilder;
        }

        /// <summary>
        /// Get the types defined in this assembly.
        /// </summary>
        [RequiresUnreferencedCode(
            "Uses Assembly.GetTypes which requires unreferenced code.")]
        public Type[] GetTypes()
        {
            return m_assemblyBuilder.GetTypes();
        }

        /// <summary>
        /// Register a type that is created without Reflection.Emit,
        /// e.g. a Structure-backed OptionSet sub-type.
        /// </summary>
        internal void AddRuntimeType(IType type)
        {
            lock (m_runtimeTypesLock)
            {
                m_runtimeTypes.Add(type);
            }
        }

        /// <summary>
        /// Get the types created without Reflection.Emit.
        /// </summary>
        internal IReadOnlyList<IType> GetRuntimeTypes()
        {
            lock (m_runtimeTypesLock)
            {
                return [.. m_runtimeTypes];
            }
        }

        private readonly AssemblyBuilder m_assemblyBuilder;
        private readonly ModuleBuilder m_moduleBuilder;
        private readonly List<IType> m_runtimeTypes = [];
        private readonly Lock m_runtimeTypesLock = new();
        private const string kOpcTypesModuleName = "Opc.Ua.ComplexTypes.Module";
    }
}
