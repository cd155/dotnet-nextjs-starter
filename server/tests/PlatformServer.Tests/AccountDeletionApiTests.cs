using System.Net;
using System.Net.Http.Json;
using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlatformServer.Features.Account;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// Deleting an account through HTTP: a code by email, then the code and the password.
public class AccountDeletionApiTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string DeletionCode = "/api/v1/account/deletion-code";
    private const string Delete = "/api/v1/account/delete";
    private const string Login = "/api/v1/account/login";
    private const string Profile = "/api/v1/account/profile";
    private const string Email = "user@example.com";
    private const string Password = "Password1";

    [Fact]
    public async Task TheRightPasswordAndCode_DeleteTheAccount_AndItsNotes()
    {
        var factory = await shared.ResetAsync();
        var userId = await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client);
        (await client.PostAsJsonAsync("/api/v1/me/notes", new { title = "Mine" })).EnsureSuccessStatusCode();

        var code = await RequestCodeAsync(factory, client);
        var response = await client.PostAsJsonAsync(Delete, Request(Password, code));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Profile)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password })).StatusCode);
        Assert.Equal(Email, Assert.Single(factory.Emails.AccountDeletions));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.Notes.AnyAsync());
    }

    [Fact]
    public async Task TheLastAllowedWrongCode_LocksTheAccount()
    {
        var factory = await shared.ResetAsync();
        var userId = await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client);
        var code = await RequestCodeAsync(factory, client);
        await SetFailedCountAsync(factory, userId, 4);

        var wrong = await client.PostAsJsonAsync(Delete, Request(Password, WrongCode(code)));
        var thenRight = await client.PostAsJsonAsync(Delete, Request(Password, code));

        Assert.Equal(HttpStatusCode.Locked, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, thenRight.StatusCode);
        Assert.NotNull(await FindUserAsync(factory, userId));
    }

    [Fact]
    public async Task ACodeMadeForSomethingElse_DoesNotDelete()
    {
        var factory = await shared.ResetAsync();
        var userId = await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client);

        string otherCode;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            otherCode = await userManager.GenerateUserTokenAsync(
                (await userManager.FindByIdAsync(userId))!, TokenOptions.DefaultEmailProvider, "SomethingElse");
        }

        var response = await client.PostAsJsonAsync(Delete, Request(Password, otherCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await FindUserAsync(factory, userId));
    }

    [Theory]
    [InlineData(RoleConstants.Admin)]
    [InlineData(RoleConstants.ContentManager)]
    public async Task AStaffAccount_CantBeDeleted(string role)
    {
        var factory = await shared.ResetAsync();
        var userId = await factory.CreateUserAsync(Email, Password);
        await factory.AddToRoleAsync(userId, role);
        var client = factory.CreateClient();
        await SignInAsync(client);

        var codeResponse = await client.PostAsync(DeletionCode, null);
        var deleteResponse = await client.PostAsJsonAsync(Delete, Request(Password, "000000"));

        Assert.Equal(HttpStatusCode.Conflict, codeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        Assert.Empty(factory.Emails.DeletionCodes);
        Assert.NotNull(await FindUserAsync(factory, userId));
    }

    [Fact]
    public async Task ThreeCodesAnHour_AreAllowed_AndNoMore()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsync(DeletionCode, null)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(3, factory.Emails.DeletionCodes.Count);
    }

    [Fact]
    public async Task SignedOut_Returns401()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(DeletionCode, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Delete, Request(Password, "000000"))).StatusCode);
    }

    private static DeleteAccountRequest Request(string password, string code)
        => new() { CurrentPassword = password, Code = code };

    // Another 6-digit code: the last digit moved on by one.
    private static string WrongCode(string code)
        => code[..^1] + (char)('0' + (code[^1] - '0' + 1) % 10);

    private static async Task SignInAsync(HttpClient client)
        => (await client.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password })).EnsureSuccessStatusCode();

    private static async Task<string> RequestCodeAsync(AppApiFactory factory, HttpClient client)
    {
        var response = await client.PostAsync(DeletionCode, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var (email, code) = Assert.Single(factory.Emails.DeletionCodes);
        Assert.Equal(Email, email);
        return code;
    }

    private static async Task<ApplicationUser?> FindUserAsync(AppApiFactory factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
    }

    private static async Task SetFailedCountAsync(AppApiFactory factory, string userId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId))!;
        user.AccessFailedCount = count;
        await userManager.UpdateAsync(user);
    }
}
