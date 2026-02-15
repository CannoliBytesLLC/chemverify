using System.Text.Json.Serialization;
using Aegis.API.Composition;
using Aegis.API.Endpoints;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register AEGIS services (EF Core, connectors, extractors, validators, audit service)
builder.Services.AddAegisServices(builder.Configuration);

// Serialize enums as strings ("Completed", "Pass", etc.) for audit readability
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

WebApplication app = builder.Build();

// Auto-migrate in Development for v0
if (app.Environment.IsDevelopment())
{
    using IServiceScope scope = app.Services.CreateScope();
    AegisDbContext db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Map AEGIS endpoints
app.MapRunEndpoints();

app.Run();
