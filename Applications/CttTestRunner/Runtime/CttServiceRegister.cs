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
    public sealed class CttServiceRegister : ObjectInstance
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, ServiceInfo> _services = new();

        public CttServiceRegister(ILogger logger) : base(new Engine())
        {
            _logger = logger;

            FastSetDataProperty("Register", new ClrFunction(Engine, "Register",
                (_, args) =>
                {
                    if (args.Length > 0 && args[0].Type == Jint.Native.JsValueType.Object)
                    {
                        var svc = ((ObjectInstance)args[0]).Get("Service");
                        if (svc.Type == Jint.Native.JsValueType.Object)
                        {
                            var name = ((ObjectInstance)svc).Get("Name").AsString();
                            _services[name] = new ServiceInfo { Name = name, Tested = true };
                        }
                    }
                    return JsValue.Undefined;
                }));

            FastSetDataProperty("SetFailed", new ClrFunction(Engine, "SetFailed",
                (_, args) =>
                {
                    if (args.Length > 0 && args[0].Type == Jint.Native.JsValueType.Object)
                    {
                        var name = ((ObjectInstance)args[0]).Get("Name").AsString();
                        if (_services.TryGetValue(name, out var info))
                        {
                            info.Failed = true;
                        }
                    }
                    return JsValue.Undefined;
                }));

            FastSetDataProperty("UaService", new ClrFunction(Engine, "UaService",
                (_, args) =>
                {
                    var obj = (ObjectInstance)Engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    if (args.Length > 0 && args[0].Type == Jint.Native.JsValueType.Object)
                    {
                        var argsObj = (ObjectInstance)args[0];
                        obj.Set("Name", argsObj.Get("Name"));
                        obj.Set("Available",
                            argsObj.HasProperty("Available") ? argsObj.Get("Available") : JsValue.FromObject(Engine, true));
                        obj.Set("Tested",
                            argsObj.HasProperty("Tested") ? argsObj.Get("Tested") : JsValue.FromObject(Engine, true));
                    }
                    return obj;
                }));
        }

        private sealed class ServiceInfo
        {
            public string Name { get; set; } = "";
            public bool Tested { get; set; }
            public bool Failed { get; set; }
        }
    }
}

