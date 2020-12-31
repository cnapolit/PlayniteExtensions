﻿using Microsoft.Win32;
using OriginLibrary.Models;
using OriginLibrary.Services;
using Playnite;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace OriginLibrary
{
    [LoadPlugin]
    public class OriginLibrary : LibraryPluginBase<OriginLibrarySettingsViewModel>
    {
        public class PlatformPath
        {
            public string CompletePath { get; set; }
            public string Root { get; set; }
            public string Path { get; set; }

            public PlatformPath(string completePath)
            {
                CompletePath = completePath;
            }

            public PlatformPath(string root, string path)
            {
                Root = root;
                Path = path;
                CompletePath = System.IO.Path.Combine(root, path);
            }
        }

        public OriginLibrary(IPlayniteAPI api) : base(
            "Origin",
            Guid.Parse("85DD7072-2F20-4E76-A007-41035E390724"),
            new LibraryPluginCapabilities { CanShutdownClient = true },
            new OriginClient(),
            Origin.Icon,
            (_) => new OriginLibrarySettingsView(),
            null,
            () => new OriginMetadataProvider(api),
            api)
        {
            SettingsViewModel = new OriginLibrarySettingsViewModel(this, PlayniteApi);
        }

        public override IGameController GetGameController(Game game)
        {
            return new OriginGameController(this, game, PlayniteApi);
        }

        internal PlatformPath GetPathFromPlatformPath(string path, RegistryView platformView)
        {
            if (!path.StartsWith("["))
            {
                return new PlatformPath(path);
            }

            var matchPath = Regex.Match(path, @"\[(.*?)\\(.*)\\(.*)\](.*)");
            if (!matchPath.Success)
            {
                Logger.Warn("Unknown path format " + path);
                return null;
            }

            var root = matchPath.Groups[1].Value;
            RegistryKey rootKey = null;

            switch (root)
            {
                case "HKEY_LOCAL_MACHINE":
                    rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, platformView);
                    break;

                case "HKEY_CURRENT_USER":
                    rootKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, platformView);
                    break;

                default:
                    throw new Exception("Unknown registry root entry " + root);
            }

            var subPath = matchPath.Groups[2].Value.Trim(Path.DirectorySeparatorChar);
            var key = matchPath.Groups[3].Value;
            var executable = matchPath.Groups[4].Value.Trim(Path.DirectorySeparatorChar);
            var subKey = rootKey.OpenSubKey(subPath);
            if (subKey == null)
            {
                return null;
            }

            var keyValue = rootKey.OpenSubKey(subPath).GetValue(key);
            if (keyValue == null)
            {
                return null;
            }

            return new PlatformPath(keyValue.ToString(), executable);
        }

        internal PlatformPath GetPathFromPlatformPath(string path)
        {
            var resultPath = GetPathFromPlatformPath(path, RegistryView.Registry64);
            if (resultPath == null)
            {
                resultPath = GetPathFromPlatformPath(path, RegistryView.Registry32);
            }

            return resultPath;
        }

        private System.Collections.Specialized.NameValueCollection ParseOriginManifest(string path)
        {
            var text = File.ReadAllText(path);
            var data = HttpUtility.UrlDecode(text);
            return HttpUtility.ParseQueryString(data);
        }

        internal GameInstallerData GetGameInstallerData(string dataPath)
        {
            try
            {
                if (File.Exists(dataPath))
                {
                    var ser = new XmlSerializer(typeof(GameInstallerData));
                    return (GameInstallerData)ser.Deserialize(XmlReader.Create(dataPath));
                }
                else
                {
                    var rootDir = dataPath;
                    for (int i = 0; i < 4; i++)
                    {
                        var target = Path.Combine(rootDir, "__Installer");
                        if (Directory.Exists(target))
                        {
                            rootDir = target;
                            break;
                        }
                        else
                        {
                            rootDir = Path.Combine(rootDir, "..");
                        }
                    }

                    var instPath = Path.Combine(rootDir, "installerdata.xml");
                    if (File.Exists(instPath))
                    {
                        var ser = new XmlSerializer(typeof(GameInstallerData));
                        return (GameInstallerData)ser.Deserialize(XmlReader.Create(instPath));
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to deserialize game installer xml {dataPath}.");
            }

            return null;
        }

        internal GameLocalDataResponse GetLocalManifest(string id)
        {
            try
            {
                return OriginApiClient.GetGameLocalData(id);
            }
            catch (WebException exc) when ((exc.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Info($"Origin manifest {id} not found on EA server, generating fake manifest.");
                return new GameLocalDataResponse
                {
                    offerId = id,
                    offerType = "Doesn't exist"
                };
            }
        }

        public GameAction GetGamePlayTask(string installerDataPath)
        {
            var data = GetGameInstallerData(installerDataPath);
            if (data == null)
            {
                return null;
            }
            else
            {
                var paths = GetPathFromPlatformPath(data.runtime.launchers.Last().filePath);
                if (paths.CompletePath.Contains(@"://"))
                {
                    return new GameAction
                    {
                        Type = GameActionType.URL,
                        Path = paths.CompletePath,
                        IsHandledByPlugin = true
                    };
                }
                else
                {
                    var action = new GameAction
                    {
                        Type = GameActionType.File,
                        IsHandledByPlugin = true
                    };
                    if (paths.Path.IsNullOrEmpty())
                    {
                        action.Path = paths.CompletePath;
                        action.WorkingDir = Path.GetDirectoryName(paths.CompletePath);
                    }
                    else
                    {
                        action.Path = paths.CompletePath;
                        action.WorkingDir = paths.Root;
                    }

                    return action;
                }
            }
        }

        public GameAction GetGamePlayTask(GameLocalDataResponse manifest)
        {
            var platform = manifest.publishing.softwareList.software.FirstOrDefault(a => a.softwarePlatform == "PCWIN");
            var playAction = new GameAction()
            {
                IsHandledByPlugin = true
            };

            if (string.IsNullOrEmpty(platform.fulfillmentAttributes.executePathOverride))
            {
                return null;
            }

            if (platform.fulfillmentAttributes.executePathOverride.Contains(@"://"))
            {
                playAction.Type = GameActionType.URL;
                playAction.Path = platform.fulfillmentAttributes.executePathOverride;
            }
            else
            {
                var executePath = GetPathFromPlatformPath(platform.fulfillmentAttributes.executePathOverride);
                if (executePath != null)
                {
                    if (executePath.CompletePath.EndsWith("installerdata.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        return GetGamePlayTask(executePath.CompletePath);
                    }
                    else
                    {
                        playAction.WorkingDir = executePath.Root;
                        playAction.Path = executePath.CompletePath;
                    }
                }
            }

            return playAction;
        }

        public string GetInstallDirectory(GameLocalDataResponse localData)
        {
            var platform = localData.publishing.softwareList.software.FirstOrDefault(a => a.softwarePlatform == "PCWIN");
            if (platform == null)
            {
                return null;
            }

            var installPath = GetPathFromPlatformPath(platform.fulfillmentAttributes.installCheckOverride);
            if (installPath == null ||
                installPath.CompletePath.IsNullOrEmpty() ||
                !File.Exists(installPath.CompletePath))
            {
                return null;
            }

            var action = GetGamePlayTask(localData);
            if (action?.Type == GameActionType.File)
            {
                return action.WorkingDir;
            }
            else
            {
                return Path.GetDirectoryName(installPath.CompletePath);
            }
        }

        public Dictionary<string, GameInfo> GetInstalledGames()
        {
            var contentPath = Path.Combine(Origin.DataPath, "LocalContent");
            var games = new Dictionary<string, GameInfo>();

            if (Directory.Exists(contentPath))
            {
                var packages = Directory.GetFiles(contentPath, "*.mfst", SearchOption.AllDirectories);
                foreach (var package in packages)
                {
                    try
                    {
                        var gameId = Path.GetFileNameWithoutExtension(package);
                        if (!gameId.StartsWith("Origin"))
                        {
                            // Get game id by fixing file via adding : before integer part of the name
                            // for example OFB-EAST52017 converts to OFB-EAST:52017
                            var match = Regex.Match(gameId, @"^(.*?)(\d+)$");
                            if (!match.Success)
                            {
                                Logger.Warn("Failed to get game id from file " + package);
                                continue;
                            }

                            gameId = match.Groups[1].Value + ":" + match.Groups[2].Value;
                        }

                        var newGame = new GameInfo()
                        {
                            Source = "Origin",
                            GameId = gameId,
                            IsInstalled = true,
                            Platform = "PC"
                        };

                        GameLocalDataResponse localData = null;

                        try
                        {
                            localData = GetLocalManifest(gameId);
                        }
                        catch (Exception e) when (!Environment.IsDebugBuild)
                        {
                            Logger.Error(e, $"Failed to get Origin manifest for a {gameId}, {package}");
                            continue;
                        }

                        if (localData == null)
                        {
                            continue;
                        }

                        if (localData.offerType != "Base Game" && localData.offerType != "DEMO")
                        {
                            continue;
                        }

                        newGame.Name = StringExtensions.NormalizeGameName(localData.localizableAttributes.displayName);
                        var installDir = GetInstallDirectory(localData);
                        if (installDir.IsNullOrEmpty())
                        {
                            continue;
                        }

                        newGame.InstallDirectory = installDir;
                        newGame.PlayAction = new GameAction
                        {
                            IsHandledByPlugin = true,
                            Type = GameActionType.URL,
                            Path = Origin.GetLaunchString(gameId)
                        };

                        games.Add(newGame.GameId, newGame);
                    }
                    catch (Exception e) when (!Environment.IsDebugBuild)
                    {
                        Logger.Error(e, $"Failed to import installed Origin game {package}.");
                    }
                }
            }

            return games;
        }

        public List<GameInfo> GetLibraryGames()
        {
            using (var view = PlayniteApi.WebViews.CreateOffscreenView())
            {
                var api = new OriginAccountClient(view);

                if (!api.GetIsUserLoggedIn())
                {
                    throw new Exception("User is not logged in.");
                }

                var token = api.GetAccessToken();
                if (token == null)
                {
                    throw new Exception("Failed to get access to user account.");
                }

                if (!string.IsNullOrEmpty(token.error))
                {
                    throw new Exception("Access error: " + token.error);
                }

                var info = api.GetAccountInfo(token);
                if (!string.IsNullOrEmpty(info.error))
                {
                    throw new Exception("Access error: " + info.error);
                }

                var games = new List<GameInfo>();

                foreach (var game in api.GetOwnedGames(info.pid.pidId, token).Where(a => a.offerType == "basegame"))
                {
                    UsageResponse usage = null;
                    try
                    {
                        usage = api.GetUsage(info.pid.pidId, game.offerId, token);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Failed to get usage data for {game.offerId}");
                    }

                    var gameName = game.offerId;
                    try
                    {
                        var localData = GetLocalManifest(game.offerId);
                        if (localData != null)
                        {
                            gameName = StringExtensions.NormalizeGameName(localData.localizableAttributes.displayName);
                        }
                    }
                    catch (Exception e) when (!Environment.IsDebugBuild)
                    {
                        Logger.Error(e, $"Failed to get Origin manifest for a {game.offerId}");
                        continue;
                    }

                    games.Add(new GameInfo()
                    {
                        Source = "Origin",
                        GameId = game.offerId,
                        Name = gameName,
                        LastActivity = usage?.lastSessionEndTimeStamp,
                        Playtime = usage?.total ?? 0,
                        Platform = "PC"
                    });
                }

                return games;
            }
        }

        public override IEnumerable<GameInfo> GetGames()
        {
            var allGames = new List<GameInfo>();
            var installedGames = new Dictionary<string, GameInfo>();
            Exception importError = null;

            if (SettingsViewModel.Settings.ImportInstalledGames)
            {
                try
                {
                    installedGames = GetInstalledGames();
                    Logger.Debug($"Found {installedGames.Count} installed Origin games.");
                    allGames.AddRange(installedGames.Values.ToList());
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import installed Origin games.");
                    importError = e;
                }
            }

            if (SettingsViewModel.Settings.ConnectAccount && SettingsViewModel.Settings.ImportUninstalledGames)
            {
                try
                {
                    var libraryGames = GetLibraryGames();
                    Logger.Debug($"Found {libraryGames.Count} library Origin games.");

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
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import linked account Origin games details.");
                    importError = e;
                }
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
