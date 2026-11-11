using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using DataLibrary.Entities.Notes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DatabaseInitializer.Data;

/// <summary>The mock data for a local stack: a demo user with a few notes. Run with <c>--seed</c> or <c>SEED_MOCK_DATA=true</c>.</summary>
public static class Seeder
{
    public const string DemoEmail = "demo@example.com";
    public const string DemoPassword = "Demopassword1@";

    public static async Task SeedDatabase(AppDbContext context, UserManager<ApplicationUser> userManager)
    {
        var demo = await userManager.FindByEmailAsync(DemoEmail);
        if (demo is null)
        {
            demo = new ApplicationUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                DisplayName = "Demo",
                EmailConfirmed = true,
                CreatedTime = DateTime.UtcNow
            };
            Program.EnsureSucceeded(await userManager.CreateAsync(demo, DemoPassword), $"create demo user {DemoEmail}");
            Program.EnsureSucceeded(await userManager.AddToRoleAsync(demo, RoleConstants.User), $"assign the User role to {DemoEmail}");
            Console.WriteLine($"Created demo user: {DemoEmail}");
        }

        if (await context.Notes.AnyAsync(n => n.UserId == demo.Id))
        {
            Console.WriteLine("The demo user already has notes. Skipping them.");
            return;
        }

        context.Notes.AddRange(
            new NoteEntity
            {
                UserId = demo.Id,
                Title = "Welcome to the starter",
                Body = "This note comes from DatabaseInitializer's Seeder. Edit it in the web app, or ask your AI chat app to change it through MCP.",
                IsPinned = true
            },
            new NoteEntity
            {
                UserId = demo.Id,
                Title = "Add your own feature",
                Body = "Follow Notes through the layers: NoteEntity, Features/Notes, Features/Mcp/NoteTools, the CMS page, and web/src/app/(app)/notes."
            });
        await context.SaveChangesAsync();
        Console.WriteLine("Seeded the demo user's notes.");
    }
}
