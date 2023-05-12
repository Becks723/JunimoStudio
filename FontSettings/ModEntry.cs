﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BmFont;
using FontSettings.Framework;
using FontSettings.Framework.DataAccess;
using FontSettings.Framework.DataAccess.Models;
using FontSettings.Framework.DataAccess.Parsing;
using FontSettings.Framework.FontGenerators;
using FontSettings.Framework.FontInfo;
using FontSettings.Framework.FontPatching;
using FontSettings.Framework.FontPatching.Invalidators;
using FontSettings.Framework.FontScanning;
using FontSettings.Framework.FontScanning.Scanners;
using FontSettings.Framework.Integrations;
using FontSettings.Framework.Menus;
using FontSettings.Framework.Menus.ViewModels;
using FontSettings.Framework.Menus.Views;
using FontSettings.Framework.Migrations;
using FontSettings.Framework.Models;
using FontSettings.Framework.Patchers;
using FontSettings.Framework.Preset;
using HarmonyLib;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData;
using StardewValley.Menus;

namespace FontSettings
{
    internal class ModEntry : Mod
    {
        private MigrateTo_0_6_0 _0_6_0_Migration;

        private ModConfig _config;

        private FontConfigRepository _fontConfigRepository;
        private FontPresetRepository _fontPresetRepository;
        private VanillaFontDataRepository _vanillaFontDataRepository;
        private SampleDataRepository _sampleDataRepository;

        private FontConfigManager _fontConfigManager;
        private VanillaFontConfigProvider _vanillaFontConfigProvider;
        private IFontFileProvider _fontFileProvider;
        private Framework.Preset.FontPresetManager _fontPresetManager;

        private readonly FontSettingsMenuContextModel _menuContextModel = new();

        private MainFontPatcher _mainFontPatcher;

        private VanillaFontProvider _vanillaFontProvider;

        private readonly DataAdditionalLanguagesWatcher _dataAdditionalLanguagesWatcher = new();

        private TitleFontButton _titleFontButton;

        internal static IModHelper ModHelper { get; private set; }

        internal static Harmony Harmony { get; private set; }

