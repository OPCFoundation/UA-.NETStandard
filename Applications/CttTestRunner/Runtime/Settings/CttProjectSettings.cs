/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;

namespace Opc.Ua.CttTestRunner.Runtime.Settings
{
    /// <summary>
    /// Parses and manages the CTT .ctt.xml project settings file.
    /// The file uses Qt's AbstractItemModelData format with nested rows/columns.
    /// </summary>
    public sealed class CttProjectSettings
    {
        private readonly string _filePath;
        private readonly string _projectDir;
        private readonly SettingsNode _root;

        public string ServerUrl { get; private set; } = "opc.tcp://localhost:4840";
        public int SecurityMode { get; private set; } = 1; // None
        public string SecurityPolicy { get; private set; } = "";
        public int UserIdentityType { get; private set; } // 0=Anonymous

        public CttProjectSettings(string filePath)
        {
            _filePath = filePath;
            _projectDir = Path.GetDirectoryName(filePath) ?? ".";
            _root = new SettingsNode("Root");
            ParseProjectFile();
        }

        /// <summary>
        /// Read a setting value by path, e.g. "/Server Test/NodeIds/Static/All Profiles/Scalar/Bool"
        /// </summary>
        public string ReadSetting(string path)
        {
            var node = NavigateTo(path);
            return node?.Value ?? "";
        }

        /// <summary>
        /// Write a setting value by path.
        /// </summary>
        public void WriteSetting(string path, string value)
        {
            var node = NavigateTo(path);
            if (node != null)
            {
                node.Value = value;
            }
        }

        /// <summary>
        /// Resolves a PKI path relative to the project directory.
        /// </summary>
        public string ResolvePath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(_projectDir, relativePath));
        }

        /// <summary>
        /// Builds the Settings JS object hierarchy from the project file.
        /// </summary>
        public JsValue BuildSettingsObject(Engine engine)
        {
            var settings = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            // Build Settings.ServerTest from "/Server Test" subtree
            var serverTest = BuildSettingsSubtree(engine, "Server Test");
            settings.Set("ServerTest", serverTest);

            // Build other top-level settings
            settings.Set("Address_Server",
                JsValue.FromObject(engine, ServerUrl));
            settings.Set("Address_CTT",
                JsValue.FromObject(engine, "opc.tcp://localhost:4843"));

            // Advanced settings
            var advanced = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            settings.Set("Advanced", advanced);

            return settings;
        }

        private ObjectInstance BuildSettingsSubtree(Engine engine, string rootName)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            var rootNode = _root.Children.FirstOrDefault(c =>
                string.Equals(c.Name, rootName, StringComparison.OrdinalIgnoreCase));
            if (rootNode != null)
            {
                PopulateSettingsObject(engine, obj, rootNode);
            }
            return obj;
        }

        private void PopulateSettingsObject(Engine engine, ObjectInstance parent, SettingsNode node)
        {
            foreach (var child in node.Children)
            {
                string propName = SanitizePropertyName(child.Name);
                if (child.Children.Count > 0)
                {
                    var childObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    PopulateSettingsObject(engine, childObj, child);

                    // If the node also has a value, add it as a special property
                    if (!string.IsNullOrEmpty(child.Value))
                    {
                        childObj.Set("_value",
                            JsValue.FromObject(engine, child.Value));
                    }
                    parent.Set(propName, childObj);
                }
                else
                {
                    parent.Set(propName,
                        JsValue.FromObject(engine, child.Value ?? ""));
                }
            }

            // Also create a Settings array (for MonitoredItem.fromSettings)
            if (node.Children.Any(c => c.Children.Count == 0 && !string.IsNullOrEmpty(c.Value)))
            {
                var settingPaths = node.Children
                    .Where(c => c.Children.Count == 0 && !string.IsNullOrEmpty(c.Value))
                    .Select(c => c.FullPath)
                    .ToArray();
                var arr = engine.Intrinsics.Array.Construct(
                    settingPaths.Select(p => JsValue.FromObject(engine, p)).ToArray());
                parent.Set("Settings", arr);
            }
        }

        private static string SanitizePropertyName(string name)
        {
            // Convert "All Profiles" -> "AllProfiles", "Scalar" -> "Scalar"
            return name.Replace(" ", "").Replace("&", "And").Replace("-", "_");
        }

        private SettingsNode? NavigateTo(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var parts = path.Trim('/').Split('/');
            var current = _root;

            foreach (var part in parts)
            {
                var child = current.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (child == null) return null;
                current = child;
            }

            return current;
        }

        private void ParseProjectFile()
        {
            var doc = new XmlDocument();
            doc.Load(_filePath);

            // Parse the AbstractItemModelData rows structure
            var rowsNode = doc.SelectSingleNode("//Rows");
            if (rowsNode != null)
            {
                ParseRows(rowsNode, _root);
            }

            // Extract key settings
            ServerUrl = ReadSetting("/Server Test/Server URL")
                .Replace("&lt;", "<").Replace("&gt;", ">")
                .Replace("&amp;", "&");
            if (string.IsNullOrEmpty(ServerUrl))
            {
                ServerUrl = "opc.tcp://localhost:4840";
            }

            var secMode = ReadSetting("/Server Test/Session/MessageSecurityMode");
            if (int.TryParse(secMode, out int mode))
            {
                SecurityMode = mode;
            }

            var userType = ReadSetting("/Server Test/Session/UserIdentityType");
            if (int.TryParse(userType, out int ut))
            {
                UserIdentityType = ut;
            }
        }

        private void ParseRows(XmlNode rowsNode, SettingsNode parent)
        {
            var rows = rowsNode.SelectNodes("Row");
            if (rows == null) return;
            foreach (XmlNode row in rows)
            {
                // Column 0 = Name (DisplayRole)
                // Column 1 = Value (DisplayRole)
                string? name = null;
                string? value = null;
                XmlNode? childRows = null;

                var columns = row.SelectNodes("Column");
                if (columns == null) continue;
                foreach (XmlNode col in columns)
                {
                    var colIdx = col.Attributes?["column"]?.Value;
                    var displayData = col.SelectSingleNode(
                        "ItemData[@rolename='DisplayRole']/Value");
                    string? data = displayData?.Attributes?["data"]?.Value;

                    if (colIdx == "0")
                    {
                        name = data;
                        childRows = col.SelectSingleNode("ChildRows");
                    }
                    else if (colIdx == "1")
                    {
                        value = data;
                    }
                }

                if (name != null)
                {
                    var node = new SettingsNode(name)
                    {
                        Value = value,
                        ParentPath = parent.FullPath
                    };
                    parent.Children.Add(node);

                    // Recurse into child rows (from column 0's ChildRows)
                    if (childRows != null)
                    {
                        ParseRows(childRows, node);
                    }
                }
            }
        }

        /// <summary>
        /// Internal node in the settings tree.
        /// </summary>
        private sealed class SettingsNode
        {
            public string Name { get; }
            public string? Value { get; set; }
            public string ParentPath { get; set; } = "";
            public List<SettingsNode> Children { get; } = new();

            public string FullPath =>
                string.IsNullOrEmpty(ParentPath) ? $"/{Name}" : $"{ParentPath}/{Name}";

            public SettingsNode(string name) => Name = name;
        }
    }
}
