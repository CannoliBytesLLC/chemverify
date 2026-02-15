using System.Text.Json.Serialization;
using ChemVerify.API.Composition;
using ChemVerify.API.Endpoints;
using ChemVerify.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register ChemVerify services (EF Core, connectors, extractors, validators, audit service)
builder.Services.AddChemVerifyServices(builder.Configuration);

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
    ChemVerifyDbContext db = scope.ServiceProvider.GetRequiredService<ChemVerifyDbContext>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Map ChemVerify endpoints
app.MapRunEndpoints();

app.Run();

public partial class Program { }

