using AIStudyHub.Application.Interfaces;
using AIStudyHub.Infrastructure.Persistence;
using AIStudyHub.Infrastructure.Services;
using AIStudyHub.Infrastructure.Services.Email;
using AIStudyHub.Infrastructure.Services.Gemini;
using AIStudyHub.Infrastructure.Services.Storage;
using AIStudyHub.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIStudyHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure Database Connection (SQL Server)
        services.AddDbContext<StudyHubDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Register the DB interface to resolve the same instance
        services.AddScoped<IStudyHubDbContext>(provider => provider.GetRequiredService<StudyHubDbContext>());

        // 2. Register File Storage, System Clock
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IFileStorage, LocalFileStorage>();

        // 3. Conditional registration for Mail Service
        bool useMockMail = configuration.GetValue<bool>("MailSettings:UseMock");
        if (useMockMail)
        {
            services.AddScoped<IMailService, MockMailService>();
        }
        else
        {
            services.AddScoped<IMailService, MailService>();
        }

        // 4. Conditional registration for Gemini Service
        bool useMockGemini = configuration.GetValue<bool>("Gemini:UseMock");
        if (useMockGemini)
        {
            services.AddScoped<IGeminiService, MockGeminiService>();
        }
        else
        {
            services.AddScoped<IGeminiService, GeminiService>();
        }

        // 5. Register Hosted background service for renewal schedules
        services.AddHostedService<SubscriptionRenewalScheduler>();

        return services;
    }
}
