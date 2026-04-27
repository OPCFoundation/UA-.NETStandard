/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;
using Opc.Ua.CttTestRunner.Runtime.Settings;

namespace Opc.Ua.CttTestRunner.Runtime.Types
{
    /// <summary>
    /// Implements the MonitoredItem helper object used by CTT scripts.
    /// Provides static factory methods: fromSettings(), fromNodeIds(), fromSetting().
    /// </summary>
    public sealed class CttMonitoredItemHelper
    {
        private readonly CttProjectSettings _project;
        private readonly ILogger _logger;
        private readonly Engine _engine;
        private int _nextClientHandle = 1;

        public CttMonitoredItemHelper(Engine engine, CttProjectSettings project, ILogger logger)
        {
            _engine = engine;
            _project = project;
            _logger = logger;
        }

        /// <summary>
        /// Creates a JS object exposing all MonitoredItem helper methods.
        /// </summary>
        public ObjectInstance ToJsObject(Engine engine)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            obj.Set("fromSettings", new ClrFunction(engine, "fromSettings",
                (_, args) => FromSettings(args)));
            obj.Set("fromNodeIds", new ClrFunction(engine, "fromNodeIds",
                (_, args) => FromNodeIds(args)));
            obj.Set("fromSetting", new ClrFunction(engine, "fromSetting",
                (_, args) => FromSetting(args)));
            obj.Set("fromSettingsExt", new ClrFunction(engine, "fromSettingsExt",
                (_, args) => FromSettingsExt(args)));
            obj.Set("GetNextClientHandle", new ClrFunction(engine, "GetNextClientHandle",
                (_, _) => JsValue.FromObject(engine, _nextClientHandle++)));
            obj.Set("GetSettingNames", new ClrFunction(engine, "GetSettingNames",
                (_, args) => GetSettingNames(args)));
            obj.Set("GetRequiredNodes", new ClrFunction(engine, "GetRequiredNodes",
                (_, args) => JsValue.Null));
            obj.Set("Clone", new ClrFunction(engine, "Clone",
                (_, args) => args.Length > 0 ? args[0] : JsValue.Null));
            obj.Set("toNodeIds", new ClrFunction(engine, "toNodeIds",
                (_, args) => ToNodeIds(args)));
            obj.Set("toIdsArray", new ClrFunction(engine, "toIdsArray",
                (_, args) => ToNodeIds(args)));
            obj.Set("fromUaRefDescHelper", new ClrFunction(engine, "fromUaRefDescHelper",
                (_, args) => JsValue.Null));
            obj.Set("GetValuesToString", new ClrFunction(engine, "GetValuesToString",
                (_, _) => JsValue.FromObject(engine, "")));
            obj.Set("GetAttributesAsNodes", new ClrFunction(engine, "GetAttributesAsNodes",
                (_, _) => _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));
            obj.Set("SafelySetArrayTypeKnown", new ClrFunction(engine, "SafelySetArrayTypeKnown",
                (_, _) => JsValue.Undefined));
            obj.Set("SafelySetValueTypeKnown", new ClrFunction(engine, "SafelySetValueTypeKnown",
                (_, _) => JsValue.Undefined));
            obj.Set("SafelySetValueTypeUnknown", new ClrFunction(engine, "SafelySetValueTypeUnknown",
                (_, _) => JsValue.Undefined));
            obj.Set("FromBrowsePathResults", new ClrFunction(engine, "FromBrowsePathResults",
                (_, _) => _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));
            obj.Set("FromBrowsePathTargets", new ClrFunction(engine, "FromBrowsePathTargets",
                (_, _) => _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));

            return obj;
        }

