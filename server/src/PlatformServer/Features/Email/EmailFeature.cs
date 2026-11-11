using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace PlatformServer.Features.Email;

/// <summary>
/// Transactional email. Identity takes the sender as its <see cref="IEmailSender{TUser}"/>, the account's
/// endpoints as <see cref="IAccountEmailSender"/>, and the CMS's test page as <see cref="EmailSender"/>. The account's
/// emails go out through the <see cref="IEmailQueue"/>; the test page sends at once, to show the result.
/// </summary>
public static class EmailFeature
{
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName);

        services.AddSingleton<EmailSender>();
        services.AddSingleton<IAccountEmailSender>(provider => provider.GetRequiredService<EmailSender>());
        services.AddSingleton<IEmailSender<ApplicationUser>>(provider => provider.GetRequiredService<EmailSender>());

        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(provider => provider.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailQueueWorker>();

        return services;
    }
}
