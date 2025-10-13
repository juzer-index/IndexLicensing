using IndexInfo.Models;
using Microsoft.EntityFrameworkCore;

namespace IndexInfo.Contexts
{
    public class MainEntity : DbContext
    {
        public MainEntity(DbContextOptions<MainEntity> options) : base(options) { }

        public DbSet<ModuleMaster> moduleMasters { get; set; }
        public DbSet<License> licenses { get; set; }
        public DbSet<CompanyMaster> companyMasters { get; set; }
        public DbSet<TenantMaster> tenantMasters { get; set; }
        public DbSet<TenantCompanyMapping> tenantCompanyMappings { get; set; }
    }
}
