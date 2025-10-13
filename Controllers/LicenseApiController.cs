using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IndexInfo.Contexts;                   // MainEntity DbContext
using IndexInfo.Models;                     // License + TenantMaster + TenantConfig
using IndexInfo.LicenseHandler;             // LicenseKey.GenerateXMLLicenseFile
using IndexLicenseKeyGenerator;
using Microsoft.AspNetCore.Hosting;

namespace IndexInfo.Controllers
{
    [ApiController]
    [Route("api/license")]
    public class LicenseApiController : ControllerBase
    {
        private readonly MainEntity _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LicenseApiController> _log;

        public LicenseApiController(MainEntity db, IWebHostEnvironment env, ILogger<LicenseApiController> log)
        {
            _db = db;
            _env = env;
            _log = log;
        }

        // ---------- Request/Response DTOs ----------

        public sealed class TenantConfigDto
        {
            public string PlantOrSite { get; set; } = "";
            public string AppPoolHost { get; set; } = "";
            public string AppPoolInstance { get; set; } = "";
            public string? APIKey { get; set; }
            public string UserID { get; set; } = "";
            public string Password { get; set; } = "";
            public string HttpVerbKey { get; set; } = "https";
            public string Company { get; set; } = "";
            public bool IsActive { get; set; } = true;
        }

        public sealed class GenerateForTenantRequest
        {
            public string TenantId { get; set; } = default!;
            public string? TenantName { get; set; }
            public int? Days { get; set; }              // default 365
            public int? Utilization { get; set; }       // default 1
            public string? LicenseType { get; set; }    // default "Full"
            public string? Domain { get; set; }
            public string? ModulesCsv { get; set; }     // e.g. "CAD,Reports"
            public TenantConfigDto? Config { get; set; }
            public bool? IsDefault { get; set; }        // maps to TenantMaster.IsDefault
        }

        public sealed class GenerateForTenantResponse
        {
            public string TenantId { get; set; } = default!;
            public string ProductId { get; set; } = default!;
            public string Xml { get; set; } = default!;
            public string DownloadPath { get; set; } = default!;
            public DateTime ExpiresOn { get; set; }
            public string PublicKey { get; set; } = default!;
            public int TenantMasterId { get; set; }
            public int? TenantConfigId { get; set; }
            public string? Warning { get; set; }        // set if we skipped config due to timeout
        }

        // ---------- Endpoint ----------

        [HttpPost("generate-for-tenant")]
        public IActionResult GenerateForTenant([FromBody] GenerateForTenantRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.TenantId))
                return BadRequest("TenantId is required.");

            var sw = Stopwatch.StartNew();
            _db.Database.SetCommandTimeout(15); // fail fast on blocked DB ops

            // defaults
            int days = req.Days ?? 365;
            int utilization = req.Utilization ?? 1;
            string licenseType = string.IsNullOrWhiteSpace(req.LicenseType) ? "Standard" : req.LicenseType.Trim();
            string modules = req.ModulesCsv ?? string.Empty;
            string domain = req.Domain ?? string.Empty;
            string tenantName = string.IsNullOrWhiteSpace(req.TenantName) ? req.TenantId : req.TenantName.Trim();

            // unique ProductID
            string productId;
            do { productId = RandomString(18, false); }
            while (_db.licenses.Any(l => l.ProductID == productId));

            // if true, continue even if TenantConfig insert times out (and add a warning)
            const bool ContinueIfConfigTimesOut = true;
            string? warning = null;

