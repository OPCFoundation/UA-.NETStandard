/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class ImageListControl : UserControl
    {
        public ImageListControl()
        {
            InitializeComponent();
        }
        
        public static System.Drawing.Icon AppIcon
        {
            get
            {
                if (s_AppIcon != null)
                {
                    return s_AppIcon;
                }

                string fileName = Assembly.GetEntryAssembly().Location;
                IntPtr hLibrary = Win32.LoadLibrary(fileName);

                if (hLibrary != IntPtr.Zero)
                {
                    IntPtr hIcon = Win32.LoadIcon(hLibrary, "#32512");

                    if (hIcon != IntPtr.Zero)
                    {
                        s_AppIcon = System.Drawing.Icon.FromHandle(hIcon);
                    }
                }

                return s_AppIcon;
            }
        }

        private static class Win32
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            static extern internal IntPtr LoadIcon(IntPtr hInstance, string lpIconName);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            static extern internal IntPtr LoadLibrary(string lpFileName);
        }

        public static ImageList GlobalImageList
        {
            get
            {
                if (s_GlobalImageList == null)
                {
                    var control = new ImageListControl();

                    if (!control.DesignMode)
                    {
                        s_GlobalImageList = control.ImageList;
                    }
                }

                return s_GlobalImageList;
            }
        }

        private static System.Drawing.Icon s_AppIcon;
        private static ImageList s_GlobalImageList;
    }

    public static class ImageIndex
    {
        public const int Info = 0;
        public const int Warning = 1;
        public const int Error = 2;
        public const int Begin = 3;
        public const int End = 4;
        public const int Server = 5;
        public const int LocalNetwork = 6;
        public const int GlobalNetwork = 7;
        public const int Package = 8;
        public const int Secure = 9;
        public const int InSecure = 10;
        public const int Object = 11;
        public const int Variable = 12;
        public const int Property = 13;
        public const int Method = 14;
        public const int NumberValue = 15;
        public const int StringValue = 16;
        public const int ByteStringValue = 17;
        public const int StructureValue = 19;
        public const int ArrayValue = 19;
        public const int ClosedFolder = 20;
        public const int Add = 21;
        public const int Computer = 22;
        public const int TrustedCertificate = 23;
        public const int RejectedCertificate = Error;
        public const int UntrustedCertificate = Info;
        public const int Constant = 24;
        public const int Attribute = 25;

        /// <summary>
        /// Returns an image index for the specified attribute.
        /// </summary>
        public static int Get(uint attributeId, object value)
        {
            if (attributeId == Attributes.Value)
            {
                TypeInfo typeInfo = TypeInfo.Construct(value);

                if (typeInfo.ValueRank >= 0)
                {
                    return ImageIndex.ArrayValue;
                }

                if (typeInfo.BuiltInType == BuiltInType.Variant)
                {
                    typeInfo = ((Variant)value).TypeInfo;

                    if (typeInfo == null)
                    {
                        typeInfo = TypeInfo.Construct(((Variant)value).Value);
                    }
                }

                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Number:
                    case BuiltInType.SByte:
                    case BuiltInType.Byte:
                    case BuiltInType.Int16:
                    case BuiltInType.UInt16:
                    case BuiltInType.Int32:
                    case BuiltInType.UInt32:
                    case BuiltInType.Int64:
                    case BuiltInType.UInt64:
                    case BuiltInType.Float:
                    case BuiltInType.Double:
                    case BuiltInType.Enumeration:
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Boolean:
                    {
                        return ImageIndex.NumberValue;
                    }

                    case BuiltInType.ByteString:
                    {
                        return ImageIndex.ByteStringValue;
                    }

                    case BuiltInType.ExtensionObject:
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.DataValue:
                    {
                        return ImageIndex.StructureValue;
                    }
                }

                return ImageIndex.StringValue;
            }

            return ImageIndex.Attribute;
        }
    }
}