        private JsValue FromSettings(JsValue[] args)
        {
            // MonitoredItem.fromSettings(settingsArray, startIndex, attributeId, indexRange, monitoringMode, ...)
            if (args.Length == 0 || !args[0].IsObject()) return JsValue.Null;

            var settingsArray = args[0].AsObject();
            int startIndex = args.Length > 1 ? (int)args[1].AsNumber() : 0;
            int attributeId = args.Length > 2 ? (int)args[2].AsNumber() : 13; // Value
            string indexRange = args.Length > 3 && args[3].IsString() ? args[3].AsString() : "";
            int monitoringMode = args.Length > 4 ? (int)args[4].AsNumber() : 2; // Reporting

            var items = _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            int length = (int)settingsArray.Get("length").AsNumber();

            for (int i = startIndex; i < length; i++)
            {
                var settingPath = settingsArray.Get(i.ToString());
                if (!settingPath.IsString()) continue;

                string path = settingPath.AsString();
                string nodeIdStr = _project.ReadSetting(path);

                if (string.IsNullOrEmpty(nodeIdStr)) continue;

                var mi = CreateMonitoredItem(nodeIdStr, attributeId, indexRange, monitoringMode, path);
                var arr = items.AsObject();
                arr.Set((int)arr.Get("length").AsNumber(), mi);
            }

            var resultArr = items.AsObject();
            return (int)resultArr.Get("length").AsNumber() > 0 ? items : JsValue.Null;
        }

        private JsValue FromNodeIds(JsValue[] args)
        {
            // MonitoredItem.fromNodeIds(nodeIdArray, attributeId)
            if (args.Length == 0 || !args[0].IsObject()) return JsValue.Null;

            var nodeIdArray = args[0].AsObject();
            int attributeId = args.Length > 1 ? (int)args[1].AsNumber() : 13;
            int length = (int)nodeIdArray.Get("length").AsNumber();

            var items = new JsValue[length];
            for (int i = 0; i < length; i++)
            {
                var nodeId = nodeIdArray.Get(i.ToString());
                items[i] = CreateMonitoredItemFromNodeId(nodeId, attributeId);
            }

            return _engine.Intrinsics.Array.Construct(items);
        }

        private JsValue FromSetting(JsValue[] args)
        {
            // MonitoredItem.fromSetting(settingPath, startIndex, attributeId, indexRange, monitoringMode)
            if (args.Length == 0 || !args[0].IsString()) return JsValue.Null;

            string path = args[0].AsString();
            int attributeId = args.Length > 2 ? (int)args[2].AsNumber() : 13;
            string indexRange = args.Length > 3 && args[3].IsString() ? args[3].AsString() : "";
            int monitoringMode = args.Length > 4 ? (int)args[4].AsNumber() : 2;

            string nodeIdStr = _project.ReadSetting(path);
            if (string.IsNullOrEmpty(nodeIdStr))
            {
                _logger.LogDebug("Setting not found: {Path}", path);
                return JsValue.Null;
            }

            return CreateMonitoredItem(nodeIdStr, attributeId, indexRange, monitoringMode, path);
        }

        private JsValue FromSettingsExt(JsValue[] args)
        {
            // fromSettingsExt({ Settings: [...], AttributeId: 13, ... })
            if (args.Length == 0 || !args[0].IsObject()) return JsValue.Null;
            var argsObj = args[0].AsObject();

            var settings = argsObj.Get("Settings");
            var attrId = argsObj.Get("AttributeId");
            int attributeId = CttGlobals.IsDefined(attrId) ? (int)attrId.AsNumber() : 13;

            if (!CttGlobals.IsDefined(settings)) return JsValue.Null;

            return FromSettings(new[] {
                settings,
                JsValue.FromObject(_engine, 0),
                JsValue.FromObject(_engine, attributeId)
            });
        }