            try
            {
                _log.LogInformation("STEP 1: Upsert TenantMaster start");
                var expiresOn = DateTime.Now.AddDays(days);
                var tenant = _db.tenantMasters.FirstOrDefault(t => t.TenantID == req.TenantId);

                if (tenant == null)
                {
                    tenant = new TenantMaster
                    {
                        TenantID = req.TenantId,
                        TenantName = tenantName,
                        TenantMailId = string.Empty,
                        TenantPhoneNumber = string.Empty,
                        Active = 1,
                        ExpiredOn = expiresOn,
                        IsDefault = req.IsDefault ?? false
                    };
                    _db.tenantMasters.Add(tenant);
                    _db.SaveChanges(); // get tenant.id
                }
                else
                {
                    tenant.TenantName = string.IsNullOrWhiteSpace(req.TenantName) ? tenant.TenantName : tenantName;
                    tenant.Active = 1;
                    tenant.ExpiredOn = expiresOn;
                    tenant.IsDefault = req.IsDefault ?? tenant.IsDefault; // keep old if not provided
                    _db.SaveChanges();
                }
                _log.LogInformation("STEP 1 done in {ms} ms (TenantMasterId: {id})", sw.ElapsedMilliseconds, tenant.id);

                // STEP 2: Create TenantConfig (EF) if provided
                int? tenantConfigId = null;
                if (req.Config != null)
                {
                    _log.LogInformation("STEP 2: Insert TenantConfig start");
                    try
                    {
                        var cfg = new TenantConfig
                        {
                            PlantOrSite = req.Config.PlantOrSite ?? string.Empty,
                            AppPoolHost = req.Config.AppPoolHost ?? string.Empty,
                            AppPoolInstance = req.Config.AppPoolInstance ?? string.Empty,
                            APIKey = string.IsNullOrWhiteSpace(req.Config.APIKey) ? null : req.Config.APIKey,
                            UserID = req.Config.UserID ?? string.Empty,
                            Password = req.Config.Password ?? string.Empty,
                            HttpVerbKey = string.IsNullOrWhiteSpace(req.Config.HttpVerbKey) ? "https" : req.Config.HttpVerbKey!,
                            TenantMasterId = tenant.id,
                            Company = req.Config.Company ?? string.Empty,
                            IsActive = req.Config.IsActive
                        };

                        _db.tenantConfigs.Add(cfg);
                        _db.SaveChanges();
                        tenantConfigId = cfg.Id;
                        _log.LogInformation("STEP 2 done in {ms} ms (TenantConfigId: {id})", sw.ElapsedMilliseconds, tenantConfigId);
                    }
                    catch (DbUpdateException dbEx)
                    {
                        _log.LogError(dbEx, "TenantConfig insert failed after {ms} ms", sw.ElapsedMilliseconds);
                        if (!ContinueIfConfigTimesOut) throw;
                        warning = "TenantConfig insert failed or timed out; license was still generated.";
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "TenantConfig insert failed after {ms} ms", sw.ElapsedMilliseconds);
                        if (!ContinueIfConfigTimesOut) throw;
                        warning = "TenantConfig insert failed; license was still generated.";
                    }
                }

                // STEP 3: Generate key pair & license text
                _log.LogInformation("STEP 3: Generate license start");
                var keyPair = IndexLicenseKeyGenerator.LicenseKey.GenerateKeyPair();
                string privateKey = "", publicKey = "";
                foreach (var kv in keyPair) { privateKey = kv.Key; publicKey = kv.Value; break; }

                var licenseDetails = IndexLicenseKeyGenerator.LicenseKey.GenerateLicense(
                    privateKey,
                    tenantName,
                    "",           // machine id (blank)
                    days,
                    licenseType,
                    utilization,
                    productId,
                    domain,
                    modules,
                    req.TenantId,
                    DateTime.Now.AddDays(days)
                );
                _log.LogInformation("STEP 3 done in {ms} ms", sw.ElapsedMilliseconds);

