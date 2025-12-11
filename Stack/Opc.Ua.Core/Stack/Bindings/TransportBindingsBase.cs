/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
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
