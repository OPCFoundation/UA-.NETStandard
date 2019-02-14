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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Opc.Ua.Com
{
    #region PropertyIds Class
    /// <summary>
	/// Defines constants for all DA property Ids.
	/// </summary>
	public class PropertyIds
	{
		/// <remarks/>
		public const int DataType = 1;
		/// <remarks/>
		public const int Value = 2;
		/// <remarks/>    
		public const int Quality = 3;
		/// <remarks/>
		public const int Timestamp = 4;
		/// <remarks/>
		public const int AccessRights = 5;
		/// <remarks/>
		public const int ScanRate = 6;
		/// <remarks/>
		public const int EuType = 7;
		/// <remarks/>
		public const int EuInfo = 8;
		/// <remarks/>
		public const int EngineeringUnits = 100;
		/// <remarks/>
		public const int Description = 101; 
		/// <remarks/>
		public const int HighEU = 102; 
		/// <remarks/>
		public const int LowEU = 103; 
		/// <remarks/>
		public const int HighIR = 104;
		/// <remarks/>
		public const int LowIR = 105;
		/// <remarks/>
		public const int CloseLabel = 106;
		/// <remarks/>
		public const int OpenLabel = 107;
		/// <remarks/>
		public const int TimeZone = 108;
		/// <remarks/>
		public const int ConditionStatus = 300;
		/// <remarks/>
		public const int AlarmQuickHelp = 301;
		/// <remarks/>
		public const int AlarmAreaList = 302;
		/// <remarks/>
		public const int PrimaryAlarmArea = 303;
		/// <remarks/>
		public const int ConditionLogic = 304;
		/// <remarks/>
		public const int LimitExceeded = 305;
		/// <remarks/>
		public const int Deadband = 306;
		/// <remarks/>
		public const int HiHiLimit = 307;
		/// <remarks/>
		public const int HiLimit = 308;
		/// <remarks/>
		public const int LoLimit = 309;
		/// <remarks/>
		public const int LoLoLimit = 310;
		/// <remarks/>
		public const int RateChangeLimit = 311;
		/// <remarks/>
		public const int DeviationLimit = 312;
		/// <remarks/>
		public const int SoundFile = 313;
		/// <remarks/>
		public const int TypeSystemId = 600;
		/// <remarks/>
		public const int DictionaryId = 601;
		/// <remarks/>
		public const int TypeId = 602;
		/// <remarks/>
		public const int Dictionary = 603;
		/// <remarks/>
		public const int TypeDescription = 604;
		/// <remarks/>
		public const int ConsistencyWindow = 605;
		/// <remarks/>
		public const int WriteBehavoir = 606;
		/// <remarks/>
		public const int UnconvertedItemId = 607;
		/// <remarks/>
		public const int UnfilteredItemId = 608;
		/// <remarks/>
        public const int DataFilterValue = 609;
        /// <remarks/>
        public const int UaBuiltInType = 610;
        /// <remarks/>
        public const int UaDataTypeId = 611;
        /// <remarks/>
        public const int UaValueRank = 612;
        /// <remarks/>
        public const int UaBrowseName = 613;
        /// <remarks/>
        public const int UaDescription = 614;
		/// <remarks/>
		public const int VendorDefined = 5000;
        
        //Ravil 17 Sep 2008: HDA Attributes (5.2 of OPC HDA 1.20 Spec)
        /// <remarks/>
        public const int Stepped = 700;
        /// <remarks/>
        public const int Archiving = 701;
        /// <remarks/>
        public const int DeriveEquation = 702;
        /// <remarks/>
        public const int NodeName = 703;
        /// <remarks/>
        public const int ProcessName = 704;
        /// <remarks/>
        public const int SourceName = 705;
        /// <remarks/>
        public const int SourceType = 706;
        /// <remarks/>
        public const int NormalMaximum = 707;
        /// <remarks/>
        public const int NormalMinimum = 708;
        /// <remarks/>
        public const int ItemId = 709;
        /// <remarks/>
        public const int MaxTimeInt = 710;
        /// <remarks/>
        public const int MinTimeInt = 711;
        /// <remarks/>
        public const int ExceptionDev = 712;
        /// <remarks/>
        public const int ExceptionDevType = 713;
        /// <remarks/>
        public const int HighEntryLimit = 714;
        /// <remarks/>
        public const int LowEntryLimit = 715;

		/// <summary>
		/// Returns the UA data type for the specified property.
		/// </summary>
		public static NodeId GetDataTypeId(int propertyId)
		{
			switch (propertyId)
			{
				case PropertyIds.DataType:          { return DataTypes.NodeId;   }
				case PropertyIds.Value:             { return DataTypes.BaseDataType;  }
				case PropertyIds.Quality:           { return DataTypes.UInt16;   }
				case PropertyIds.Timestamp:         { return DataTypes.DateTime; }
				case PropertyIds.AccessRights:      { return DataTypes.Int32;    }
				case PropertyIds.ScanRate:          { return DataTypes.Float;    }
				case PropertyIds.EuType:            { return DataTypes.Int32;    }
				case PropertyIds.EuInfo:            { return DataTypes.String;   }
				case PropertyIds.EngineeringUnits:  { return DataTypes.String;   }
				case PropertyIds.Description:       { return DataTypes.String;   }
				case PropertyIds.HighEU:            { return DataTypes.Double;   }
				case PropertyIds.LowEU:             { return DataTypes.Double;   }
				case PropertyIds.HighIR:            { return DataTypes.Double;   }
				case PropertyIds.LowIR:             { return DataTypes.Double;   }
				case PropertyIds.CloseLabel:        { return DataTypes.String;   }
				case PropertyIds.OpenLabel:         { return DataTypes.String;   }
				case PropertyIds.TimeZone:          { return DataTypes.Int32;    }
				case PropertyIds.ConditionStatus:   { return DataTypes.String;   }
				case PropertyIds.AlarmQuickHelp:    { return DataTypes.String;   }
				case PropertyIds.AlarmAreaList:     { return DataTypes.String;   }
				case PropertyIds.PrimaryAlarmArea:  { return DataTypes.String;   }
				case PropertyIds.ConditionLogic:    { return DataTypes.String;   }
				case PropertyIds.LimitExceeded:     { return DataTypes.String;   }
				case PropertyIds.Deadband:          { return DataTypes.Double;   }
				case PropertyIds.HiHiLimit:         { return DataTypes.Double;   }
				case PropertyIds.HiLimit:           { return DataTypes.Double;   }
				case PropertyIds.LoLimit:           { return DataTypes.Double;   }
				case PropertyIds.LoLoLimit:         { return DataTypes.Double;   }
				case PropertyIds.RateChangeLimit:   { return DataTypes.Double;   }
				case PropertyIds.DeviationLimit:    { return DataTypes.Double;   }
				case PropertyIds.SoundFile:         { return DataTypes.String;   }
				case PropertyIds.TypeSystemId:      { return DataTypes.String;   }
				case PropertyIds.DictionaryId:      { return DataTypes.String;   }
				case PropertyIds.TypeId:            { return DataTypes.String;   }
				case PropertyIds.Dictionary:        { return DataTypes.String;   }
				case PropertyIds.TypeDescription:   { return DataTypes.String;   }
				case PropertyIds.ConsistencyWindow: { return DataTypes.String;   }
				case PropertyIds.WriteBehavoir:     { return DataTypes.String;   }
				case PropertyIds.UnconvertedItemId: { return DataTypes.String;   }
				case PropertyIds.UnfilteredItemId:  { return DataTypes.String;   }
                case PropertyIds.DataFilterValue:   { return DataTypes.String;   }
                case PropertyIds.UaBuiltInType:     { return DataTypes.Int32;    }
                case PropertyIds.UaDataTypeId:      { return DataTypes.String;   }
                case PropertyIds.UaValueRank:       { return DataTypes.Int32;    }
                case PropertyIds.UaBrowseName:      { return DataTypes.String;   }
                case PropertyIds.UaDescription:     { return DataTypes.String;   }
			}

			return DataTypes.BaseDataType;
		}
        
		/// <summary>
		/// Returns the VARTYPE for the specified property.
		/// </summary>
		public static VarEnum GetVarType(int propertyId)
		{
			switch (propertyId)
			{
				case PropertyIds.DataType:          { return VarEnum.VT_I2;      }
				case PropertyIds.Value:             { return VarEnum.VT_VARIANT; }
				case PropertyIds.Quality:           { return VarEnum.VT_I2;      }
				case PropertyIds.Timestamp:         { return VarEnum.VT_DATE;    }  
				case PropertyIds.AccessRights:      { return VarEnum.VT_I4;      }
				case PropertyIds.ScanRate:          { return VarEnum.VT_R4;      }
				case PropertyIds.EuType:            { return VarEnum.VT_I4;      }
				case PropertyIds.EuInfo:            { return VarEnum.VT_BSTR | VarEnum.VT_ARRAY; }
				case PropertyIds.EngineeringUnits:  { return VarEnum.VT_BSTR;    }
				case PropertyIds.Description:       { return VarEnum.VT_BSTR;    }
				case PropertyIds.HighEU:            { return VarEnum.VT_R8;      }
				case PropertyIds.LowEU:             { return VarEnum.VT_R8;      }
				case PropertyIds.HighIR:            { return VarEnum.VT_R8;      }
				case PropertyIds.LowIR:             { return VarEnum.VT_R8;      }
				case PropertyIds.CloseLabel:        { return VarEnum.VT_BSTR;    }
				case PropertyIds.OpenLabel:         { return VarEnum.VT_BSTR;    }
				case PropertyIds.TimeZone:          { return VarEnum.VT_I4;      }
				case PropertyIds.ConditionStatus:   { return VarEnum.VT_BSTR;    }
				case PropertyIds.AlarmQuickHelp:    { return VarEnum.VT_BSTR;    }
				case PropertyIds.AlarmAreaList:     { return VarEnum.VT_BSTR;    }
				case PropertyIds.PrimaryAlarmArea:  { return VarEnum.VT_BSTR;    }
				case PropertyIds.ConditionLogic:    { return VarEnum.VT_BSTR;    }
				case PropertyIds.LimitExceeded:     { return VarEnum.VT_BSTR;    }
				case PropertyIds.Deadband:          { return VarEnum.VT_R8;      }
				case PropertyIds.HiHiLimit:         { return VarEnum.VT_R8;      }
				case PropertyIds.HiLimit:           { return VarEnum.VT_R8;      }
				case PropertyIds.LoLimit:           { return VarEnum.VT_R8;      }
				case PropertyIds.LoLoLimit:         { return VarEnum.VT_R8;      }
				case PropertyIds.RateChangeLimit:   { return VarEnum.VT_R8;      }
				case PropertyIds.DeviationLimit:    { return VarEnum.VT_R8;      }
				case PropertyIds.SoundFile:         { return VarEnum.VT_BSTR;    }
				case PropertyIds.TypeSystemId:      { return VarEnum.VT_BSTR;    }
				case PropertyIds.DictionaryId:      { return VarEnum.VT_BSTR;    }
				case PropertyIds.TypeId:            { return VarEnum.VT_BSTR;    }
				case PropertyIds.Dictionary:        { return VarEnum.VT_BSTR;    }
				case PropertyIds.TypeDescription:   { return VarEnum.VT_BSTR;    }
				case PropertyIds.ConsistencyWindow: { return VarEnum.VT_BSTR;    }
				case PropertyIds.WriteBehavoir:     { return VarEnum.VT_BSTR;    }
				case PropertyIds.UnconvertedItemId: { return VarEnum.VT_BSTR;    }
				case PropertyIds.UnfilteredItemId:  { return VarEnum.VT_BSTR;    }
				case PropertyIds.DataFilterValue:   { return VarEnum.VT_BSTR;    }
                case PropertyIds.UaBuiltInType:     { return VarEnum.VT_I4;      }
                case PropertyIds.UaDataTypeId:      { return VarEnum.VT_BSTR;    }
                case PropertyIds.UaValueRank:       { return VarEnum.VT_I4;      }
                case PropertyIds.UaBrowseName:      { return VarEnum.VT_BSTR;    }
                case PropertyIds.UaDescription:     { return VarEnum.VT_BSTR;    }
			}

			return VarEnum.VT_EMPTY;
		}

		/// <summary>
		/// Returns the UA array size for the specified property.
		/// </summary>
		public static int GetValueRank(int propertyId)
		{
			switch (propertyId)
			{
				case PropertyIds.EuInfo:        
				case PropertyIds.AlarmAreaList:
				{
					return ValueRanks.OneDimension;
				}
			}

			return -1;
		}

		/// <summary>
		/// Returns the UA browse name for the specified property.
		/// </summary>
		public static string GetBrowseName(int propertyId)
		{		
			// check for non-standard properties.
            if (propertyId < 0 || propertyId > PropertyIds.VendorDefined)
			{
				return null;
			}

			// use reflection to find the browse name.
			FieldInfo[] fields = typeof(PropertyIds).GetFields(BindingFlags.Static | BindingFlags.Public);

			for (int ii = 0; ii < fields.Length; ii++)
			{
				if ((int)fields[ii].GetValue(typeof(PropertyIds)) == propertyId)
				{
					return fields[ii].Name;
				}
			}

			// property not found.
			return null;
		}

		/// <summary>
		/// Returns the UA description for the specified property.
		/// </summary>
		public static string GetDescription(int propertyId)
		{		
			switch (propertyId)
			{
				case PropertyIds.DataType:          { return "Item Canonical DataType"; }
				case PropertyIds.Value:             { return "Item Value";              }
				case PropertyIds.Quality:           { return "Item Quality";            }
				case PropertyIds.Timestamp:         { return "Item Timestamp";          }
				case PropertyIds.AccessRights:      { return "Item Access Rights";      }
				case PropertyIds.ScanRate:          { return "Server Scan Rate";        }
				case PropertyIds.EuType:            { return "Item EU Type";            }
				case PropertyIds.EuInfo:            { return "Item EU Info";            }
				case PropertyIds.EngineeringUnits:  { return "EU Units";                }
				case PropertyIds.Description:       { return "Item Description";        }
				case PropertyIds.HighEU:            { return "High EU";                 }
				case PropertyIds.LowEU:             { return "Low EU";                  }
				case PropertyIds.HighIR:            { return "High Instrument Range";   }
				case PropertyIds.LowIR:             { return "Low Instrument Range";    }
				case PropertyIds.CloseLabel:        { return "Contact Close Label";     }
				case PropertyIds.OpenLabel:         { return "Contact Open Label";      }
				case PropertyIds.TimeZone:          { return "Timezone";                }
				case PropertyIds.ConditionStatus:   { return "Condition ErrorId";        }
				case PropertyIds.AlarmQuickHelp:    { return "Alarm Quick Help";        }
				case PropertyIds.AlarmAreaList:     { return "Alarm Area List";         }
				case PropertyIds.PrimaryAlarmArea:  { return "Primary Alarm Area";      }
				case PropertyIds.ConditionLogic:    { return "Condition Logic";         }
				case PropertyIds.LimitExceeded:     { return "Limit Exceeded";          }
				case PropertyIds.Deadband:          { return "Deadband";                }
				case PropertyIds.HiHiLimit:         { return "HiHi Limit";              }
				case PropertyIds.HiLimit:           { return "Hi Limit";                }
				case PropertyIds.LoLimit:           { return "Lo Limit";                }
				case PropertyIds.LoLoLimit:         { return "LoLo Limit";              }
				case PropertyIds.RateChangeLimit:   { return "Rate of Change Limit";    }
				case PropertyIds.DeviationLimit:    { return "Deviation Limit";         }
				case PropertyIds.SoundFile:         { return "Sound File";              }
				case PropertyIds.TypeSystemId:      { return "Type System Id";          }
				case PropertyIds.DictionaryId:      { return "Dictionary Id";           }
				case PropertyIds.TypeId:            { return "Type Id";                 }
				case PropertyIds.Dictionary:        { return "Dictionary";              }
				case PropertyIds.TypeDescription:   { return "Type Description";        }
				case PropertyIds.ConsistencyWindow: { return "Consistency Window";      }
				case PropertyIds.WriteBehavoir:     { return "Write Behavior";          }
				case PropertyIds.UnconvertedItemId: { return "Unconverted Item Id";     }
				case PropertyIds.UnfilteredItemId:  { return "Unfiltered Item Id";      }
				case PropertyIds.DataFilterValue:   { return "Data Filter Value";       }
                case PropertyIds.UaBuiltInType:     { return "UA BuiltIn Type";         }
                case PropertyIds.UaDataTypeId:      { return "UA Data Type Id";         }
                case PropertyIds.UaValueRank:       { return "UA Value Rank";           }
                case PropertyIds.UaBrowseName:      { return "UA Browse Name";          }
                case PropertyIds.UaDescription:     { return "UA Description";          }
			}

			return null;
		}
    }
    #endregion
}
