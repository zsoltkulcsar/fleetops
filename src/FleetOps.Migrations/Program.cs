using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<FleetOpsDbContext>(options => options.UseNpgsql(
    Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL") ??
    "Host=localhost;Port=5432;Database=fleetops;Username=postgres;Password=postgres"));
var app = builder.Build();
app.MapGet("/", () => "FleetOps migrations");
app.Run();
