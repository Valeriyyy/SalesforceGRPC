// ------------------------------------------------------------------------------
// <auto-generated>
//    Generated by avrogen, version 1.11.1
//    Changes to this file may cause incorrect behavior and will be lost if code
//    is regenerated
// </auto-generated>
// ------------------------------------------------------------------------------
namespace com.sforce.eventbus {
    using System;
    using System.Collections.Generic;
    using System.Text;
    using global::Avro;
    using global::Avro.Specific;

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("avrogen", "1.11.1")]
    public partial class Location : global::Avro.Specific.ISpecificRecord {
        public static global::Avro.Schema _SCHEMA = global::Avro.Schema.Parse(@"{""type"":""record"",""name"":""Location"",""namespace"":""com.sforce.eventbus"",""fields"":[{""name"":""Latitude"",""default"":null,""type"":[""null"",""double""]},{""name"":""Longitude"",""default"":null,""type"":[""null"",""double""]},{""name"":""XyzEncoded"",""default"":null,""type"":[""null"",""string""]}]}");
        private System.Nullable<System.Double> _Latitude;
        private System.Nullable<System.Double> _Longitude;
        private string _XyzEncoded;
        public virtual global::Avro.Schema Schema {
            get {
                return Location._SCHEMA;
            }
        }
        public System.Nullable<System.Double> Latitude {
            get {
                return this._Latitude;
            }
            set {
                this._Latitude = value;
            }
        }
        public System.Nullable<System.Double> Longitude {
            get {
                return this._Longitude;
            }
            set {
                this._Longitude = value;
            }
        }
        public string XyzEncoded {
            get {
                return this._XyzEncoded;
            }
            set {
                this._XyzEncoded = value;
            }
        }
        public virtual object Get(int fieldPos) {
            switch (fieldPos) {
                case 0: return this.Latitude;
                case 1: return this.Longitude;
                case 2: return this.XyzEncoded;
                default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Get()");
            };
        }
        public virtual void Put(int fieldPos, object fieldValue) {
            switch (fieldPos) {
                case 0: this.Latitude = (System.Nullable<System.Double>)fieldValue; break;
                case 1: this.Longitude = (System.Nullable<System.Double>)fieldValue; break;
                case 2: this.XyzEncoded = (System.String)fieldValue; break;
                default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            };
        }
    }
}