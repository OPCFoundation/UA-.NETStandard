/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Reflection.Emit;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Use a single assembly and module builder instance to build the type system.
    /// </summary>
    public class AssemblyModule
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public AssemblyModule(string assemblyName = null)
        {
            m_assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(assemblyName ?? Guid.NewGuid().ToString()),
                AssemblyBuilderAccess.Run);
            m_moduleBuilder = m_assemblyBuilder.DefineDynamicModule(m_opcTypesModuleName);
        }
        #endregion

        #region Public Members
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
        public Type[] GetTypes()
        {
            return m_assemblyBuilder.GetTypes();
        }
        #endregion

        #region Private Fields
        AssemblyBuilder m_assemblyBuilder;
        ModuleBuilder m_moduleBuilder;
        private const string m_opcTypesModuleName = "Opc.Ua.ComplexTypes.Module";
        #endregion
    }

}//namespace
