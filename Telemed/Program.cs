// Program.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Telemed.Models;
using Telemed.Services;
using TServices = Telemed.Services; // namespace alias to avoid ambiguity

var builder = WebApplication.CreateBuilder(args);

// Bind Stripe settings and set the global API key
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Register Identity Email Sender (for built-in Identity emails)
builder.Services.AddTransient<IEmailSender, EmailSenderService>();

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Persist data-protection keys to disk so app-pool recycles (common on
// shared/free hosting) don't invalidate every login session and
// antiforgery token in the app.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

//ChatBot
builder.Services.AddScoped<ChatService>();

// QuestPDF community license (optional)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Register application services (use alias to avoid conflict with Stripe.InvoiceService)
builder.Services.AddScoped<TServices.IInvoiceService, TServices.InvoiceService>();
builder.Services.AddTransient<TServices.IEmailSenderExtended, TServices.EmailSenderWithAttachments>();

// Add Identity
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();

// Register as a singleton so controllers (e.g. AdminController's manual
// "run backup now" action) can always resolve it, regardless of whether
// the automatic background loop is enabled.
builder.Services.AddSingleton<TelemedSystem.Services.BackupService>();

// Only run it as a hosted background service if enabled in configuration -
// free/shared hosting tiers recycle the app pool and have no always-on,
// so the automatic loop is unreliable there.
if (builder.Configuration.GetValue<bool>("DatabaseBackup:Enabled"))
{
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<TelemedSystem.Services.BackupService>());
}


var app = builder.Build();

// Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // Identity UI support

// Optional: Seed roles and admin users (only if DbInitializer exists)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await DbInitializer.InitializeAsync(services);
}

// --------- TEMPORARY: Force-confirm admin user for development ---------
// This will confirm the seeded admin's email programmatically so you can log in.
// Remove this block after successful login.
//using (var scope = app.Services.CreateScope())
//{
//    var services = scope.ServiceProvider;
//    try
//    {
//        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
//        var admin = await userManager.FindByEmailAsync("admin@telemed.local");
//        if (admin != null && !admin.EmailConfirmed)
//        {
//            var token = await userManager.GenerateEmailConfirmationTokenAsync(admin);
//            var confirmResult = await userManager.ConfirmEmailAsync(admin, token);
//            Console.WriteLine($"Admin email forced confirmed: {confirmResult.Succeeded}");
//            if (!confirmResult.Succeeded)
//            {
//                foreach (var err in confirmResult.Errors)
//                {
//                    Console.WriteLine($"Confirm error: {err.Code} - {err.Description}");
//                }
//            }
//        }
//        else
//        {
//            Console.WriteLine("Admin already confirmed or not found.");
//        }
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine("Error while forcing admin confirmation: " + ex.Message);
//    }
//}
// ----------------------------------------------------------------------

app.Run();
