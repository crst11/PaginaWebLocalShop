

using TiendaMicroempresas.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalShopCors(builder.Configuration);
builder.Services.AddLocalShopServices();

var app = builder.Build();

app.UseCors("frontend");

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "LocalShop API" }));

app.MapGet("/", () => Results.Ok(new
{
    api = "LocalShop API",
    status = "Activa",
    endpoints = new[]
    {
        "/api/store/overview",
        "/api/auth/register",
        "/api/auth/login",
        "/api/auth/me",
        "/api/customers/register",
        "/api/customers/login",
        "/api/customers/orders",
        "/api/admin/catalog",
        "/api/admin/orders",
        "/api/admin/products",
        "/api/orders",
        "/api/orders/checkout"
    }
}));

app.MapControllers();

app.Run();

