using CertAI.Manager.Models;
using Microsoft.EntityFrameworkCore;
using static CertAI.Manager.Controllers.TrainerController;

var builder = WebApplication.CreateBuilder(args);

// 1. Solo necesitamos Controllers with Views
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// builder.Services.AddRazorPages(); // <-- COMENTA O BORRA ESTO

// Agrega esto en tus servicios
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson(options =>
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
    );

// 1. Agrega esto para que el sistema sepa inyectar HttpClient
builder.Services.AddHttpClient();

// 2. Tu registro actual (mantenlo)
builder.Services.AddScoped<IGeminiService, GeminiService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Cambia esto a una ruta de MVC o coméntalo temporalmente para ver el error real
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// 2. Esta es tu ruta maestra
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Trainer}/{action=Index}/{id?}");

// app.MapRazorPages(); // <-- COMENTA O BORRA ESTO

app.Run();