        public override void Entry(IModHelper helper)
        {
            ModHelper = this.Helper;
            I18n.Init(helper.Translation);
            Log.Init(this.Monitor);
            Textures.Init(this.ModManifest);
            StardewValleyUI.EntryPoint.Main();

            this._config = helper.ReadConfig<ModConfig>();
            this._config.ValidateValues(this.Monitor);

            // init vanilla font provider.
            this._vanillaFontProvider = new VanillaFontProvider(helper, this.Monitor, this._config);
            this._vanillaFontProvider.RecordStarted += this.OnFontRecordStarted;
            this._vanillaFontProvider.RecordFinished += this.OnFontRecordFinished;

            // init migrations.
            this._0_6_0_Migration = new(helper, this.ModManifest);

            string presetDirectory = Path.Combine(Constants.DataPath, ".smapi", "mod-data", this.ModManifest.UniqueID.ToLower(), "Presets");

            // do changes to database.
            this._0_6_0_Migration.ApplyDatabaseChanges(
                fontConfigRepository: new FontConfigRepository(helper),
                fontPresetRepository: new FontPresetRepository(presetDirectory),
                modConfig: this._config,
                writeModConfig: this.SaveConfig);

            // init service objects.
            this._vanillaFontConfigProvider = new VanillaFontConfigProvider(this._vanillaFontProvider);
            this._fontFileProvider = new FontFileProvider(this.YieldFontScanners());

            var vanillaFontConfigParser = new FontConfigParserForVanilla(this._fontFileProvider, this._vanillaFontProvider);
            var userFontConfigParser = new FontConfigParserForUser(this._fontFileProvider, this._vanillaFontProvider, this._vanillaFontConfigProvider);
            var fontPresetParser = new FontPresetParser(this._fontFileProvider, this._vanillaFontConfigProvider, this._vanillaFontProvider);

            // init repositories.
            this._fontConfigRepository = new FontConfigRepository(helper, this.Monitor, userFontConfigParser);
            this._fontPresetRepository = new FontPresetRepository(presetDirectory, this.Monitor, fontPresetParser);
            this._vanillaFontDataRepository = new VanillaFontDataRepository(helper, this.Monitor, vanillaFontConfigParser);
            this._sampleDataRepository = new SampleDataRepository(helper, this.Monitor);
            this._config.Sample = this._sampleDataRepository.ReadSampleData();

            // init managers.
            this._fontConfigManager = new FontConfigManager();
            this._fontPresetManager = new Framework.Preset.FontPresetManager();

            // connect manager and repository.
            this._fontConfigManager.ConfigUpdated += (s, e) => this._fontConfigRepository.WriteConfig(e.Key, e.Config);
            this._fontPresetManager.PresetUpdated += (s, e) => this._fontPresetRepository.WritePreset(e.Name, e.Preset);

            // init font patching.
            this._mainFontPatcher = new MainFontPatcher(this._fontConfigManager, new FontPatchResolverFactory(), new FontPatchInvalidatorComposition(helper));
            this._vanillaFontProvider.SetInvalidateHelper(this._mainFontPatcher);

            // watch `Data/AdditonalLanguages` asset.
            this._dataAdditionalLanguagesWatcher.Updated += this.OnDataAdditionalLanguagesUpdated;

            Harmony = new Harmony(this.ModManifest.UniqueID);
            {
                new FontShadowPatcher(this._config)
                    .Patch(Harmony, this.Monitor);

                var spriteTextPatcher = new SpriteTextPatcher(this._config);
                spriteTextPatcher.Patch(Harmony, this.Monitor);
                this._mainFontPatcher.FontPixelZoomOverride += (s, e) =>
                    spriteTextPatcher.SetOverridePixelZoom(e.PixelZoom);

                new SpriteTextLatinPatcher(this._config, this.ModManifest, helper)
                    .Patch(Harmony, this.Monitor);
            }

            helper.Events.Content.AssetRequested += this.OnAssetRequestedEarly;
            helper.Events.Content.AssetReady += this.OnAssetReadyEarly;
            helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Content.AssetReady += this.OnAssetReady;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.Display.WindowResized += this.OnWindowResized;
            helper.Events.Display.Rendered += this.OnRendered;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        }

        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            this._vanillaFontProvider.OnUpdateTicking(e);
        }


        private void OnAssetRequestedEarly(object sender, AssetRequestedEventArgs e)
        {
            this._vanillaFontProvider.OnAssetRequested(e);
        }

