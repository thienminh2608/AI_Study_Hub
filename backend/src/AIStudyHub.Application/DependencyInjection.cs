using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStudyHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IFriendshipService, FriendshipService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ITrashService, TrashService>();
        services.AddScoped<IVersionService, VersionService>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IPaymentCompletionService, PaymentCompletionService>();
        services.AddScoped<ISubscriptionPurchaseService, SubscriptionPurchaseService>();
        services.AddScoped<IAdminAnalyticsService, AdminAnalyticsService>();

        return services;
    }
}
