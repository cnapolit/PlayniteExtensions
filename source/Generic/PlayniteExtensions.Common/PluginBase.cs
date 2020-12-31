﻿using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Playnite.SDK
{
    [IgnorePlugin]
    public class MetadataPluginBase<TSettings> : MetadataPlugin
        where TSettings : ISettings
    {
        public readonly ILogger Logger = LogManager.GetLogger();

        public override string Name { get; }
        public override Guid Id { get; }
        public override List<MetadataField> SupportedFields { get; }

        public TSettings SettingsViewModel { get; set; }
        private Func<MetadataRequestOptions, OnDemandMetadataProvider> GetMetadataProviderAction { get; }
        private Func<UserControl> GetSettingsViewAction { get; }

        public MetadataPluginBase(
            string name,
            Guid id,
            List<MetadataField> supportedFields,
            Func<UserControl> getSettingsViewAction,
            Func<MetadataRequestOptions, OnDemandMetadataProvider> getMetadataProviderAction,
            IPlayniteAPI api) : base(api)
        {
            Name = name;
            Id = id;
            SupportedFields = supportedFields;
            GetSettingsViewAction = getSettingsViewAction;
            GetMetadataProviderAction = getMetadataProviderAction;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (SettingsViewModel != null)
            {
                return SettingsViewModel;
            }

            return base.GetSettings(firstRunSettings);
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            if (GetSettingsViewAction != null)
            {
                return GetSettingsViewAction();
            }

            return base.GetSettingsView(firstRunView);
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            if (GetMetadataProviderAction != null)
            {
                return GetMetadataProviderAction(options);
            }

            return null;
        }
    }

    [IgnorePlugin]
    public class LibraryPluginBase<TSettings> : LibraryPlugin
        where TSettings : ISettings
    {
        private Func<bool, UserControl> GetSettingsViewAction { get; }
        private Func<Game, IGameController> GetGameControllerAction { get; }
        private Func<LibraryMetadataProvider> GetMetadataDownloaderAction { get; }

        public readonly ILogger Logger = LogManager.GetLogger();

        public string ImportErrorMessageId { get; }
        public override string Name { get; }
        public override Guid Id { get; }
        public override LibraryPluginCapabilities Capabilities { get; }
        public override LibraryClient Client { get; }
        public override string LibraryIcon { get; }

        public TSettings SettingsViewModel { get; set; }

        public LibraryPluginBase(
            string name,
            Guid id,
            LibraryPluginCapabilities capabilities,
            LibraryClient client,
            string libraryIcon,
            Func<bool, UserControl> getSettingsViewAction,
            Func<Game, IGameController> getGameControllerAction,
            Func<LibraryMetadataProvider> getMetadataDownloaderAction,
            IPlayniteAPI api) : base(api)
        {
            Name = name;
            Id = id;
            ImportErrorMessageId = $"{name}_libImportError";
            Capabilities = capabilities;
            Client = client;
            LibraryIcon = libraryIcon;
            GetSettingsViewAction = getSettingsViewAction;
            GetGameControllerAction = getGameControllerAction;
            GetMetadataDownloaderAction = getMetadataDownloaderAction;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (SettingsViewModel != null)
            {
                return SettingsViewModel;
            }

            return base.GetSettings(firstRunSettings);
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            if (GetSettingsViewAction != null)
            {
                return GetSettingsViewAction(firstRunView);
            }

            return base.GetSettingsView(firstRunView);
        }

        public override IGameController GetGameController(Game game)
        {
            if (GetGameControllerAction != null)
            {
                return GetGameControllerAction(game);
            }

            return base.GetGameController(game);
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            if (GetMetadataDownloaderAction != null)
            {
                return GetMetadataDownloaderAction();
            }

            return base.GetMetadataDownloader();
        }
    }
}
