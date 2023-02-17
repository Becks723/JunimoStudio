﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FontSettings.Framework.Models;

namespace FontSettings.Framework
{
    internal interface IVanillaFontConfigProvider
    {
        FontConfig GetVanillaFontConfig(LanguageInfo language, GameFontType fontType);
    }

    internal class VanillaFontConfigProvider : IVanillaFontConfigProvider
    {
        private readonly IDictionary<FontConfigKey, FontConfig> _vanillaFontsLookup;
        private readonly IVanillaFontProvider _vanillaFontProvider;

        public VanillaFontConfigProvider(IDictionary<FontConfigKey, FontConfig> vanillaFonts, IVanillaFontProvider vanillaFontProvider)
        {
            this._vanillaFontsLookup = vanillaFonts;
            this._vanillaFontProvider = vanillaFontProvider;
        }

        public FontConfig GetVanillaFontConfig(LanguageInfo language, GameFontType fontType)
        {
            if (this._vanillaFontsLookup.TryGetValue(new FontConfigKey(language, fontType), out FontConfig value))
                return value;

            return this.CreateFallbackFontConfig(language, fontType);
        }

        private FontConfig CreateFallbackFontConfig(LanguageInfo language, GameFontType fontType)
        {
            if (fontType != GameFontType.SpriteText)
                return new FontConfig(
                    Enabled: true,
                    FontFilePath: null,
                    FontIndex: 0,
                    FontSize: 26,
                    Spacing: 0,
                    LineSpacing: 26,
                    CharOffsetX: 0,
                    CharOffsetY: 0,
                    CharacterRanges: this._vanillaFontProvider.GetVanillaCharacterRanges(language, fontType));

            else
                return new BmFontConfig(
                    Enabled: true,
                    FontFilePath: null,
                    FontIndex: 0,
                    FontSize: 26,
                    Spacing: 0,
                    LineSpacing: 26,
                    CharOffsetX: 0,
                    CharOffsetY: 0,
                    CharacterRanges: this._vanillaFontProvider.GetVanillaCharacterRanges(language, fontType),
                    PixelZoom: FontHelpers.GetDefaultFontPixelZoom());
        }
    }
}
