﻿using ItchioLibrary.Models;
using Playnite;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ItchioLibrary
{
    [LoadPlugin]
    public class ItchioLibrary : LibraryPluginBase<ItchioLibrarySettingsViewModel>
    {
        public ItchioLibrary(IPlayniteAPI api) : base(
            "itch.io",
            Guid.Parse("00000001-EBB2-4EEC-ABCB-7C89937A42BB"),
            new LibraryPluginCapabilities { CanShutdownClient = true },
            new ItchioClient(),
            Itch.Icon,
            (_) => new ItchioLibrarySettingsView(),
            (g) => new ItchioGameController(g, api),
            () => new ItchioMetadataProvider(api),
            api)
        {
            SettingsViewModel = new ItchioLibrarySettingsViewModel(this, api);
        }

        public static bool TryGetGameActions(string installDir, out GameAction playAction, out List<GameAction> otherActions)
        {
            var fileEnum = new SafeFileEnumerator(installDir, ".itch.toml", SearchOption.AllDirectories);
            if (fileEnum.Any())
            {
                var strMan = File.ReadAllText(fileEnum.First().FullName);
                var manifest = Serialization.FromToml<LaunchManifest>(strMan);
                if (manifest.actions?.Any() == true)
                {
                    playAction = null;
                    otherActions = new List<GameAction>();
                    foreach (var action in manifest.actions)
                    {
                        if (action.name.Equals("play", StringComparison.OrdinalIgnoreCase))
                        {
                            playAction = new GameAction
                            {
                                IsHandledByPlugin = true,
                                Name = "Play",
                                Path = action.path,
                                WorkingDir = action.path.IsHttpUrl() ? null : ExpandableVariables.InstallationDirectory,
                                Type = action.path.IsHttpUrl() ? GameActionType.URL : GameActionType.File,
                                Arguments = action.args?.Any() == true ? string.Join(" ", action.args) : null
                            };
                        }
                        else
                        {
                            otherActions.Add(new GameAction
                            {
                                Name = action.name,
                                Path = action.path,
                                WorkingDir = action.path.IsHttpUrl() ? null : ExpandableVariables.InstallationDirectory,
                                Type = action.path.IsHttpUrl() ? GameActionType.URL : GameActionType.File,
                                Arguments = action.args?.Any() == true ? string.Join(" ", action.args) : null
                            });
                        }
                    }

                    return true;
                }
            }

            playAction = null;
            otherActions = null;
            return false;
        }

        internal Dictionary<string, GameInfo> GetInstalledGames()
        {
            var games = new Dictionary<string, GameInfo>();
            using (var butler = new Butler())
            {
                var caves = butler.GetCaves();
                if (caves?.Any() != true)
                {
                    return games;
                }

                foreach (var cave in caves)
                {
                    if (cave.game.classification != Models.GameClassification.game &&
                        cave.game.classification != Models.GameClassification.tool)
                    {
                        continue;
                    }

                    // TODO: We don't support multiple version of one game at moment
                    if (games.ContainsKey(cave.game.id.ToString()))
                    {
                        continue;
                    }

                    var installDir = cave.installInfo.installFolder;
                    if (!Directory.Exists(installDir))
                    {
                        continue;
                    }

                    var game = new GameInfo()
                    {
                        Source = "itch.io",
                        GameId = cave.game.id.ToString(),
                        Name = cave.game.title.RemoveTrademarks(),
                        InstallDirectory = installDir,
                        IsInstalled = true,
                        CoverImage = cave.game.coverUrl,
                        Platform = "PC",
                        PlayAction = new GameAction
                        {
                            Type = GameActionType.URL,
                            Path = ItchioGameController.DynamicLaunchActionStr,
                            Arguments = cave.id,
                            IsHandledByPlugin = true
                        }
                    };

                    if (TryGetGameActions(installDir, out var play, out var others))
                    {
                        game.OtherActions = new List<GameAction>(others);
                    }

                    games.Add(game.GameId, game);
                }
            }

            return games;
        }

        internal List<GameInfo> GetLibraryGames()
        {
            var games = new List<GameInfo>();
            using (var butler = new Butler())
            {
                var profiles = butler.GetProfiles();
                if (profiles?.Any() != true)
                {
                    throw new Exception("User is not authenticated.");
                }

                foreach (var profile in profiles)
                {
                    var keys = butler.GetOwnedKeys(profile.id);
                    if (!keys.HasItems())
                    {
                        continue;
                    }

                    foreach (var key in keys)
                    {
                        if (key.game == null)
                        {
                            continue;
                        }

                        if (key.game.classification != GameClassification.game &&
                            key.game.classification != GameClassification.tool)
                        {
                            continue;
                        }

                        if (games.Any(a => a.GameId == key.game.id.ToString()))
                        {
                            continue;
                        }

                        var game = new GameInfo()
                        {
                            Source = "itch.io",
                            GameId = key.game.id.ToString(),
                            Name = key.game.title.RemoveTrademarks(),
                            CoverImage = key.game.coverUrl,
                            Platform = "PC"
                        };

                        games.Add(game);
                    }
                }
            }

            return games;
        }

        public override IEnumerable<GameInfo> GetGames()
        {
            var allGames = new List<GameInfo>();
            var installedGames = new Dictionary<string, GameInfo>();
            Exception importError = null;

            if (!SettingsViewModel.Settings.ImportInstalledGames && !SettingsViewModel.Settings.ImportUninstalledGames)
            {
                return allGames;
            }

            if (Itch.IsInstalled)
            {
                if (SettingsViewModel.Settings.ImportInstalledGames)
                {
                    try
                    {
                        installedGames = GetInstalledGames();
                        Logger.Debug($"Found {installedGames.Count} installed itch.io games.");
                        allGames.AddRange(installedGames.Values.ToList());
                    }
                    catch (Exception e) when (!PlayniteApi.ApplicationInfo.ThrowAllErrors)
                    {
                        Logger.Error(e, "Failed to import installed itch.io games.");
                        importError = e;
                    }
                }

                if (SettingsViewModel.Settings.ConnectAccount)
                {
                    try
                    {
                        var libraryGames = GetLibraryGames();
                        Logger.Debug($"Found {libraryGames.Count} library itch.io games.");

                        if (!SettingsViewModel.Settings.ImportUninstalledGames)
                        {
                            libraryGames = libraryGames.Where(lg => installedGames.ContainsKey(lg.GameId)).ToList();
                        }

                        foreach (var game in libraryGames)
                        {
                            if (installedGames.TryGetValue(game.GameId, out var installed))
                            {
                                installed.Playtime = game.Playtime;
                                installed.LastActivity = game.LastActivity;
                            }
                            else
                            {
                                allGames.Add(game);
                            }
                        }
                    }
                    catch (Exception e) when (!PlayniteApi.ApplicationInfo.ThrowAllErrors)
                    {
                        Logger.Error(e, "Failed to import linked account itch.io games details.");
                        importError = e;
                    }
                }
            }
            else
            {
                importError = new Exception(
                    PlayniteApi.Resources.GetString("LOCItchioClientNotInstalledError"));
            }

            if (importError != null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    ImportErrorMessageId,
                    string.Format(PlayniteApi.Resources.GetString("LOCLibraryImportError"), Name) +
                    System.Environment.NewLine + importError.Message,
                    NotificationType.Error,
                    () => OpenSettingsView()));
            }
            else
            {
                PlayniteApi.Notifications.Remove(ImportErrorMessageId);
            }

            return allGames;
        }
    }
}
