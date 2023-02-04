﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FontSettings.Framework.Models;
using FontSettings.Framework.Preset;

namespace FontSettings.Framework.DataAccess.Parsing
{
    internal class FontPresetParser
    {
        private readonly IFontFileProvider _fontFileProvider;
        private readonly IVanillaFontConfigProvider _vanillaFontConfigProvider;

        private readonly FontFilePathParseHelper _fontFilePathParseHelper = new();

        public FontPresetParser(IFontFileProvider fontFileProvider, IVanillaFontConfigProvider vanillaFontConfigProvider)
        {
            this._fontFileProvider = fontFileProvider;
            this._vanillaFontConfigProvider = vanillaFontConfigProvider;
        }

        public IEnumerable<FontPresetReal> Parse(FontPreset preset)
        {
            var language = new LanguageInfo(preset.Lang, preset.Locale);
            var fontType = this.ParseFontType(preset.FontType);

            var settings = new FontConfig_(
                Enabled: true,
                FontFilePath: this.ParseFontFilePath(preset.Requires.FontFileName, language, fontType),
                FontIndex: preset.FontIndex,
                FontSize: preset.FontSize,
                Spacing: preset.Spacing,
                LineSpacing: preset.LineSpacing,
                CharOffsetX: preset.CharOffsetX,
                CharOffsetY: preset.CharOffsetY,
                CharacterRanges: CharRangeSource.GetBuiltInCharRange(language));

            if (preset.PixelZoom != 0)
                settings = new BmFontConfig(
                    original: settings,
                    pixelZoom: preset.PixelZoom);

            var fontPreset = new FontPresetReal(
                language,
                fontType,
                settings);

            fontPreset = new FontPresetWithName(fontPreset, preset.Name);

            yield return fontPreset;
        }

        public FontPreset ParseBack(FontPresetReal preset)
        {
            var font = preset.Settings;

            return new FontPreset
            {
                Requires = new() { FontFileName = this.ParseBackFontFilePath(font.FontFilePath, preset.Language, preset.FontType) },
                FontType = this.ParseBackFontType(preset.FontType),
                FontIndex = font.FontIndex,
                Lang = preset.Language.Code,
                Locale = preset.Language.Locale,
                FontSize = font.FontSize,
                Spacing = font.Spacing,
                LineSpacing = (int)font.LineSpacing,
                CharOffsetX = font.CharOffsetX,
                CharOffsetY = font.CharOffsetY,
                PixelZoom = font.Supports<IWithPixelZoom>() 
                    ? font.GetInstance<IWithPixelZoom>().PixelZoom 
                    : 0,
            };
        }

        private GameFontType ParseFontType(FontPresetFontType presetFontType)
        {
            if (presetFontType == FontPresetFontType.Any)
                presetFontType = FontPresetFontType.Small;

            return (GameFontType)(int)presetFontType;
        }

        private FontPresetFontType ParseBackFontType(GameFontType fontType)
        {
            return (FontPresetFontType)(int)fontType;
        }

        private string? ParseFontFilePath(string? path, LanguageInfo language, GameFontType fontType)
            => this._fontFilePathParseHelper.ParseFontFilePath(path, this._fontFileProvider.FontFiles, this._vanillaFontConfigProvider, language, fontType);

        private string? ParseBackFontFilePath(string? path, LanguageInfo language, GameFontType fontType)
            => this._fontFilePathParseHelper.ParseBackFontFilePath(path, this._fontFileProvider.FontFiles, this._vanillaFontConfigProvider, language, fontType);
    }
}