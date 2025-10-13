using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IndexInfo.Models
{
    [Table("TenantMaster")]
    public class TenantMaster
    {
        [Key]
        public int id { get; set; }
        public string TenantID { get; set; }
        public string TenantName { get; set; }
        public string TenantMailId { get; set; }
        public string TenantPhoneNumber { get; set; }
        public int Active { get; set; }
        public DateTime? ExpiredOn { get; set; }
        public bool IsDefault { get; set; }   // <-- must exist because SQL requires it

    }
}
