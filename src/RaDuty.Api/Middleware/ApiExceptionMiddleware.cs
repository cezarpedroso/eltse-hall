using Microsoft.AspNetCore.Mvc;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception exception)
        {
            var (status, code, title) = exception switch
            {
                AppException app => (app.StatusCode, app.Code, app.Message),
                DomainRuleException rule => (422, rule.Code, rule.Message),
                BadHttpRequestException => (400, "INVALID_REQUEST", "The request could not be processed."),
                _ => (500, "UNEXPECTED_ERROR", "An unexpected error occurred.")
            };
            if (status >= 500) logger.LogError(exception, "Unhandled API error. Trace {TraceId}", context.TraceIdentifier);
            else logger.LogInformation("API request rejected with {ErrorCode}. Trace {TraceId}", code, context.TraceIdentifier);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = status, Title = title, Type = $"https://raduty.example/problems/{code.ToLowerInvariant()}",
                Instance = context.Request.Path
            };
            problem.Extensions["code"] = code;
            problem.Extensions["traceId"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
