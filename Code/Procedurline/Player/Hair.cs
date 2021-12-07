using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using MonoMod.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;
using Mono.Cecil.Cil;

namespace Celeste.Mod.Procedurline {
    public struct HairColorData {
        public Color NormalColor { get; set; }
        public Color TwoDashesColor { get; set; }
        public Color UsedColor { get; set; }
    }

    public struct HairStyleData {
        public Vector2[] ScaleMultipliers { get; set; }
        public Vector2 StepMultiplier { get; set; }
        public float GravityMultiplier { get; set; }
    }

    public class HairOverride : IDisposable {
        private static DynData<Player> dynPlayer = new DynData<Player>();

        private On.Celeste.Player.hook_Update playerUpdateHook;
        private On.Celeste.Player.hook_UpdateHair playerHairUpdateHook;
        private On.Celeste.PlayerHair.hook_GetHairScale hairScaleHook;
        private List<ILHook> ilHooks = new List<ILHook>();

        public HairOverride(HairColorData color, HairStyleData style, TargetSelector<PlayerHair> sel) {
            //Install hooks
            On.Celeste.Player.Update += (playerUpdateHook = (On.Celeste.Player.orig_Update orig, Player player) => {
                orig(player);
                if(sel(player.Hair)) {
                    //Update variables
                    player.Hair.StepInFacingPerSegment *= style.StepMultiplier.X;
                    player.Hair.StepYSinePerSegment *= style.StepMultiplier.Y;
                    player.Hair.StepApproach *= style.GravityMultiplier;
                }
            });

            On.Celeste.Player.UpdateHair += (playerHairUpdateHook = (On.Celeste.Player.orig_UpdateHair orig, Player player, bool gravity) => {
                //TODO This is kinda a hack...
                Color normalColor = Player.NormalHairColor, twoDashColor = Player.TwoDashesHairColor, usedColor = Player.UsedHairColor;
                if(sel(player.Hair)) {
                    dynPlayer.Set("NormalHairColor", color.NormalColor.RemoveAlpha());
                    dynPlayer.Set("TwoDashesHairColor", color.TwoDashesColor.RemoveAlpha());
                    dynPlayer.Set("UsedHairColor", color.UsedColor.RemoveAlpha());
                }

                orig(player, gravity);

                if(sel(player.Hair)) {
                    dynPlayer.Set("NormalHairColor", normalColor);
                    dynPlayer.Set("TwoDashesHairColor", twoDashColor);
                    dynPlayer.Set("UsedHairColor", usedColor);
                }
            });

            On.Celeste.PlayerHair.GetHairScale += (hairScaleHook = (On.Celeste.PlayerHair.orig_GetHairScale orig, PlayerHair hair, int idx) => {
                if(sel(hair) && idx < style.ScaleMultipliers.Length) {
                    return orig(hair, idx) * style.ScaleMultipliers[idx];
                } else return orig(hair, idx);
            });

            //Install ILHooks on all PlayerHair functions (including constructors)
            foreach(MethodBase m in typeof(PlayerHair).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Concat<MethodBase>(typeof(PlayerHair).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))) {
                if(m.DeclaringType != typeof(PlayerHair)) continue;

                ilHooks.Add(new ILHook(m, ctx => {
                    //Hook all accesses to PlayerSprite.HairCount
                    ILCursor cursor = new ILCursor(ctx);
                    while(cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdfld(typeof(PlayerSprite).GetField(nameof(PlayerSprite.HairCount))))) {
                        //Replace with delegate
                        cursor.Remove();
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.EmitDelegate<Func<PlayerSprite, PlayerHair, int>>((sprite, hair) => {
                            int numNodes = sel(hair) ? style.ScaleMultipliers.Length : sprite.HairCount;

                            if(!m.IsConstructor && hair.Nodes.Count != numNodes) {
                                //Update nodes
                                hair.Nodes.Clear();
                                for(int i = 0; i < numNodes; i++) hair.Nodes.Add(Vector2.Zero);
                                hair.Start();
                            }

                            return numNodes;
                        });
                    }
                }));
            }
        }

        public void Dispose() {
            //Uninstall hooks
            if(playerUpdateHook != null) On.Celeste.Player.Update -= playerUpdateHook;
            playerUpdateHook = null;

            if(playerHairUpdateHook != null) On.Celeste.Player.UpdateHair -= playerHairUpdateHook;
            playerHairUpdateHook = null;

            if(hairScaleHook != null) On.Celeste.PlayerHair.GetHairScale -= hairScaleHook;
            hairScaleHook = null;

            foreach(ILHook h in ilHooks) h.Dispose();
            ilHooks.Clear();
        }
    }
}