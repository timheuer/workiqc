using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkIQC.Persistence.Services;

namespace WorkIQC.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        var dbPath = StorageHelper.GetDatabasePath();

        services.AddDbContext<WorkIQDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IConversationService, ConversationService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkIQDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}
