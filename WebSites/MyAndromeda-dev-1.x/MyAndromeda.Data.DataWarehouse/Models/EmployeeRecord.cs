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
    
    public partial class EmployeeRecord
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public EmployeeRecord()
        {
            this.EmployeeStoreLinkRecords = new HashSet<EmployeeStoreLinkRecord>();
        }
    
        public System.Guid Id { get; set; }
        public string Name { get; set; }
        public string Data { get; set; }
        public bool Deleted { get; set; }
        public System.DateTime CreatedUtc { get; set; }
        public System.DateTime LastUpdatedUtc { get; set; }
    
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<EmployeeStoreLinkRecord> EmployeeStoreLinkRecords { get; set; }
    }
}
