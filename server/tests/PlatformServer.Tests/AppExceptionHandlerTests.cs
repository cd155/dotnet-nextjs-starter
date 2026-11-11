using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PlatformServer.Configuration;
using SharedLibrary.Exceptions;
using Xunit;

namespace PlatformServer.Tests;

/// <summary>
/// Runs <see cref="AppExceptionHandler"/> behind the built-in exception handler, registered as Program registers
/// it.
/// </summary>
public class AppExceptionHandlerTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<(int StatusCode, string ContentType, string Body)> InvokeAsync(
        RequestDelegate next,
        bool isDevelopment = false,
        CancellationToken requestAborted = default,
        string? accept = null)
    {
        var env = new FakeHostEnvironment
        {
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener("Test"));
        services.AddSingleton<IHostEnvironment>(env);
        services.AddExceptionHandling(env);

        await using var serviceProvider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(serviceProvider);
        app.UseExceptionHandler();
        app.Run(next);
        var pipeline = app.Build();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            RequestAborted = requestAborted
        };
        context.Request.Path = "/api/v1/test";
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty, body);
    }

    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<JsonElement> AssertProblemAsync(Exception exception, int expectedStatus, string expectedTitle)
    {
        var (statusCode, contentType, body) = await InvokeAsync(_ => throw exception);

        Assert.Equal(expectedStatus, statusCode);
        Assert.Contains("application/problem+json", contentType);

        var problem = Parse(body);
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, problem.GetProperty("title").GetString());
        Assert.Equal(exception.Message, problem.GetProperty("detail").GetString());
        Assert.Equal("/api/v1/test", problem.GetProperty("instance").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.False(problem.TryGetProperty("exceptionType", out _));

        return problem;
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenNoException_PassesThrough()
    {
        var (statusCode, _, _) = await InvokeAsync(ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        Assert.Equal(204, statusCode);
    }

    // ── AppValidationException ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenAppValidationException_Returns400WithErrorsByField()
    {
        var exception = new AppValidationException("Validation failed.", new Dictionary<string, string[]>
        {
            ["Field"] = ["Required."]
        });

        var problem = await AssertProblemAsync(exception, 400, ExceptionConstants.ValidationErrorTitle);

        var errors = problem.GetProperty("errors");
        Assert.Equal("Required.", errors.GetProperty("Field")[0].GetString());
    }

    // ── Other AppExceptions ───────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenEntityNotFoundException_Returns404WithProblemJson()
        => await AssertProblemAsync(new EntityNotFoundException("Note", 42), 404, ExceptionConstants.ResourceNotFoundTitle);

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedException_Returns401WithProblemJson()
        => await AssertProblemAsync(new UnauthorizedException(), 401, ExceptionConstants.UnauthorizedTitle);

    [Fact]
    public async Task InvokeAsync_WhenForbiddenException_Returns403WithProblemJson()
        => await AssertProblemAsync(new ForbiddenException(), 403, ExceptionConstants.ForbiddenTitle);

    [Fact]
    public async Task InvokeAsync_WhenConflictException_Returns409WithProblemJson()
        => await AssertProblemAsync(new ConflictException("A generator draft is already waiting."), 409, ExceptionConstants.ConflictTitle);

    [Fact]
    public async Task InvokeAsync_WhenServiceUnavailableException_Returns503WithProblemJson()
        => await AssertProblemAsync(new ServiceUnavailableException(), 503, ExceptionConstants.ServiceUnavailableTitle);

    [Fact]
    public async Task InvokeAsync_WhenDomainException_Returns400WithProblemJson()
        => await AssertProblemAsync(new DomainException("Business rule violated."), 400, ExceptionConstants.DomainErrorTitle);

    [Fact]
    public async Task InvokeAsync_WhenAcceptRulesOutJson_ReturnsStatusWithoutBody()
    {
        var (statusCode, _, body) = await InvokeAsync(
            _ => throw new EntityNotFoundException("Note", 42),
            accept: "text/html");

        Assert.Equal(404, statusCode);
        Assert.Empty(body);
    }

    // ── BadHttpRequestException ───────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenArgumentCannotBeBound_Returns400WithProblemJson()
        => await AssertProblemAsync(
            new BadHttpRequestException("Failed to bind parameter \"int id\" from \"nope\"."),
            400,
            "Bad Request");

    // ── Unhandled exception (production) ─────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_InProduction_Returns500WithGenericBody()
    {
        var (statusCode, contentType, body) = await InvokeAsync(
            _ => throw new InvalidOperationException("Secret internal detail"),
            isDevelopment: false);

        Assert.Equal(500, statusCode);
        Assert.Contains("application/problem+json", contentType);
        Assert.DoesNotContain("Secret internal detail", body);

        var problem = Parse(body);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.False(problem.TryGetProperty("detail", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    // ── Unhandled exception (development) ────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_InDevelopment_Returns500WithSpecificMessage()
    {
        var (statusCode, _, body) = await InvokeAsync(
            _ => throw new InvalidOperationException("Secret internal detail"),
            isDevelopment: true);

        Assert.Equal(500, statusCode);
        Assert.Equal("Secret internal detail", Parse(body).GetProperty("detail").GetString());
    }

    // ── Cancelled request ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenOperationCancelledAndRequestAborted_DoesNotWriteResponse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (statusCode, _, body) = await InvokeAsync(
            _ => throw new OperationCanceledException("Cancelled"),
            requestAborted: cts.Token);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, statusCode);
        Assert.Empty(body);
    }

    // ── Sentry filter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientErrorExceptionFilter_FiltersOnlyClientErrorsTheHandlerAnswered()
    {
        var filter = new ClientErrorExceptionFilter();
        var notFound = new EntityNotFoundException("Note", 42);
        var unavailable = new ServiceUnavailableException();

        // Thrown outside a request, as in the CMS, the exception still reaches Sentry.
        Assert.False(filter.Filter(notFound));

        await InvokeAsync(_ => throw notFound);
        await InvokeAsync(_ => throw unavailable);

        Assert.True(filter.Filter(notFound));
        Assert.False(filter.Filter(unavailable));
    }
}
