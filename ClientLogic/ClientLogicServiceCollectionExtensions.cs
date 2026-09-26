using ClientLogic.Launch;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClientLogic;

public static class ClientLogicServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ClientLogic services a front end shares: the map loader, the CnCNet connection and its data, the
    /// tunnel handler and the game process service.
    /// The front end registers the UI services they need first (<see cref="IUiDispatcher"/>, <see cref="IDialogService"/>)
    /// and a <see cref="System.Random"/>. A service the front end has already registered is kept, e.g. a tunnel handler
    /// that it also drives from its game loop.
    /// </summary>
    public static IServiceCollection AddClientLogic(this IServiceCollection services)
    {
        services.TryAddSingleton<MapLoader>();
        services.TryAddSingleton<GameCollection>();
        services.TryAddSingleton<CnCNetUserData>();
        services.TryAddSingleton<CnCNetManager>();
        services.TryAddSingleton<PrivateMessageHandler>();
        services.TryAddSingleton<GameProcessService>();
        services.TryAddSingleton(serviceProvider => new TunnelHandler(serviceProvider.GetRequiredService<IUiDispatcher>()));
        return services;
    }
}
