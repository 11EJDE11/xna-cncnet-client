using System;

using ClientGUI;
using ClientGUI.Settings;

using ClientLogic.Launch;
using ClientLogic.UI;

using DTAClient.Domain;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.DXGUI.Campaign;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer;
using DTAClient.DXGUI.Multiplayer.CnCNet;
using DTAClient.DXGUI.Multiplayer.GameLobby;
using DTAClient.Online;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework.Graphics;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

using MainMenu = DTAClient.DXGUI.Generic.MainMenu;

namespace DTAClient.DXGUI
{
    public static class XnaFrontEndServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the XNA front end: its UI services, the services it drives or saves itself, and its controls.
        /// Call before <c>AddClientLogic()</c>, which keeps these registrations.
        /// </summary>
        public static IServiceCollection AddXnaFrontEnd(this IServiceCollection services, WindowManager windowManager,
            GraphicsDevice graphicsDevice)
        {
            services
                .AddSingleton<ServiceProvider>()
                .AddSingleton(windowManager)
                .AddSingleton(graphicsDevice)
                .AddSingleton<IUiDispatcher>(new XnaUiDispatcher(windowManager))
                .AddSingleton<IDialogService>(new XnaDialogService(windowManager))
                .AddSingleton(GameProcessLogic.Service)
                .AddSingleton(_ =>
                {
                    var cncnetUserData = new CnCNetUserData();
                    windowManager.GameClosing += (_, _) => cncnetUserData.Save();
                    return cncnetUserData;
                })
                .AddSingleton(serviceProvider =>
                {
                    var tunnelHandler = new TunnelHandler(serviceProvider.GetRequiredService<IUiDispatcher>());

                    windowManager.Game.Components.Add(
                        new TunnelHandlerComponent(windowManager, serviceProvider.GetRequiredService<CnCNetManager>(), tunnelHandler));

                    return tunnelHandler;
                })
                .AddSingleton<DiscordHandler>()
                .AddSingleton<DirectDrawWrapperManager>();

            // singleton xna controls - same instance on each request
            services
                .AddSingletonXnaControl<LoadingScreen>()
                .AddSingletonXnaControl<TopBar>()
                .AddSingletonXnaControl<OptionsWindow>()
                .AddSingletonXnaControl<PrivateMessagingWindow>()
                .AddSingletonXnaControl<PrivateMessagingPanel>()
                .AddSingletonXnaControl<LANLobby>()
                .AddSingletonXnaControl<CnCNetGameLobby>()
                .AddSingletonXnaControl<CnCNetGameLoadingLobby>()
                .AddSingletonXnaControl<CnCNetLobby>()
                .AddSingletonXnaControl<GameInProgressWindow>()
                .AddSingletonXnaControl<SkirmishLobby>()
                .AddSingletonXnaControl<MainMenu>()
                .AddSingletonXnaControl<MapPreviewBox>()
                .AddSingletonXnaControl<GameLaunchButton>()
                .AddSingletonXnaControl<PlayerExtraOptionsPanel>()
                .AddSingletonXnaControl<CampaignTagSelector>()
                .AddSingletonXnaControl<GameLoadingWindow>()
                .AddSingletonXnaControl<StatisticsWindow>()
                .AddSingletonXnaControl<UpdateQueryWindow>()
                .AddSingletonXnaControl<ManualUpdateQueryWindow>()
                .AddSingletonXnaControl<UpdateWindow>()
                .AddSingletonXnaControl<ExtrasWindow>();

            // transient xna controls - new instance on each request
            services
                .AddTransientXnaControl<XNAControl>()
                .AddTransientXnaControl<XNAButton>()
                .AddTransientXnaControl<XNAClientButton>()
                .AddTransientXnaControl<XNAClientCheckBox>()
                .AddTransientXnaControl<XNAClientDropDown>()
                .AddTransientXnaControl<XNALinkButton>()
                .AddTransientXnaControl<XNAExtraPanel>()
                .AddTransientXnaControl<XNACheckBox>()
                .AddTransientXnaControl<XNADropDown>()
                .AddTransientXnaControl<XNALabel>()
                .AddTransientXnaControl<XNALinkLabel>()
                .AddTransientXnaControl<XNAClientLinkLabel>()
                .AddTransientXnaControl<XNAListBox>()
                .AddTransientXnaControl<XNAMultiColumnListBox>()
                .AddTransientXnaControl<XNAPanel>()
                .AddTransientXnaControl<XNAProgressBar>()
                .AddTransientXnaControl<XNASuggestionTextBox>()
                .AddTransientXnaControl<XNATextBox>()
                .AddTransientXnaControl<XNATextBlock>()
                .AddTransientXnaControl<XNATrackbar>()
                .AddTransientXnaControl<XNAChatTextBox>()
                .AddTransientXnaControl<ChatListBox>()
                .AddTransientXnaControl<GameLobbyCheckBox>()
                .AddTransientXnaControl<GameLobbyDropDown>()
                .AddTransientXnaControl<CampaignCheckBox>()
                .AddTransientXnaControl<CampaignDropDown>()
                .AddTransientXnaControl<SettingCheckBox>()
                .AddTransientXnaControl<SettingDropDown>()
                .AddTransientXnaControl<FileSettingCheckBox>()
                .AddTransientXnaControl<FileSettingDropDown>();

            return services;
        }
    }
}