        private void OnAssetReadyEarly(object sender, AssetReadyEventArgs e)
        {
            this._vanillaFontProvider.OnAssetReady(e);
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            new GMCMIntegration(
                config: this._config,
                reset: this.ResetConfig,
                save: () => this.SaveConfig(this._config),
                modRegistry: this.Helper.ModRegistry,
                monitor: this.Monitor,
                manifest: this.ModManifest)
                .Integrate();

            new ToolbarIconsIntegration(
                modRegistry: this.Helper.ModRegistry,
                monitor: this.Monitor,
                uniqueId: this.ModManifest.UniqueID,
                openFontSettingsMenu: this.OpenFontSettingsMenu)
                .Integrate();

            // init title font button. (must be after `Textures.OnAssetRequested` subscription)
            this._titleFontButton = new TitleFontButton(
                position: this.GetTitleFontButtonPosition(),
                onClicked: () => this.OpenFontSettingsMenu());
        }

        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            if (this._config.OpenFontSettingsMenu.JustPressed())
            {
                this.OpenFontSettingsMenu();
            }
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            Textures.OnAssetRequested(e);
            this._mainFontPatcher.OnAssetRequested(e);
        }

        private void OnAssetReady(object sender, AssetReadyEventArgs e)
        {
            this._mainFontPatcher.OnAssetReady(e);
            this._dataAdditionalLanguagesWatcher.OnAssetReady(e);
        }

        private void OnWindowResized(object sender, WindowResizedEventArgs e)
        {
            if (this._titleFontButton != null)
                this._titleFontButton.Position = this.GetTitleFontButtonPosition();
        }

        private void OnRendered(object sender, RenderedEventArgs e)
        {
            if (this.IsTitleMenuInteractable())
                this._titleFontButton?.Draw(e.SpriteBatch);
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (this.IsTitleMenuInteractable())
                this._titleFontButton?.Update();
        }

        private void SaveConfig(ModConfig config)
        {
            this.Helper.WriteConfig(config);
        }

        private void ResetConfig()
        {
            // 重置
            this._config.ResetToDefault();

            // 保存
            this.Helper.WriteConfig(this._config);
        }

        private IEnumerable<IFontFileScanner> YieldFontScanners()
        {
            var scanSettings = new ScanSettings();

            // installation folder for each platform.
            switch (Constants.TargetPlatform)
            {
                case GamePlatform.Windows:
                    yield return new InstalledFontScannerForWindows(scanSettings);
                    break;
                case GamePlatform.Mac:
                    yield return new InstalledFontScannerForMacOS(scanSettings);
                    break;
                case GamePlatform.Linux:
                    yield return new InstalledFontScannerForLinux(scanSettings);
                    break;
                case GamePlatform.Android:
                    yield return new IntalledFontScannerForAndroid(scanSettings);
                    break;
            }

            // mod internal 'assets/fonts' folder.
            string vanillaFontFolder = Path.Combine(this.Helper.DirectoryPath, "assets/fonts");
            Directory.CreateDirectory(vanillaFontFolder);
            yield return new BasicFontFileScanner(vanillaFontFolder, scanSettings);

            // custom folders specified by user.
            var customFolders = this._config.CustomFontFolders.Distinct().ToArray();
            foreach (string folder in customFolders)
            {
                if (!Directory.Exists(folder))
                    this.Monitor.Log($"Skipped invalid custom font folder: {folder}");

                yield return new BasicFontFileScanner(folder, scanSettings);
            }
        }

        private void OnFontRecordStarted(object sender, RecordEventArgs e)
        {
            this.Monitor.Log($"记录{e.Language}的{e.FontType}，中断font patch。");
            this._mainFontPatcher.PauseFontPatch();
        }

        private void OnFontRecordFinished(object sender, RecordEventArgs e)
        {
            this.Monitor.Log($"完成记录{e.Language}的{e.FontType}。");

            var key = new FontConfigKey(e.Language, e.FontType);

            // parse vanilla configs in context.
            if (this._vanillaFontDataRepository != null
                && this._vanillaFontConfigProvider != null)
            {
                var config = this._vanillaFontDataRepository.ReadVanillaFontConfig(key);

                if (config != null)
                    this._vanillaFontConfigProvider.AddVanillaFontConfigs(new Dictionary<FontConfigKey, FontConfig>() { { key, config } });
            }

            // parse configs in context.
            if (this._fontConfigRepository != null
                && this._fontConfigManager != null)
            {
                var config = this._fontConfigRepository.ReadConfig(key);

                if (config != null)
                    this._fontConfigManager.AddFontConfig(new(key, config));
            }

            // parse presets in context.
            if (this._fontPresetRepository != null
                && this._fontPresetManager != null)
            {
                var presets = this._fontPresetRepository.ReadPresets(key);

                this._fontPresetManager.AddPresets(presets);
            }

            this.Monitor.Log($"恢复font patch。");

            this._mainFontPatcher.ResumeFontPatch();
        }

        private void OpenFontSettingsMenu()
        {
            var menu = this.CreateFontSettingsMenu();

            if (Game1.activeClickableMenu is TitleMenu)
                TitleMenu.subMenu = menu;
            else
                Game1.activeClickableMenu = menu;
        }

        private FontSettingsMenu CreateFontSettingsMenu()
        {
            Stopwatch stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();

                var gen = new SampleFontGenerator(this._vanillaFontProvider, () => this._config.EnableLatinDialogueFont);
                IFontGenerator sampleFontGenerator = gen;
                IAsyncFontGenerator sampleAsyncFontGenerator = gen;

                FontSettingsMenuModel viewModel;
                {
                    bool async = true;
                    if (async)
                        viewModel = new FontSettingsMenuModelAsync(
                            config: this._config,
                            vanillaFontProvider: this._vanillaFontProvider,
                            sampleFontGenerator: sampleFontGenerator,
                            sampleAsyncFontGenerator: sampleAsyncFontGenerator,
                            presetManager: this._fontPresetManager,
                            fontConfigManager: this._fontConfigManager,
                            vanillaFontConfigProvider: this._vanillaFontConfigProvider,
                            gameFontChanger: new FontPatchChanger(this._mainFontPatcher),
                            fontFileProvider: this._fontFileProvider,
                            fontInfoRetriever: new FontInfoRetriever(),
                            asyncFontInfoRetriever: new FontInfoRetriever(),
                            stagedValues: this._menuContextModel);
                    else
                        viewModel = new FontSettingsMenuModel(
                            config: this._config,
                            vanillaFontProvider: this._vanillaFontProvider,
                            sampleFontGenerator: sampleFontGenerator,
                            sampleAsyncFontGenerator: sampleAsyncFontGenerator,
                            presetManager: this._fontPresetManager,
                            fontConfigManager: this._fontConfigManager,
                            vanillaFontConfigProvider: this._vanillaFontConfigProvider,
                            gameFontChanger: new FontPatchChanger(this._mainFontPatcher),
                            fontFileProvider: this._fontFileProvider,
                            fontInfoRetriever: new FontInfoRetriever(),
                            stagedValues: this._menuContextModel);
                }

                return new FontSettingsMenu(this._fontPresetManager, this.Helper.ModRegistry, this._config.SimplifiedDropDown, this._config.EnableLatinDialogueFont, viewModel);
            }
            finally
            {
                stopwatch.Stop();
                this.Monitor.Log($"{nameof(FontSettingsMenu)} creation completed in '{stopwatch.ElapsedMilliseconds}ms'");
            }
        }

        private void OnDataAdditionalLanguagesUpdated(object sender, List<ModLanguage> value)
        {
            FontHelpers.SetModLanguages(value.ToArray());
        }

        private bool AssertModFileExists(string relativePath, out string? fullPath) // fullPath = null when returns false
        {
            fullPath = Path.Combine(this.Helper.DirectoryPath, relativePath);
            fullPath = PathUtilities.NormalizePath(fullPath);

            if (this.AssertModFileExists(fullPath))
                return true;
            else
            {
                fullPath = null;
                return false;
            }
        }

        private bool AssertModFileExists(string relativePath)
        {
            string fullPath = Path.Combine(this.Helper.DirectoryPath, relativePath);
            fullPath = PathUtilities.NormalizePath(fullPath);

            return this.AssertFileExists(fullPath, I18n.Misc_ModFileNotFound(relativePath));
        }

        private bool AssertFileExists(string filePath, string message)
        {
            if (!File.Exists(filePath))
            {
                this.Monitor.Log(message, LogLevel.Error);
                return false;
            }
            return true;
        }

        private Microsoft.Xna.Framework.Point GetTitleFontButtonPosition()
        {
            var registry = this.Helper.ModRegistry;
            bool gmcm = registry.IsLoaded("spacechase0.GenericModConfigMenu");  // (36, Game1.viewport.Height - 100)
            bool mum = registry.IsLoaded("cat.modupdatemenu");                  // (36, Game1.viewport.Height - 150 - 48)
                                                                                // ()
            int interval = 75 + 24;

            switch (0)
            {
                case { } when !gmcm:
                    return new(36, Game1.viewport.Height - interval);

                case { } when gmcm:
                    return mum
                        ? new(36, Game1.viewport.Height - interval * 3)
                        : new(36, Game1.viewport.Height - interval * 2);

                default:
                    return new(36, Game1.viewport.Height - interval);
            }
        }

        /// <summary>Copied from GMCM source code :D</summary>
        private bool IsTitleMenuInteractable()
        {
            if (Game1.activeClickableMenu is not TitleMenu titleMenu || TitleMenu.subMenu != null)
                return false;

            var method = this.Helper.Reflection.GetMethod(titleMenu, "ShouldAllowInteraction", false);
            if (method != null)
                return method.Invoke<bool>();
            else // method isn't available on Android
                return this.Helper.Reflection.GetField<bool>(titleMenu, "titleInPosition").GetValue();
        }
    }
}
