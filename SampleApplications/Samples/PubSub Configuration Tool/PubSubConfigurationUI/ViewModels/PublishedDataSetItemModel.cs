using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class PublishedDataItemSetModel : BaseViewModel
    {
        #region Private Member 

        private string _Attribute = "Value";
        private string _DeadbandType = "0";
        private double _DeadbandValue;
        private PublishedDataSetItemDefinition _Definition;
        private QualifiedNameCollection _FieldMetaDataProperties = new QualifiedNameCollection( );
        private string _Indexrange = string.Empty;
        private bool _IsEnabled;
        private double _SamplingInterval = -1;
        private string _SubstituteValue = "0";

        #endregion

        #region Private Property

        private string _PublishVariable { get; set; }

        private string _PublishVariableNodeId { get; set; }

        #endregion

        #region Private Methods

        private List< FieldInfo > GetFields( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.ToList( );
        }

        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        #endregion

        #region Constructors

        public PublishedDataItemSetModel( )
        {
            Definition = new PublishedDataSetItemDefinition( null );
            //PublishedDataSetCollection = new ObservableCollection<PublishedDataSetBase>();
        }

        #endregion

        #region Public Property

        public PublishedDataSetItemDefinition Definition
        {
            get { return _Definition; }
            set
            {
                _Definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        public bool IsEnabled
        {
            get { return _IsEnabled; }
            set
            {
                _IsEnabled = value;
                OnPropertyChanged( "IsEnabled" );
            }
        }

        public string PublishVariable
        {
            get { return _PublishVariable; }
            set
            {
                _PublishVariable = value;
                OnPropertyChanged( "PublishVariable" );
            }
        }

        public string PublishVariableNodeId
        {
            get { return _PublishVariableNodeId; }
            set
            {
                _PublishVariableNodeId = value;
                OnPropertyChanged( "PublishVariableNodeId" );
            }
        }

        public string Attribute
        {
            get { return _Attribute; }
            set
            {
                _Attribute = value;
                OnPropertyChanged( "Attribute" );
            }
        }

        public double SamplingInterval
        {
            get { return _SamplingInterval; }
            set
            {
                _SamplingInterval = value;
                OnPropertyChanged( "SamplingInterval" );
            }
        }

        public string DeadbandType
        {
            get { return _DeadbandType; }
            set
            {
                _DeadbandType = value;
                OnPropertyChanged( "DeadbandType" );
            }
        }

        public double DeadbandValue
        {
            get { return _DeadbandValue; }
            set
            {
                _DeadbandValue = value;
                OnPropertyChanged( "DeadbandValue" );
            }
        }

        public string Indexrange
        {
            get { return _Indexrange; }
            set
            {
                _Indexrange = value;
                OnPropertyChanged( "Indexrange" );
            }
        }

        public string SubstituteValue
        {
            get { return _SubstituteValue; }
            set
            {
                _SubstituteValue = value;
                OnPropertyChanged( "SubstituteValue" );
            }
        }

        public QualifiedNameCollection FieldMetaDataProperties
        {
            get { return _FieldMetaDataProperties; }
            set
            {
                _FieldMetaDataProperties = value;
                OnPropertyChanged( "FieldMetaDataProperties" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            PublishVariable = Definition.Name;
            PublishVariableNodeId = Definition.PublishVariableNodeId.ToString( );
            Attribute = GetFields( typeof( Attributes ) )
            .Where( i => Convert.ToUInt16( i.GetValue( i ) ) == Definition.Attribute ).FirstOrDefault( ).Name;
            SamplingInterval = Definition.SamplingInterval;
            DeadbandType = GetConstants( typeof( DeadbandType ) )
            .Where( i => Convert.ToInt16( i.GetValue( i ) ) == Definition.DeadbandType ).FirstOrDefault( ).Name;
            DeadbandValue = Definition.DeadbandValue;
            Indexrange = Definition.Indexrange;
            SubstituteValue = Definition.SubstituteValue.Value != null
                ? Convert.ToString( Definition.SubstituteValue.Value ) : string.Empty;
            FieldMetaDataProperties = Definition.FieldMetaDataProperties;
        }

        #endregion
    }
}