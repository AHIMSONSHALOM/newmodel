using ProductHub_MVC.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the MVC container.
builder.Services.AddControllersWithViews();

// Register your custom MS-SQL Database Context Utility
builder.Services.AddScoped<SqlDbContext>();

// =========================================================================
// REGISTER SESSIONS AND COOKIE CONTEXT PROPERTIES FOR AUTHENTICATION
// =========================================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20); // Cookie auto expires after 20 minutes of inactivity
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable the Session infrastructure state mechanism
app.UseSession();

app.UseAuthorization();

// =========================================================================
// ✅ DEFAULT ROUTE MODIFIED: Route directly to Account/Login gatekeeper on load
// =========================================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();