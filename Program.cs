using IndexInfo.Contexts;
using Microsoft.EntityFrameworkCore;
using NToastNotify;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

// Read from Configuration, then fall back to raw env var, then guard
var connectionString =
      Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection (env var or appsettings).");
Console.WriteLine($"Using connection string: {connectionString}");
// RDS SQL Server: host + port 1433, no backslashes
// Example value you should pass via env:
// Server=tcp:xxxxxxxxxx.rds.amazonaws.com,1433;Database=xxxxxx;User Id=xxxx;Password=xxxx;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30

builder.Services.AddDbContext<MainEntity>(x => x.UseSqlServer(connectionString));


builder.Services.AddRazorPages().AddNToastNotifyNoty(new NotyOptions
{
    ProgressBar = true,
    Timeout = 5000
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();
app.UseNToastNotify();
app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Generate}/{action=generate}/{id?}");

app.Run();
