/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The bindings for the transport listeners.
    /// </summary>
    public class TransportBindingsBase<T> :
        ITransportBindings<T> where T : class, ITransportBindingScheme
    {
        /// <summary>
        /// Implement the default constructor.
        /// </summary>
        /// <remarks>
        /// The default constructor adds all interfaces T.
        /// </remarks>
        protected TransportBindingsBase()
        {
            Bindings = new Dictionary<string, T>();
            AddBindings(typeof(TransportBindingsBase<T>).Assembly);
        }

        /// <summary>
        /// Initialize object with default list of bindings.
        /// </summary>
        protected TransportBindingsBase(Type[] defaultBindings)
        {
            Bindings = new Dictionary<string, T>();
            AddBindings(defaultBindings);
        }

        #region Public Properties
        /// <summary>
        /// Dictionary of bindings.
        /// </summary>
        protected Dictionary<string, T> Bindings { get; private set; }
        #endregion

        #region ITransportBindings
        /// <inheritdoc/>
        public T GetBinding(string uriScheme)
        {
            T binding;
            if (!Bindings.TryGetValue(uriScheme, out binding))
            {
                TryAddDefaultTransportBindings(uriScheme);
                if (!Bindings.TryGetValue(uriScheme, out binding))
                {
                    return default(T);
                }
            }
            return binding;
        }

        /// <inheritdoc/>
        public bool HasBinding(string uriScheme)
        {
            T binding;
            if (Bindings.TryGetValue(uriScheme, out binding))
            {
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void SetBinding(T binding)
        {
            Bindings[binding.UriScheme] = binding;
        }

        /// <inheritdoc/>
        public IEnumerable<Type> AddBindings(Assembly assembly)
        {
            var bindings = assembly.GetExportedTypes().Where(type => IsBindingType(type));
            return AddBindings(bindings);
        }

        /// <inheritdoc/>
        public IEnumerable<Type> AddBindings(IEnumerable<Type> bindings)
        {
            var result = new List<Type>();
            foreach (Type bindingType in bindings)
            {
                var binding = Activator.CreateInstance(bindingType) as T;
                if (binding != null)
                {
                    Bindings[binding.UriScheme] = binding;
                    result.Add(bindingType);
                }
            }
            return result;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Validate the type is a transport listener.
        /// </summary>
        protected static bool IsBindingType(System.Type bindingType)
        {
            if (bindingType == null)
            {
                return false;
            }

            var bindingTypeInfo = bindingType.GetTypeInfo();
            if (bindingTypeInfo.IsAbstract ||
                !typeof(T).GetTypeInfo().IsAssignableFrom(bindingTypeInfo))
            {
                return false;
            }

            var listener = Activator.CreateInstance(bindingType) as T;
            if (listener == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to load a binding from well known assemblies at runtime.
        /// </summary>
        /// <param name="scheme">The uri scheme of the binding.</param>
        private bool TryAddDefaultTransportBindings(string scheme)
        {
            string assemblyName;
            if (Utils.DefaultBindings.TryGetValue(scheme, out assemblyName))
            {
                Assembly assembly = null;
                string fullName = Utils.DefaultOpcUaCoreAssemblyFullName.Replace(Utils.DefaultOpcUaCoreAssemblyName, assemblyName);
                try
                {
                    assembly = Assembly.Load(fullName);
                }
                catch
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Failed to load the assembly {0} for transport binding {1}.",
                        fullName, scheme
                        );
                }

                if (assembly != null)
                {
                    var listeners = AddBindings(assembly);
                    return listeners.Count() > 0;
                }
            }
            else
            {
                Utils.Trace(Utils.TraceMasks.Error, "The transport binding {0} is unsupported.", scheme);
            }
            return false;
        }
        #endregion
    }
}
