using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace JunimoStudio.Menus.Controls
{
    internal class Checkbox : Element
    {
        public Texture2D Texture { get; set; }
        public Rectangle CheckedTextureRect { get; set; }
        public Rectangle UncheckedTextureRect { get; set; }

        public Action<Element> Callback { get; set; }

        public bool Checked { get; set; } = true;

        public Checkbox()
        {
            Texture = Game1.mouseCursors;
            CheckedTextureRect = OptionsCheckbox.sourceRectChecked;
            UncheckedTextureRect = OptionsCheckbox.sourceRectUnchecked;
        }

        public override int Width => CheckedTextureRect.Width * 4;
        public override int Height => CheckedTextureRect.Height * 4;
        public override string ClickedSound => "drumkit6";

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Clicked && Callback != null)
            {
                Checked = !Checked;
                Callback.Invoke(this);
            }
        }

        public override void Draw(SpriteBatch b)
        {
            b.Draw(Texture, Position, Checked ? CheckedTextureRect : UncheckedTextureRect, Color.White, 0, Vector2.Zero, 4, SpriteEffects.None, 0);
            Game1.activeClickableMenu?.drawMouse(b);
        }
    }
}
