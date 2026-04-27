using AccessPass.Services;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Services;
using Virtuagym.CheckIn.Web.Components;
using Virtuagym.CheckIn.Web.Models;
using Virtuagym.CheckIn.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind AppSettings from configuration
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// AccessPass services (offline local access passes)
builder.Services.AddSingleton<AccessPassStore>();
builder.Services.AddSingleton<IAccessPassService, AccessPassService>();
builder.Services.AddSingleton<AccessPassQrService>();

// Core services
builder.Services.AddSingleton<WebLogService>();
builder.Services.AddSingleton<WebCheckinService>();
builder.Services.AddSingleton<WebVirtuagymApiServiceFactory>();
builder.Services.AddSingleton<AppSettingsService>();

// Sound player (scoped per circuit – needs IJSRuntime)
builder.Services.AddScoped<ISoundPlayer, WebSoundPlayer>();

// Background tasks (cache sync, auto-checkout, log cleanup)
builder.Services.AddSingleton<BackgroundTaskService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundTaskService>());

// Hardware scanner service (QR, USB-Reader, CCID – runs independently of browser)
builder.Services.AddSingleton<HardwareScannerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HardwareScannerService>());

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Allow larger JS interop payloads (e.g. webcam base64 frames)
        options.MaximumReceiveMessageSize = 4 * 1024 * 1024; // 4 MB
    });

var app = builder.Build();

// Initialize localization
var settings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
L.Initialize(settings.AppLanguage);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
