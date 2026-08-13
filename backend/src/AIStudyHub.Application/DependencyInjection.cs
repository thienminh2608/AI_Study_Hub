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

        return services;
    }
}
