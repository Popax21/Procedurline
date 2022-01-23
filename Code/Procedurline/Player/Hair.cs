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
        private static LinkedList<HairOverride> overrideStack = new LinkedList<HairOverride>();
        private static On.Celeste.Player.hook_Update playerUpdateHook;
        private static On.Celeste.Player.hook_UpdateHair playerHairUpdateHook;
        private static On.Celeste.PlayerHair.hook_GetHairScale hairScaleHook;
        private static List<ILHook> ilHooks = new List<ILHook>();

        internal static void Load() {
            HairOverride DetermineOverride(PlayerHair hair) {
                foreach(HairOverride o in overrideStack) {
                    if(o.TargetSelector(hair)) return o;
                }
                return null;
            }
            Color defaultNormalColor = Player.NormalHairColor, defaultTwoDashColor = Player.TwoDashesHairColor, defaultUsedColor = Player.UsedHairColor;

            //Install hooks
            On.Celeste.Player.Update += (playerUpdateHook = (On.Celeste.Player.orig_Update orig, Player player) => {
                HairOverride ov = DetermineOverride(player.Hair);
                orig(player);
                if(ov != null) {
                    //Update variables
                    player.Hair.StepInFacingPerSegment *= ov.StyleData.StepMultiplier.X;
                    player.Hair.StepYSinePerSegment *= ov.StyleData.StepMultiplier.Y;
                    player.Hair.StepApproach *= ov.StyleData.GravityMultiplier;
                }
            });

            On.Celeste.PlayerHair.GetHairScale += (hairScaleHook = (On.Celeste.PlayerHair.orig_GetHairScale orig, PlayerHair hair, int idx) => {
                HairOverride ov = DetermineOverride(hair);
                if(ov != null && idx < ov.StyleData.ScaleMultipliers.Length) {
                    return orig(hair, idx) * ov.StyleData.ScaleMultipliers[idx];
                } else return orig(hair, idx);
            });

            //Install ILHooks on all PlayerHair functions (including constructors)
            foreach(MethodBase m in typeof(PlayerHair).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Concat<MethodBase>(typeof(PlayerHair).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))) {
                if(m.DeclaringType != typeof(PlayerHair)) continue;

                ilHooks.Add(new ILHook(m, ctx => {
                    //Hook all accesses to PlayerSprite.HairCount
                    ILCursor cursor = new ILCursor(ctx);
                    while(cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld(typeof(PlayerSprite).GetField(nameof(PlayerSprite.HairCount))))) {
                        //Replace with delegate
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.EmitDelegate<Func<int, PlayerHair, int>>((origCount, hair) => {
                            HairOverride ov = DetermineOverride(hair);

                            int numNodes = ov?.StyleData.ScaleMultipliers.Length ?? origCount;
                            if(!m.IsConstructor && hair.Nodes.Count != numNodes) {
                                //Update nodes
                                while(hair.Nodes.Count > numNodes) hair.Nodes.RemoveAt(hair.Nodes.Count);
                                while(hair.Nodes.Count < numNodes) hair.Nodes.Add(hair.Nodes[hair.Nodes.Count-1]);
                            }

                            return numNodes;
                        });
                    }
                }));
            }

            //Hook Player.UpdateHair
            ilHooks.Add(new ILHook(typeof(Player).GetMethod(nameof(Player.UpdateHair), BindingFlags.Public | BindingFlags.Instance), ctx => {
                void HookColorAccess(Func<HairOverride, Color> colCb, params string[] fields) {
                    ILCursor cursor = new ILCursor(ctx);
                    while(cursor.TryGotoNext(MoveType.After, instr => fields.Any(f => instr.MatchLdsfld<Player>(f)))) {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.EmitDelegate<Func<Color, Player, Color>>((origCol, player) => {
                            HairOverride ov = DetermineOverride(player.Hair);
                            if(ov != null) return colCb(ov);
                            else return origCol;
                        });
                    }
                }

                //Hook all accesses to Player.*HairColor
                HookColorAccess(ov => ov.ColorData.NormalColor.RemoveAlpha(), nameof(Player.NormalHairColor), nameof(Player.NormalBadelineHairColor));
                HookColorAccess(ov => ov.ColorData.TwoDashesColor.RemoveAlpha(), nameof(Player.TwoDashesHairColor), nameof(Player.TwoDashesBadelineHairColor));
                HookColorAccess(ov => ov.ColorData.UsedColor.RemoveAlpha(), nameof(Player.UsedHairColor), nameof(Player.UsedBadelineHairColor));
            }));
        }

        internal static void Unload() {
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

        private LinkedListNode<HairOverride> node;

        public HairOverride(HairColorData color, HairStyleData style, TargetSelector<PlayerHair> sel) {
            ColorData = color;
            StyleData = style;
            TargetSelector = sel;

            node = overrideStack.AddFirst(this);
        }

        public void Dispose() {
            if(node != null) overrideStack.Remove(node);
            node = null;
        }

        public HairColorData ColorData { get; }
        public HairStyleData StyleData { get; }
        public TargetSelector<PlayerHair> TargetSelector { get; }
    }
}