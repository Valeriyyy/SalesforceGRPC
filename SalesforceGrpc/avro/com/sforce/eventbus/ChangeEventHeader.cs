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
    public partial class ChangeEventHeader : global::Avro.Specific.ISpecificRecord {
        public static global::Avro.Schema _SCHEMA = global::Avro.Schema.Parse(@"{""type"":""record"",""name"":""ChangeEventHeader"",""namespace"":""com.sforce.eventbus"",""fields"":[{""name"":""entityName"",""type"":""string""},{""name"":""recordIds"",""type"":{""type"":""array"",""items"":""string""}},{""name"":""changeType"",""type"":{""type"":""enum"",""name"":""ChangeType"",""namespace"":""com.sforce.eventbus"",""symbols"":[""CREATE"",""UPDATE"",""DELETE"",""UNDELETE"",""GAP_CREATE"",""GAP_UPDATE"",""GAP_DELETE"",""GAP_UNDELETE"",""GAP_OVERFLOW""]}},{""name"":""changeOrigin"",""type"":""string""},{""name"":""transactionKey"",""type"":""string""},{""name"":""sequenceNumber"",""type"":""int""},{""name"":""commitTimestamp"",""type"":""long""},{""name"":""commitNumber"",""type"":""long""},{""name"":""commitUser"",""type"":""string""},{""name"":""nulledFields"",""type"":{""type"":""array"",""items"":""string""}},{""name"":""diffFields"",""type"":{""type"":""array"",""items"":""string""}},{""name"":""changedFields"",""type"":{""type"":""array"",""items"":""string""}}]}");
        private string _entityName;
        private IList<System.String> _recordIds;
        private com.sforce.eventbus.ChangeType _changeType;
        private string _changeOrigin;
        private string _transactionKey;
        private int _sequenceNumber;
        private long _commitTimestamp;
        private long _commitNumber;
        private string _commitUser;
        private IList<System.String> _nulledFields;
        private IList<System.String> _diffFields;
        private IList<System.String> _changedFields;
        public virtual global::Avro.Schema Schema {
            get {
                return ChangeEventHeader._SCHEMA;
            }
        }
        public string entityName {
            get {
                return this._entityName;
            }
            set {
                this._entityName = value;
            }
        }
        public IList<System.String> recordIds {
            get {
                return this._recordIds;
            }
            set {
                this._recordIds = value;
            }
        }
        public com.sforce.eventbus.ChangeType changeType {
            get {
                return this._changeType;
            }
            set {
                this._changeType = value;
            }
        }
        public string changeOrigin {
            get {
                return this._changeOrigin;
            }
            set {
                this._changeOrigin = value;
            }
        }
        public string transactionKey {
            get {
                return this._transactionKey;
            }
            set {
                this._transactionKey = value;
            }
        }
        public int sequenceNumber {
            get {
                return this._sequenceNumber;
            }
            set {
                this._sequenceNumber = value;
            }
        }
        public long commitTimestamp {
            get {
                return this._commitTimestamp;
            }
            set {
                this._commitTimestamp = value;
            }
        }
        public long commitNumber {
            get {
                return this._commitNumber;
            }
            set {
                this._commitNumber = value;
            }
        }
        public string commitUser {
            get {
                return this._commitUser;
            }
            set {
                this._commitUser = value;
            }
        }
        public IList<System.String> nulledFields {
            get {
                return this._nulledFields;
            }
            set {
                this._nulledFields = value;
            }
        }
        public IList<System.String> diffFields {
            get {
                return this._diffFields;
            }
            set {
                this._diffFields = value;
            }
        }
        public IList<System.String> changedFields {
            get {
                return this._changedFields;
            }
            set {
                this._changedFields = value;
            }
        }
        public virtual object Get(int fieldPos) {
            switch (fieldPos) {
                case 0: return this.entityName;
                case 1: return this.recordIds;
                case 2: return this.changeType;
                case 3: return this.changeOrigin;
                case 4: return this.transactionKey;
                case 5: return this.sequenceNumber;
                case 6: return this.commitTimestamp;
                case 7: return this.commitNumber;
                case 8: return this.commitUser;
                case 9: return this.nulledFields;
                case 10: return this.diffFields;
                case 11: return this.changedFields;
                default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Get()");
            };
        }
        public virtual void Put(int fieldPos, object fieldValue) {
            switch (fieldPos) {
                case 0: this.entityName = (System.String)fieldValue; break;
                case 1: this.recordIds = (IList<System.String>)fieldValue; break;
                case 2: this.changeType = (com.sforce.eventbus.ChangeType)fieldValue; break;
                case 3: this.changeOrigin = (System.String)fieldValue; break;
                case 4: this.transactionKey = (System.String)fieldValue; break;
                case 5: this.sequenceNumber = (System.Int32)fieldValue; break;
                case 6: this.commitTimestamp = (System.Int64)fieldValue; break;
                case 7: this.commitNumber = (System.Int64)fieldValue; break;
                case 8: this.commitUser = (System.String)fieldValue; break;
                case 9: this.nulledFields = (IList<System.String>)fieldValue; break;
                case 10: this.diffFields = (IList<System.String>)fieldValue; break;
                case 11: this.changedFields = (IList<System.String>)fieldValue; break;
                default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            };
        }
    }
}