//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace MyAndromeda.Data.DataWarehouse.Models
{
    using System;
    using System.Collections.Generic;
    
    public partial class Audit
    {
        public System.Guid ID { get; set; }
        public Nullable<System.DateTime> DateCreated { get; set; }
        public string SrcID { get; set; }
        public string HardwareID { get; set; }
        public string IP_Port { get; set; }
        public string Action { get; set; }
        public Nullable<int> ResponseTime { get; set; }
        public Nullable<int> ErrorCode { get; set; }
        public string ExtraInfo { get; set; }
        public string ACSServer { get; set; }
    }
}
