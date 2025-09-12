/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The bindings for the transport listeners.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TransportBindingsBase<T> : ITransportBindings<T>
        where T : class, ITransportBindingScheme
    {
        /// <summary>
        /// Implement the default constructor.
        /// </summary>
        /// <remarks>
        /// The default constructor adds all interfaces T.
        /// </remarks>
        protected TransportBindingsBase()
        {
            Bindings = [];
            AddBindings(typeof(TransportBindingsBase<T>).Assembly);
        }

        /// <summary>
        /// Initialize object with default list of bindings.
        /// </summary>
        protected TransportBindingsBase(Type[] defaultBindings)
        {
            Bindings = [];
            AddBindings(defaultBindings);
        }

        /// <summary>
        /// Dictionary of bindings.
        /// </summary>
        protected Dictionary<string, T> Bindings { get; }

        /// <inheritdoc/>
        public T GetBinding(string uriScheme, ITelemetryContext telemetry)
        {
            if (!Bindings.TryGetValue(uriScheme, out T binding))
            {
                TryAddDefaultTransportBindings(telemetry, uriScheme);
                if (!Bindings.TryGetValue(uriScheme, out binding))
                {
                    return default;
                }
            }
            return binding;
        }

        /// <inheritdoc/>
        public bool HasBinding(string uriScheme)
        {
            return Bindings.TryGetValue(uriScheme, out _);
        }

        /// <inheritdoc/>
        public void SetBinding(T binding)
        {
            Bindings[binding.UriScheme] = binding;
        }

        /// <inheritdoc/>
        public IEnumerable<Type> AddBindings(Assembly assembly)
        {
            IEnumerable<Type> bindings = assembly.GetExportedTypes().Where(IsBindingType);
            return AddBindings(bindings);
        }

        /// <inheritdoc/>
        public IEnumerable<Type> AddBindings(IEnumerable<Type> bindings)
        {
            var result = new List<Type>();
            foreach (Type bindingType in bindings)
            {
                if (Activator.CreateInstance(bindingType) is T binding)
                {
                    Bindings[binding.UriScheme] = binding;
                    result.Add(bindingType);
                }
            }
            return result;
        }

        /// <summary>
        /// Validate the type is a transport listener.
        /// </summary>
        protected static bool IsBindingType(Type bindingType)
        {
            if (bindingType == null)
            {
                return false;
            }

            System.Reflection.TypeInfo bindingTypeInfo = bindingType.GetTypeInfo();
            if (bindingTypeInfo.IsAbstract ||
                !typeof(T).GetTypeInfo().IsAssignableFrom(bindingTypeInfo))
            {
                return false;
            }

            if (Activator.CreateInstance(bindingType) is not T)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to load a binding from well known assemblies at runtime.
        /// </summary>
        /// <param name="telemetry"></param>
        /// <param name="scheme">The uri scheme of the binding.</param>
        private bool TryAddDefaultTransportBindings(ITelemetryContext telemetry, string scheme)
        {
            ILogger<TransportBindingsBase<T>> logger = telemetry.CreateLogger<TransportBindingsBase<T>>();
            if (Utils.DefaultBindings.TryGetValue(scheme, out string assemblyName))
            {
                Assembly assembly = null;
                string fullName = Utils.DefaultOpcUaCoreAssemblyFullName.Replace(
                    Utils.DefaultOpcUaCoreAssemblyName,
                    assemblyName,
                    StringComparison.Ordinal);
                try
                {
                    assembly = Assembly.Load(fullName);
                }
                catch
                {
                    logger.LogError(
                        "Failed to load the assembly {FullName} for transport binding {Scheme}.",
                        fullName,
                        scheme);
                }

                if (assembly != null)
                {
                    IEnumerable<Type> listeners = AddBindings(assembly);
                    return listeners.Any();
                }
            }
            else
            {
                logger.LogError("The transport binding {Scheme} is unsupported.", scheme);
            }
            return false;
        }
    }
}
