using System.Text.Json;
using System.Text.Json.Serialization;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var app = builder.Build();

app.UseHttpMetrics();
app.MapControllers();
app.MapMetrics("/metrics");

app.Run();

public partial class Program { }
