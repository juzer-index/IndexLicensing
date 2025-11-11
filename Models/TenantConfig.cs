using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IndexInfo.Models
{
    [Table("TenantConfigs")]
    public class TenantConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string PlantOrSite { get; set; } = string.Empty;

        [Required]
        public string AppPoolHost { get; set; } = string.Empty;

        [Required]
        public string AppPoolInstance { get ; set ; } = string.Empty;

        public string? APIKey { get; set; }

        [Required]
        public string UserID { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string HttpVerbKey { get; set; } = "https";

        [Required]
        public int TenantMasterId { get; set; }   // FK to TenantMaster.id

        [Required]
        public string Company { get; set; } = string.Empty;

        [Required]
        public bool IsActive { get; set; } = true;
    }
}
