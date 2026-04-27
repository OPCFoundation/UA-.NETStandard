/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// Tracks which OPC UA services have been tested during the run.
    /// </summary>
    public sealed class CttServiceRegister
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, ServiceInfo> _services = new();

        public CttServiceRegister(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a JS object exposing Register, SetFailed, and UaService methods.
        /// </summary>
        public ObjectInstance ToJsObject()
        {
            var engine = new Engine();
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            obj.Set("Register", new ClrFunction(engine, "Register",
                (_, args) =>
                {
                    if (args.Length > 0 && args[0].IsObject())
                    {
                        var svc = args[0].AsObject().Get("Service");
                        if (svc.IsObject())
                        {
                            var name = svc.AsObject().Get("Name").AsString();
                            _services[name] = new ServiceInfo { Name = name, Tested = true };
                        }
                    }
                    return JsValue.Undefined;
                }));

            obj.Set("SetFailed", new ClrFunction(engine, "SetFailed",
                (_, args) =>
                {
                    if (args.Length > 0 && args[0].IsObject())
                    {
                        var name = args[0].AsObject().Get("Name").AsString();
                        if (_services.TryGetValue(name, out var info))
                        {
                            info.Failed = true;
                        }
                    }
                    return JsValue.Undefined;
                }));

            obj.Set("UaService", new ClrFunction(engine, "UaService",
                (_, args) =>
                {
                    var svcObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    if (args.Length > 0 && args[0].IsObject())
                    {
                        var argsObj = args[0].AsObject();
                        svcObj.Set("Name", argsObj.Get("Name"));
                        svcObj.Set("Available",
                            argsObj.HasProperty("Available") ? argsObj.Get("Available") : JsValue.FromObject(engine, true));
                        svcObj.Set("Tested",
                            argsObj.HasProperty("Tested") ? argsObj.Get("Tested") : JsValue.FromObject(engine, true));
                    }
                    return svcObj;
                }));

            return obj;
        }

        private sealed class ServiceInfo
        {
            public string Name { get; set; } = "";
            public bool Tested { get; set; }
            public bool Failed { get; set; }
        }
    }
}

