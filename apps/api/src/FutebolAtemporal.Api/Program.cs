using FutebolAtemporal.Api;
﻿using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Infrastructure;
using FutebolAtemporal.Infrastructure.Persistence;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => ApiLogging.Configure(cfg, ctx.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
// A web envia e recebe estados como texto ("RegistrationClosed", "Inactive", "Paid"); números seguem aceitos.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp", policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseCors("WebApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await app.InitializeDatabaseAsync();
app.Run();

public partial class Program;

file sealed class AppExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public AppExceptionHandler(IProblemDetailsService problemDetails) => _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code, extensions) = exception switch
        {
            ForbiddenException => (StatusCodes.Status403Forbidden, "Sem permissão", (string?)"forbidden", (IReadOnlyDictionary<string, object?>?)null),
            NotFoundException nf => (StatusCodes.Status404NotFound, "Não encontrado", nf.Code, (IReadOnlyDictionary<string, object?>?)null),
            ConflictException cf => (StatusCodes.Status409Conflict, "Conflito de estado", cf.Code, cf.Extensions),
            UnprocessableEntityException ue => (StatusCodes.Status422UnprocessableEntity, "Entidade não processável", ue.Code, ue.Extensions),
            ServiceUnavailableException su => (StatusCodes.Status503ServiceUnavailable, "Serviço indisponível", su.Code, (IReadOnlyDictionary<string, object?>?)null),
            DomainException de => (StatusCodes.Status400BadRequest, "Regra de negócio", de.Code, de.Extensions),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflito de atualização", "live_version_conflict", (IReadOnlyDictionary<string, object?>?)null),
            _ => (0, null, null, null)
        };

        if (status == 0) return false;

        httpContext.Response.StatusCode = status;
        var ctx = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = title,
                // Não expõe a mensagem interna do EF em conflitos de concorrência.
                Detail = exception is DbUpdateConcurrencyException
                    ? "Os dados mudaram ao mesmo tempo. Atualize e tente de novo."
                    : exception.Message,
                Status = status
            }
        };
        if (!string.IsNullOrWhiteSpace(code))
            ctx.ProblemDetails.Extensions["code"] = code;
        if (extensions is not null)
        {
            foreach (var kv in extensions)
                ctx.ProblemDetails.Extensions[kv.Key] = kv.Value;
        }
        return await _problemDetails.TryWriteAsync(ctx);
    }
}
