using IndexInfo.Models;
using Microsoft.EntityFrameworkCore;

namespace IndexInfo.Contexts
{
    public class MainEntity:DbContext
    {
        public MainEntity(DbContextOptions<MainEntity> option) :base(option)
        {

        }

        public DbSet<ModuleMaster> moduleMasters { get; set; }
        public DbSet<License> licenses { get; set; }
        public DbSet<CompanyMaster> companyMasters { get; set; }
        public DbSet<TenantMaster> tenantMasters { get; set; }
        public DbSet<TenantConfig> tenantConfigs { get; set; }

        public DbSet<TenantCompanyMapping> tenantCompanyMappings { get; set; }
        




        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            optionsBuilder.UseSqlServer(connectionString);
        }
    }
}
