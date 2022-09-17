using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A subclass of <see cref="MTexture" /> which allows one to apply a scale vector when drawing
    /// </summary>
    public class ScaledMTexture : MTexture {
        public ScaledMTexture() : base() {}
        public ScaledMTexture(VirtualTexture texture) : base(texture) {}
        public ScaledMTexture(MTexture parent, int x, int y, int width, int height) : base(parent, x, y, width, height) {}
        public ScaledMTexture(MTexture parent, Rectangle clipRect) : base(parent, clipRect) {}
        public ScaledMTexture(MTexture parent, string atlasPath, Rectangle clipRect, Vector2 drawOffset, int width, int height) : base(parent, atlasPath, clipRect, drawOffset, width, height) {}
        public ScaledMTexture(MTexture parent, string atlasPath, Rectangle clipRect) : base(parent, atlasPath, clipRect) {}
        public ScaledMTexture(VirtualTexture texture, Vector2 drawOffset, int frameWidth, int frameHeight) : base(texture, drawOffset, frameWidth, frameHeight) {}

        internal static void AddHooks() {
            IL.Monocle.MTexture.Draw_Vector2 += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2 += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float_float += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float_float_SpriteEffects += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2 += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float_Rectangle += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float_SpriteEffects += DrawModifier;

            IL.Monocle.MTexture.DrawCentered_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float_float += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float_float_SpriteEffects += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2_float += DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2_float_SpriteEffects += DrawModifier;

            IL.Monocle.MTexture.DrawOutline_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float_float_SpriteEffects += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2_float_SpriteEffects += DrawModifier;

            IL.Monocle.MTexture.DrawOutlineCentered_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float_float_SpriteEffects += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2 += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2_float += DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2_float_SpriteEffects += DrawModifier;
        }

        internal static void RemoveHooks() {
            IL.Monocle.MTexture.Draw_Vector2 += DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float_float -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_float_float_SpriteEffects -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float_Rectangle -= DrawModifier;
            IL.Monocle.MTexture.Draw_Vector2_Vector2_Color_Vector2_float_SpriteEffects -= DrawModifier;

            IL.Monocle.MTexture.DrawCentered_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float_float -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_float_float_SpriteEffects -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2_float -= DrawModifier;
            IL.Monocle.MTexture.DrawCentered_Vector2_Color_Vector2_float_SpriteEffects -= DrawModifier;

            IL.Monocle.MTexture.DrawOutline_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_float_float_SpriteEffects -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutline_Vector2_Vector2_Color_Vector2_float_SpriteEffects -= DrawModifier;

            IL.Monocle.MTexture.DrawOutlineCentered_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_float_float_SpriteEffects -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2 -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2_float -= DrawModifier;
            IL.Monocle.MTexture.DrawOutlineCentered_Vector2_Color_Vector2_float_SpriteEffects -= DrawModifier;
        }

        private static void DrawModifier(ILContext ctx) {
            bool replacedDraw = false;
            
            //Replace draw calls with float scales
            MethodInfo drawFloatMethod = typeof(SpriteBatch).GetMethod(nameof(SpriteBatch.Draw), new Type[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) });
            for(ILCursor cursor = new ILCursor(ctx); cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(drawFloatMethod)); replacedDraw = true) {
                cursor.Remove();
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, float, SpriteEffects, float, MTexture>>(static (batch, tex, pos, rect, col, rot, orig, scale, eff, depth, mtex) => {
                    if(mtex is ScaledMTexture sMTex) {
                        batch.Draw(tex, pos, rect, col, rot, orig / sMTex.Scale, sMTex.Scale*scale, eff, depth);
                    } else {
                        batch.Draw(tex, pos, rect, col, rot, orig, scale, eff, depth);
                    }
                });
            }

            //Replace draw calls with Vector2 scales
            MethodInfo drawVec2Method = typeof(SpriteBatch).GetMethod(nameof(SpriteBatch.Draw), new Type[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float) });
            for(ILCursor cursor = new ILCursor(ctx); cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(drawVec2Method)); replacedDraw = true) {
                cursor.Remove();
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color, float, Vector2, Vector2, SpriteEffects, float, MTexture>>(static (batch, tex, pos, rect, col, rot, orig, scale, eff, depth, mtex) => {
                    if(mtex is ScaledMTexture sMTex) {
                        batch.Draw(tex, pos, rect, col, rot, orig / sMTex.Scale, sMTex.Scale*scale, eff, depth);
                    } else {
                        batch.Draw(tex, pos, rect, col, rot, orig, scale, eff, depth);
                    }
                });
            }

            if(!replacedDraw) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't replace SpriteBatch.Draw call for ScaledMTextures in method {ctx.Method}!");
        }

        public virtual Vector2 Scale { get; set; }
    }
}