                // STEP 4: Write XML (make sure wwwroot exists)
                _log.LogInformation("STEP 4: Write XML start");
                var webRoot = _env.WebRootPath;
                if (string.IsNullOrWhiteSpace(webRoot))
                {
                    webRoot = Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), "wwwroot");
                }
                var downloads = Path.Combine(webRoot, "Downloads");
                Directory.CreateDirectory(downloads);
                var fileName = $"{productId}.xml";
                LicenseKey.GenerateXMLLicenseFile(licenseDetails, downloads, fileName);
                _log.LogInformation("STEP 4 done in {ms} ms", sw.ElapsedMilliseconds);

                // STEP 5: Insert license rows
                _log.LogInformation("STEP 5: Insert license rows start");
                var moduleList = string.IsNullOrWhiteSpace(modules)
                    ? new[] { "" }  // keep your original behavior
                    : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var m in moduleList)
                {
                    _db.licenses.Add(new License
                    {
                        ProductID = productId,
                        TenantId = req.TenantId,
                        ValidDays = days,
                        Utilization = utilization,
                        LicenseType = licenseType,
                        Module = m,
                        GenarationOn = DateTime.Now,
                        ExpiredOn = DateTime.Now.AddDays(days),
                        Domain = domain
                    });
                }
                _db.SaveChanges();
                _log.LogInformation("STEP 5 done in {ms} ms", sw.ElapsedMilliseconds);

                // Return payload
                var xml = System.IO.File.ReadAllText(Path.Combine(downloads, fileName));
                return Ok(new GenerateForTenantResponse
                {
                    TenantId = req.TenantId,
                    ProductId = productId,
                    Xml = xml,
                    DownloadPath = $"/Downloads/{fileName}",
                    ExpiresOn = DateTime.Now.AddDays(days),
                    PublicKey = publicKey,
                    TenantMasterId = tenant.id,
                    TenantConfigId = tenantConfigId,
                    Warning = warning
                });
            }
            catch (DbUpdateException dbEx)
            {
                var msg = dbEx.InnerException?.Message ?? dbEx.Message;
                _log.LogError(dbEx, "DB error after {ms} ms: {msg}", sw.ElapsedMilliseconds, msg);
                return Problem(title: "Database save failed", detail: msg, statusCode: 500);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error after {ms} ms", sw.ElapsedMilliseconds);
                return Problem(title: "License generation failed", detail: ex.Message, statusCode: 500);
            }
        }
        // -------------------- READ ENDPOINTS --------------------

        [HttpGet("licenses")]
        public IActionResult GetLicenses([FromQuery] string? tenantId = null)
        {
            var query = _db.licenses.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(tenantId))
                query = query.Where(l => l.TenantId == tenantId);

            var items = query
                .OrderByDescending(l => l.GenarationOn)
                .Select(l => new
                {
                    l.Id,
                    l.ProductID,
                    l.TenantId,
                    l.Module,
                    l.LicenseType,
                    l.Utilization,
                    l.ValidDays,
                    l.Domain,
                    l.GenarationOn,
                    l.ExpiredOn
                })
                .ToList();

            return Ok(items);
        }

        [HttpGet("tenant-configs")]
        public IActionResult GetTenantConfigs([FromQuery] string? tenantId = null)
        {
            // Join configs to TenantMaster so we can optionally filter by string TenantID
            var query =
                from cfg in _db.tenantConfigs.AsNoTracking()
                join t in _db.tenantMasters.AsNoTracking()
                    on cfg.TenantMasterId equals t.id
                select new
                {
                    cfg.Id,
                    TenantMasterId = t.id,
                    TenantId = t.TenantID,
                    TenantName = t.TenantName,
                    cfg.PlantOrSite,
                    cfg.AppPoolHost,
                    cfg.AppPoolInstance,
                    cfg.APIKey,
                    cfg.UserID,
                    cfg.Password,
                    cfg.HttpVerbKey,
                    cfg.Company,
                    cfg.IsActive
                };

            if (!string.IsNullOrWhiteSpace(tenantId))
                query = query.Where(x => x.TenantId == tenantId);

            var items = query
                .OrderByDescending(x => x.Id)
                .ToList();

            return Ok(items);
        }


        // ---------- Helpers ----------

        private static string RandomString(int size, bool lowerCase = false)
        {
            var builder = new StringBuilder(size);
            char offset = lowerCase ? 'a' : 'A';
            const int lettersOffset = 26;
            var rnd = new Random();
            for (int i = 0; i < size; i++)
            {
                var ch = (char)rnd.Next(offset, offset + lettersOffset);
                builder.Append(ch);
            }
            return lowerCase ? builder.ToString().ToLower() : builder.ToString();
        }
        

    }
}
