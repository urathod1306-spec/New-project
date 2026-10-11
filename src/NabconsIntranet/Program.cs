using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

var contentRoot = AppContext.BaseDirectory;

// ---------------------------------------------------------------------------
// Command-line maintenance tools (run while the intranet is stopped):
//   NabconsIntranet.exe --restore <backup.zip>
//   NabconsIntranet.exe --backup
//   NabconsIntranet.exe --reset-admin-password
// ---------------------------------------------------------------------------
if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && !args[0].StartsWith("--urls", StringComparison.OrdinalIgnoreCase)
    && !args[0].StartsWith("--environment", StringComparison.OrdinalIgnoreCase) && !args[0].StartsWith("--contentRoot", StringComparison.OrdinalIgnoreCase))
{
    return Tools.Run(args, contentRoot);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
});

builder.Host.UseWindowsService(o => o.ServiceName = "NABCONSIntranet");

var options = builder.Configuration.GetSection("Intranet").Get<IntranetOptions>() ?? new IntranetOptions();
options.Resolve(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(options);
builder.Logging.AddProvider(new FileLoggerProvider(options.LogPath));

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = options.MaxUploadBytes * 5 + 1024 * 1024;
});
builder.Services.Configure<FormOptions>(f =>
{
    f.MultipartBodyLengthLimit = options.MaxUploadBytes * 5 + 1024 * 1024;
    f.ValueCountLimit = 2048;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<Fmt>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<FileStore>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<MasterRepository>();
builder.Services.AddSingleton<StaffRepository>();
builder.Services.AddSingleton<CircularRepository>();
builder.Services.AddSingleton<NoticeRepository>();
builder.Services.AddSingleton<RequestWorkflow>();
builder.Services.AddTransient<Seeder>();
builder.Services.AddHostedService<BackupScheduler>();

if (options.IsWindowsAuth)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, WindowsRoleTransformation>();
}
else
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(c =>
    {
        c.LoginPath = "/Account/Login";
        c.LogoutPath = "/Account/Logout";
        c.AccessDeniedPath = "/Account/AccessDenied";
        c.ExpireTimeSpan = TimeSpan.FromMinutes(options.SessionTimeoutMinutes);
        c.SlidingExpiration = true;
        c.Cookie.Name = "NABCONS.Auth";
        c.Cookie.HttpOnly = true;
        c.Cookie.SameSite = SameSiteMode.Strict;
        c.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        c.Events.OnValidatePrincipal = IdentityBuilder.ValidateCookie;
    });
}

builder.Services.AddAuthorization(a =>
{
    // Everything requires a signed-in, active intranet user unless a page explicitly allows anonymous access.
    a.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(ClaimNames.UserId).Build();
    a.AddPolicy(Policies.Publisher, p => p.RequireClaim(ClaimNames.UserId).RequireRole(Roles.Publisher, Roles.Administrator));
    a.AddPolicy(Policies.Approver, p => p.RequireClaim(ClaimNames.UserId).RequireRole(Roles.Approver));
    a.AddPolicy(Policies.Admin, p => p.RequireClaim(ClaimNames.UserId).RequireRole(Roles.Administrator));
    a.AddPolicy(Policies.AuditView, p => p.RequireClaim(ClaimNames.UserId).RequireRole(Roles.Auditor, Roles.Administrator));
});

builder.Services.AddAntiforgery(a =>
{
    a.Cookie.Name = "NABCONS.AF";
    a.Cookie.SameSite = SameSiteMode.Strict;
    a.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", Policies.Admin);
    o.Conventions.AuthorizeFolder("/Audit", Policies.AuditView);
    o.Conventions.AuthorizePage("/Circulars/Edit", Policies.Publisher);
    o.Conventions.AuthorizePage("/Notices/Index", Policies.Publisher);
    o.Conventions.AuthorizePage("/Requests/Inbox", Policies.Approver);
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    o.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

// --- first run: create the database, administrator and (optionally) demo data ---
app.Services.GetRequiredService<Db>().Initialize();
app.Services.GetRequiredService<Seeder>().Run();
app.Services.GetRequiredService<AuditService>().Log("System.Started", "System", null,
    $"NABCONS Intranet {typeof(Program).Assembly.GetName().Version} started; auth={options.AuthenticationMode}", "system");

app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
                                    "connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Cache-Control"] = "no-cache, no-store";
    h["Pragma"] = "no-cache";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = s => s.Context.Response.Headers["Cache-Control"] = "public, max-age=3600",
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Local accounts that must change their password are held on the change-password page.
app.Use(async (ctx, next) =>
{
    if (ctx.User.HasClaim(ClaimNames.MustChangePassword, "1")
        && !ctx.Request.Path.StartsWithSegments("/Account")
        && !ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js") && !ctx.Request.Path.StartsWithSegments("/img"))
    {
        ctx.Response.Redirect("/Account/ChangePassword");
        return;
    }
    await next();
});

app.MapRazorPages();

if (!Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    var port = new Uri((builder.Configuration["Urls"] ?? "http://0.0.0.0:8080").Split(';')[0].Replace("0.0.0.0", "localhost").Replace("*", "localhost").Replace("+", "localhost")).Port;
    var ips = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a)).Select(a => a.ToString()).ToList();
    Console.WriteLine();
    Console.WriteLine("  ==============================================================");
    Console.WriteLine($"   {options.SiteTitle} is running.  KEEP THIS WINDOW OPEN.");
    Console.WriteLine($"   On this PC:        http://localhost:{port}");
    Console.WriteLine($"   From other LAN PCs: http://{Environment.MachineName}:{port}");
    foreach (var ip in ips) Console.WriteLine($"                       http://{ip}:{port}");
    Console.WriteLine("   To stop: close this window or press Ctrl+C.");
    Console.WriteLine("  ==============================================================");
    Console.WriteLine();
}
app.Logger.LogInformation("NABCONS Intranet listening on {Urls}. Data folder: {Data}", string.Join(", ", app.Urls), options.DataPath);
try
{
    app.Run();
}
catch (IOException ex) when (ex.InnerException is System.Net.Sockets.SocketException || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("The intranet could not start because its network port is already in use.");
    Console.Error.WriteLine("It is probably already running (check for another window, or the Windows service).");
    Console.Error.WriteLine("To use a different port, change \"Urls\" in appsettings.json, e.g. http://0.0.0.0:8081");
    Console.Error.WriteLine("Details: " + ex.Message);
    return 10;
}
return 0;

public partial class Program;
