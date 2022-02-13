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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Helpers to find node managers implemented in this library.
    /// </summary>

    public static class Utils
    {
        public static void AddDefaultNodeManagers(StandardServer server)
        {
            foreach (var nodeManagerFactory in NodeManagerFactories)
            {
                server.AddNodeManager(nodeManagerFactory);
            }
        }

        public static ReadOnlyList<INodeManagerFactory> NodeManagerFactories
        {
            get
            {
                if (m_nodeManagerFactories == null)
                {
                    m_nodeManagerFactories = GetNodeManagerFactories();
                }
                return new ReadOnlyList<INodeManagerFactory>(m_nodeManagerFactories);
            }
        }

        private static INodeManagerFactory IsINodeManagerFactoryType(Type type)
        {
            var nodeManagerTypeInfo = type.GetTypeInfo();
            if (nodeManagerTypeInfo.IsAbstract ||
                !typeof(INodeManagerFactory).IsAssignableFrom(type))
            {
                return null;
            }
            return Activator.CreateInstance(type) as INodeManagerFactory;
        }

        private static IList<INodeManagerFactory> GetNodeManagerFactories()
        {
            var assembly = typeof(Utils).Assembly;
            var nodeManagerFactories = assembly.GetExportedTypes().Select(type => IsINodeManagerFactoryType(type)).Where(type => type != null);
            return nodeManagerFactories.ToList();
        }

        private static IList<INodeManagerFactory> m_nodeManagerFactories;
    }
}


#if mist
#endif