        private JsValue CreateMonitoredItem(string nodeIdStr, int attributeId, string indexRange, int monitoringMode, string settingPath)
        {
            var obj = (ObjectInstance)_engine.Intrinsics.Object.Construct(Array.Empty<Jint.Native.JsValue>());
            obj.Set("NodeId", JsValue.FromObject(_engine, nodeIdStr));
            obj.Set("AttributeId", JsValue.FromObject(_engine, attributeId));
            obj.Set("IndexRange", JsValue.FromObject(_engine, indexRange));
            obj.Set("MonitoringMode", JsValue.FromObject(_engine, monitoringMode));
            obj.Set("ClientHandle", JsValue.FromObject(_engine, _nextClientHandle++));
            obj.Set("SettingName", JsValue.FromObject(_engine, settingPath));
            obj.Set("Value", JsValue.Null);
            obj.Set("DataType", JsValue.FromObject(_engine, 0));
            obj.Set("ArrayUpperBound", JsValue.FromObject(_engine, -1));
            obj.Set("IsArray", JsValue.FromObject(_engine, false));
            obj.Set("DataEncoding", JsValue.Undefined);
            obj.Set("SamplingInterval", JsValue.FromObject(_engine, 1000));
            obj.Set("QueueSize", JsValue.FromObject(_engine, 1));
            obj.Set("DiscardOldest", JsValue.FromObject(_engine, true));
            obj.Set("Filter", JsValue.Null);

            // SetBrowse helper
            obj.Set("SetBrowse", new ClrFunction(_engine, "SetBrowse",
                (thisObj, args) =>
                {
                    if (args.Length >= 1) thisObj.AsObject().Set("BrowseDirection", args[0]);
                    if (args.Length >= 2) thisObj.AsObject().Set("IncludeSubtypes", args[1]);
                    if (args.Length >= 3) thisObj.AsObject().Set("NodeClassMask", args[2]);
                    if (args.Length >= 4) thisObj.AsObject().Set("ReferenceTypeId", args[3]);
                    if (args.Length >= 5) thisObj.AsObject().Set("ResultMask", args[4]);
                    return JsValue.Undefined;
                }));

            // clone()
            obj.Set("clone", new ClrFunction(_engine, "clone",
                (_, _) => CreateMonitoredItem(nodeIdStr, attributeId, indexRange, monitoringMode, settingPath)));

            // SafeNodeId
            obj.Set("SafeNodeId", new ClrFunction(_engine, "SafeNodeId",
                (_, _) => JsValue.FromObject(_engine, nodeIdStr)));

            // ClearVQTT
            obj.Set("ClearVQTT", new ClrFunction(_engine, "ClearVQTT",
                (thisVal, _) =>
                {
                    thisVal.AsObject().Set("Value", JsValue.Null);
                    return JsValue.Undefined;
                }));

            return obj;
        }

        private JsValue CreateMonitoredItemFromNodeId(JsValue nodeId, int attributeId)
        {
            string nodeIdStr = nodeId.IsString() ? nodeId.AsString()
                : nodeId.IsObject() ? nodeId.AsObject().Get("toString").AsObject()
                    .Call(nodeId, Array.Empty<JsValue>()).AsString()
                : "i=0";
            return CreateMonitoredItem(nodeIdStr, attributeId, "", 2, "");
        }

        private JsValue GetSettingNames(JsValue[] args)
        {
            if (args.Length == 0 || !args[0].IsObject()) return _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            var items = args[0].AsObject();

            if (items.HasProperty("length"))
            {
                int length = (int)items.Get("length").AsNumber();
                var names = new JsValue[length];
                for (int i = 0; i < length; i++)
                {
                    var item = items.Get(i.ToString());
                    names[i] = CttGlobals.IsDefined(item) && item.IsObject()
                        ? item.AsObject().Get("SettingName")
                        : JsValue.FromObject(_engine, "");
                }
                return _engine.Intrinsics.Array.Construct(names);
            }
            else
            {
                // Single item
                return _engine.Intrinsics.Array.Construct(
                    new[] { items.Get("SettingName") });
            }
        }

        private JsValue ToNodeIds(JsValue[] args)
        {
            if (args.Length == 0 || !args[0].IsObject()) return _engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            var items = args[0].AsObject();
            int length = (int)items.Get("length").AsNumber();
            var nodeIds = new JsValue[length];
            for (int i = 0; i < length; i++)
            {
                var item = items.Get(i.ToString()).AsObject();
                nodeIds[i] = item.Get("NodeId");
            }
            return _engine.Intrinsics.Array.Construct(nodeIds);
        }
    }
}

