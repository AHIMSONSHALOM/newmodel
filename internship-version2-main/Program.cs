using ProductHub_MVC.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the MVC container.
builder.Services.AddControllersWithViews();

// Register your custom MS-SQL Database Context Utility
builder.Services.AddScoped<SqlDbContext>();

// =========================================================================
// REGISTER SESSIONS AND COOKIE CONTEXT PROPERTIES FOR TOAST NOTIFICATIONS
// =========================================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
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

// Route directly to your custom Product Controller dashboard grid layout page on load
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Product}/{action=Index}/{id?}");

app.Run();