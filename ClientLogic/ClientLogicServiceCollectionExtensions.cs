using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClientLogic;

public static class ClientLogicServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ClientLogic services a front end shares: the map loader and the tunnel handler.
    /// The front end registers the UI services they need first (<see cref="IUiDispatcher"/>, <see cref="IDialogService"/>).
    /// A service the front end has already registered is kept, e.g. a tunnel handler that it also drives from its game loop.
    /// </summary>
    public static IServiceCollection AddClientLogic(this IServiceCollection services)
    {
        services.TryAddSingleton<MapLoader>();
        services.TryAddSingleton(serviceProvider => new TunnelHandler(serviceProvider.GetRequiredService<IUiDispatcher>()));
        return services;
    }
}
