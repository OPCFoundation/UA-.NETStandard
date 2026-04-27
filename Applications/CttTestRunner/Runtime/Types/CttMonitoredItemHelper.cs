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
    public sealed class CttMonitoredItemHelper : ObjectInstance
    {
        private readonly CttProjectSettings _project;
        private readonly ILogger _logger;
        private int _nextClientHandle = 1;

        public CttMonitoredItemHelper(Engine engine, CttProjectSettings project, ILogger logger)
            : base(engine)
        {
            _project = project;
            _logger = logger;

            // Static methods
            FastSetDataProperty("fromSettings", new ClrFunction(engine, "fromSettings",
                (_, args) => FromSettings(args)));
            FastSetDataProperty("fromNodeIds", new ClrFunction(engine, "fromNodeIds",
                (_, args) => FromNodeIds(args)));
            FastSetDataProperty("fromSetting", new ClrFunction(engine, "fromSetting",
                (_, args) => FromSetting(args)));
            FastSetDataProperty("fromSettingsExt", new ClrFunction(engine, "fromSettingsExt",
                (_, args) => FromSettingsExt(args)));
            FastSetDataProperty("GetNextClientHandle", new ClrFunction(engine, "GetNextClientHandle",
                (_, _) => JsValue.FromObject(engine, _nextClientHandle++)));
            FastSetDataProperty("GetSettingNames", new ClrFunction(engine, "GetSettingNames",
                (_, args) => GetSettingNames(args)));
            FastSetDataProperty("GetRequiredNodes", new ClrFunction(engine, "GetRequiredNodes",
                (_, args) => JsValue.Null));
            FastSetDataProperty("Clone", new ClrFunction(engine, "Clone",
                (_, args) => args.Length > 0 ? args[0] : JsValue.Null));
            FastSetDataProperty("toNodeIds", new ClrFunction(engine, "toNodeIds",
                (_, args) => ToNodeIds(args)));
            FastSetDataProperty("toIdsArray", new ClrFunction(engine, "toIdsArray",
                (_, args) => ToNodeIds(args)));
            FastSetDataProperty("fromUaRefDescHelper", new ClrFunction(engine, "fromUaRefDescHelper",
                (_, args) => JsValue.Null));
            FastSetDataProperty("GetValuesToString", new ClrFunction(engine, "GetValuesToString",
                (_, _) => JsValue.FromObject(engine, "")));
            FastSetDataProperty("GetAttributesAsNodes", new ClrFunction(engine, "GetAttributesAsNodes",
                (_, _) => Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));
            FastSetDataProperty("SafelySetArrayTypeKnown", new ClrFunction(engine, "SafelySetArrayTypeKnown",
                (_, _) => JsValue.Undefined));
            FastSetDataProperty("SafelySetValueTypeKnown", new ClrFunction(engine, "SafelySetValueTypeKnown",
                (_, _) => JsValue.Undefined));
            FastSetDataProperty("SafelySetValueTypeUnknown", new ClrFunction(engine, "SafelySetValueTypeUnknown",
                (_, _) => JsValue.Undefined));
            FastSetDataProperty("FromBrowsePathResults", new ClrFunction(engine, "FromBrowsePathResults",
                (_, _) => Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));
            FastSetDataProperty("FromBrowsePathTargets", new ClrFunction(engine, "FromBrowsePathTargets",
                (_, _) => Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>())));
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

            var items = Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
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

            return Engine.Intrinsics.Array.Construct(items);
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
                JsValue.FromObject(Engine, 0),
                JsValue.FromObject(Engine, attributeId)
            });
        }

        private JsValue CreateMonitoredItem(string nodeIdStr, int attributeId, string indexRange, int monitoringMode, string settingPath)
        {
            var obj = (ObjectInstance)Engine.Intrinsics.Object.Construct(Array.Empty<Jint.Native.JsValue>());
            obj.Set("NodeId", JsValue.FromObject(Engine, nodeIdStr));
            obj.Set("AttributeId", JsValue.FromObject(Engine, attributeId));
            obj.Set("IndexRange", JsValue.FromObject(Engine, indexRange));
            obj.Set("MonitoringMode", JsValue.FromObject(Engine, monitoringMode));
            obj.Set("ClientHandle", JsValue.FromObject(Engine, _nextClientHandle++));
            obj.Set("SettingName", JsValue.FromObject(Engine, settingPath));
            obj.Set("Value", JsValue.Null);
            obj.Set("DataType", JsValue.FromObject(Engine, 0));
            obj.Set("ArrayUpperBound", JsValue.FromObject(Engine, -1));
            obj.Set("IsArray", JsValue.FromObject(Engine, false));
            obj.Set("DataEncoding", JsValue.Undefined);
            obj.Set("SamplingInterval", JsValue.FromObject(Engine, 1000));
            obj.Set("QueueSize", JsValue.FromObject(Engine, 1));
            obj.Set("DiscardOldest", JsValue.FromObject(Engine, true));
            obj.Set("Filter", JsValue.Null);

            // SetBrowse helper
            obj.Set("SetBrowse", new ClrFunction(Engine, "SetBrowse",
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
            obj.Set("clone", new ClrFunction(Engine, "clone",
                (_, _) => CreateMonitoredItem(nodeIdStr, attributeId, indexRange, monitoringMode, settingPath)));

            // SafeNodeId
            obj.Set("SafeNodeId", new ClrFunction(Engine, "SafeNodeId",
                (_, _) => JsValue.FromObject(Engine, nodeIdStr)));

            // ClearVQTT
            obj.Set("ClearVQTT", new ClrFunction(Engine, "ClearVQTT",
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
            if (args.Length == 0 || !args[0].IsObject()) return Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
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
                        : JsValue.FromObject(Engine, "");
                }
                return Engine.Intrinsics.Array.Construct(names);
            }
            else
            {
                // Single item
                return Engine.Intrinsics.Array.Construct(
                    new[] { items.Get("SettingName") });
            }
        }

        private JsValue ToNodeIds(JsValue[] args)
        {
            if (args.Length == 0 || !args[0].IsObject()) return Engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            var items = args[0].AsObject();
            int length = (int)items.Get("length").AsNumber();
            var nodeIds = new JsValue[length];
            for (int i = 0; i < length; i++)
            {
                var item = items.Get(i.ToString()).AsObject();
                nodeIds[i] = item.Get("NodeId");
            }
            return Engine.Intrinsics.Array.Construct(nodeIds);
        }
    }
}

