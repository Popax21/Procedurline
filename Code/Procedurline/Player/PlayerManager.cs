using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Manages player related processors like hair color processors.
    /// </summary>
    public sealed class PlayerManager : GameComponent {
        private static readonly Dictionary<string, PlayerAnimMetadata> PlayerSprite_FrameMetadata = (Dictionary<string, PlayerAnimMetadata>) typeof(PlayerSprite).GetField("FrameMetadata", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        public readonly CompositeDataProcessor<Player, VoidBox, PlayerHairSettingsData> HairSettingsProcessor;
        public readonly CompositeDataProcessor<Player, VoidBox, PlayerHairColorData> HairColorProcessor;
        public readonly CompositeDataProcessor<PlayerHair, int, PlayerHairNodeData> HairNodeProcessor;

        private readonly DisposablePool hookPool = new DisposablePool();
        [ThreadStatic] private int hairGetterHooksBypassCounter;

        public PlayerManager(Game game) : base(game) {
            game.Components.Add(this);

            //Create processors
            HairSettingsProcessor = new CompositeDataProcessor<Player, VoidBox, PlayerHairSettingsData>();
            HairColorProcessor = new CompositeDataProcessor<Player, VoidBox, PlayerHairColorData>();
            HairNodeProcessor = new CompositeDataProcessor<PlayerHair, int, PlayerHairNodeData>();

            HairSettingsProcessor.AddProcessor(int.MinValue, new DelegateDataProcessor<Player, VoidBox, PlayerHairSettingsData>(registerScopes: (_, k) => ProcedurlineModule.PlayerScope.RegisterKey(k)));
            HairColorProcessor.AddProcessor(int.MinValue, new DelegateDataProcessor<Player, VoidBox, PlayerHairColorData>(registerScopes: (_, k) => ProcedurlineModule.PlayerScope.RegisterKey(k)));
            HairNodeProcessor.AddProcessor(int.MinValue, new DelegateDataProcessor<PlayerHair, int, PlayerHairNodeData>(registerScopes: (_, k) => ProcedurlineModule.PlayerScope.RegisterKey(k)));

            using(new DetourContext(ProcedurlineModule.HOOK_PRIO)) {
                //Install player sprite hooks
                On.Celeste.PlayerSprite.ctor += PlayerSpriteCtorHook;

                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.Running)).GetGetMethod(), (Func<Func<PlayerSprite, bool>, PlayerSprite, bool>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.IsRunning ?? orig(sprite);
                })));
                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.DreamDashing)).GetGetMethod(), (Func<Func<PlayerSprite, bool>, PlayerSprite, bool>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.IsDreamDashing ?? orig(sprite);
                })));
                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.CarryYOffset)).GetGetMethod(), (Func<Func<PlayerSprite, float>, PlayerSprite, float>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.CarryYOffset ?? orig(sprite);
                })));
                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.HasHair)).GetGetMethod(), (Func<Func<PlayerSprite, bool>, PlayerSprite, bool>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.HasHair ?? orig(sprite);
                })));
                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.HairOffset)).GetGetMethod(), (Func<Func<PlayerSprite, Vector2>, PlayerSprite, Vector2>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.HairOffset ?? orig(sprite);
                })));
                hookPool.Add(new Hook(typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.HairFrame)).GetGetMethod(), (Func<Func<PlayerSprite, int>, PlayerSprite, int>) ((orig, sprite) => {
                    return GetPlayerSpriteFrame(sprite)?.HairFrame ?? orig(sprite);
                })));

                //Install player hair related hooks
                On.Celeste.Player.Update += PlayerUpdateHook;
                IL.Celeste.Player.UpdateHair += HairColorAccessModifier;
                IL.Celeste.Player.GetTrailColor += HairColorAccessModifier;

                IL.Celeste.PlayerHair.Render += HairRenderModifier;
                On.Celeste.PlayerHair.GetHairColor += HairNodeColorHook;
                On.Celeste.PlayerHair.GetHairScale += HairNodeScaleHook;
                On.Celeste.PlayerHair.GetHairTexture += HairNodeTextureHook;
            }
        }

        protected override void Dispose(bool disposing) {
            //Uninstall hooks
            On.Celeste.PlayerSprite.ctor -= PlayerSpriteCtorHook;

            hookPool.Dispose();

            On.Celeste.Player.Update -= PlayerUpdateHook;
            IL.Celeste.Player.UpdateHair -= HairColorAccessModifier;
            IL.Celeste.Player.GetTrailColor -= HairColorAccessModifier;

            IL.Celeste.PlayerHair.Render -= HairRenderModifier;
            On.Celeste.PlayerHair.GetHairColor -= HairNodeColorHook;
            On.Celeste.PlayerHair.GetHairScale -= HairNodeScaleHook;
            On.Celeste.PlayerHair.GetHairTexture -= HairNodeTextureHook;

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        /// <summary>
        /// Returns the <see cref="PlayerHairNodeData" /> for a given node index.
        /// </summary>
        public PlayerHairNodeData GetHairNodeData(PlayerHair hair, int idx) {
            //Get the unprocessed node data
            PlayerHairNodeData nodeData;
            hairGetterHooksBypassCounter++;
            try {
                nodeData = new PlayerHairNodeData() {
                    Color = hair.GetHairColor(idx),
                    Scale = hair.GetHairScale(idx),
                    Texture = hair.GetHairTexture(idx)
                };
            } finally {
                hairGetterHooksBypassCounter--;
            }

            //Process the data
            HairNodeProcessor.ProcessData(hair, null, idx, ref nodeData);
            return nodeData;
        }

        private PlayerSpriteAnimationFrameData? GetPlayerSpriteFrame(PlayerSprite sprite) {
            string curAnim = sprite.CurrentAnimationID ?? sprite.LastAnimationID;
            if(curAnim == null || !(sprite.GetProcessedAnimation(curAnim) is Sprite.Animation anim)) return null;
            if(sprite.CurrentAnimationFrame < 0 || anim.Frames.Length <= sprite.CurrentAnimationFrame) return null;
            return (anim as IPlayerSpriteAnimation)?.GetPlayerAnimationMetadata(sprite.CurrentAnimationFrame);
        }

        private void PlayerSpriteCtorHook(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite sprite, PlayerSpriteMode mode) {
            orig(sprite, mode);

            //Replace Sprite.Animation instances with PlayerSpriteAnimations
            foreach(string animId in sprite.Animations.Keys.ToArray()) {
                Sprite.Animation anim = sprite.Animations[animId];
                if(anim.GetType() != typeof(Sprite.Animation)) {
                    Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Player animation '{animId}' isn't a vanilla Sprite.Animation - potential mod conflict! [type {anim.GetType()}] ");
                    continue;
                }

                sprite.Animations[animId] = new PlayerSpriteAnimation() {
                    Goto = anim.Goto,
                    Delay = anim.Delay,
                    Frames = anim.Frames,
                    PlayerFrameData = anim.Frames.Select(f => PlayerSprite_FrameMetadata.TryGetValue(f.AtlasPath, out PlayerAnimMetadata md) ? new PlayerSpriteAnimationFrameData(animId, md) : default).ToArray()
                };
            }
        }

        private void PlayerUpdateHook(On.Celeste.Player.orig_Update orig, Player player) {
            orig(player);

            //Process hair settings
            PlayerHairSettingsData data = new PlayerHairSettingsData() {
                NodeCount = player.Sprite?.HairCount ?? 0,
                StepPerSegment = player.Hair?.StepPerSegment ?? Vector2.Zero,
                StepInFacingPerSegment = player.Hair?.StepInFacingPerSegment ?? 0,
                StepApproach = player.Hair?.StepApproach ?? 0,
                StepYSinePerSegment = player.Hair?.StepYSinePerSegment ?? 0
            };

            if(!HairSettingsProcessor.ProcessData(player, null, default, ref data)) return;

            if(player.Sprite != null) player.Sprite.HairCount = data.NodeCount;
            if(player.Hair != null) {
                player.Hair.StepPerSegment = data.StepPerSegment;
                player.Hair.StepInFacingPerSegment = data.StepInFacingPerSegment;
                player.Hair.StepApproach = data.StepApproach;
                player.Hair.StepYSinePerSegment = data.StepYSinePerSegment;
            }
        }

        private void HairColorAccessModifier(ILContext ctx) {
            //Insert color data processor invocation at start of method
            VariableDefinition colorDataVar = new VariableDefinition(ctx.Import(typeof(PlayerHairColorData?)));
            ctx.Body.Variables.Add(colorDataVar);
            {
                ILCursor cursor = new ILCursor(ctx);
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate((Func<Player, PlayerHairColorData?>) (player => {
                    PlayerHairColorData colorData = new PlayerHairColorData() {
                        UsedColor = Player.UsedHairColor, NormalColor = Player.NormalHairColor, TwoDashesColor = Player.TwoDashesHairColor,
                        BadelineUsedColor = Player.UsedBadelineHairColor, BadelineNormalColor = Player.NormalBadelineHairColor, BadelineTwoDashesColor = Player.TwoDashesBadelineHairColor
                    };
                    if(!HairColorProcessor.ProcessData(player, null, default, ref colorData)) return null;
                    return colorData;
                }));
                cursor.Emit(OpCodes.Stloc, colorDataVar);
            }

            //Hook all accesses to Player.xxxxxHairColor
            MethodInfo hasValueMth = typeof(PlayerHairColorData?).GetProperty("HasValue").GetGetMethod();
            MethodInfo valueMth = typeof(PlayerHairColorData?).GetProperty("Value").GetGetMethod();

            void HookColorAccesses(string colorFieldName, string colorDataFieldName) {
                FieldInfo colorField = typeof(Player).GetField(colorFieldName, BindingFlags.Public | BindingFlags.Static);
                FieldInfo colorDataField = typeof(PlayerHairColorData).GetField(colorDataFieldName, BindingFlags.Public | BindingFlags.Instance);

                ILCursor cursor = new ILCursor(ctx);
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(colorField))) {
                    ILLabel useDefaultLabel = cursor.DefineLabel();

                    //Check if the stored color data local isn't null
                    cursor.Emit(OpCodes.Ldloca, colorDataVar);
                    cursor.Emit(OpCodes.Call, hasValueMth);
                    cursor.Emit(OpCodes.Brfalse_S, useDefaultLabel);

                    //It isn't, so replace the color on the stack with the one stored in it
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldloca, colorDataVar);
                    cursor.Emit(OpCodes.Call, valueMth);
                    cursor.Emit(OpCodes.Ldfld, colorDataField);

                    cursor.MarkLabel(useDefaultLabel);
                }
            }

            HookColorAccesses(nameof(Player.UsedHairColor), nameof(PlayerHairColorData.UsedColor));
            HookColorAccesses(nameof(Player.NormalHairColor), nameof(PlayerHairColorData.NormalColor));
            HookColorAccesses(nameof(Player.TwoDashesHairColor), nameof(PlayerHairColorData.TwoDashesColor));
            HookColorAccesses(nameof(Player.UsedBadelineHairColor), nameof(PlayerHairColorData.BadelineUsedColor));
            HookColorAccesses(nameof(Player.NormalBadelineHairColor), nameof(PlayerHairColorData.BadelineNormalColor));
            HookColorAccesses(nameof(Player.TwoDashesBadelineHairColor), nameof(PlayerHairColorData.BadelineTwoDashesColor));
        }

        private void HairRenderModifier(ILContext ctx) {
            //Observation: All invocations of hair node data getters are grouped
            //So cache last queried hair node data
            VariableDefinition cachedIdxVar = new VariableDefinition(ctx.Import(typeof(int)));
            VariableDefinition cachedDataVar = new VariableDefinition(ctx.Import(typeof(PlayerHairNodeData)));
            ctx.Body.Variables.Add(cachedIdxVar);
            ctx.Body.Variables.Add(cachedDataVar);

            ILCursor cursor = new ILCursor(ctx);
            cursor.Emit(OpCodes.Ldc_I4, -1);
            cursor.Emit(OpCodes.Stloc, cachedIdxVar);

            FieldInfo colorField = typeof(PlayerHairNodeData).GetField(nameof(PlayerHairNodeData.Color));
            FieldInfo scaleField = typeof(PlayerHairNodeData).GetField(nameof(PlayerHairNodeData.Scale));
            FieldInfo textureField = typeof(PlayerHairNodeData).GetField(nameof(PlayerHairNodeData.Texture));

            while(cursor.TryGotoNext(i => i.OpCode == OpCodes.Call)) {
                MethodReference method = (MethodReference) cursor.Instrs[cursor.Index].Operand;

                if(!method.DeclaringType.Is(typeof(PlayerHair))) {
                    cursor.Index++;
                    continue;
                }

                void EmitCacheQuery() {
                    ILLabel noUpdate = cursor.DefineLabel(), end = cursor.DefineLabel();

                    //Check currently cached index
                    cursor.Emit(OpCodes.Dup);
                    cursor.Emit(OpCodes.Ldloc, cachedIdxVar);
                    cursor.Emit(OpCodes.Ceq);
                    cursor.Emit(OpCodes.Brtrue, noUpdate);

                    //Update cached value
                    cursor.EmitDelegate<Func<PlayerHair, int, PlayerHairNodeData>>(GetHairNodeData);
                    cursor.Emit(OpCodes.Stloc, cachedDataVar);

                    //No cache update required
                    cursor.MarkLabel(noUpdate);
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Pop);

                    //Common end branch
                    cursor.MarkLabel(end);
                    cursor.Emit(OpCodes.Ldloc, cachedDataVar);
                }

                switch(method.Name) {
                    case "GetHairColor": {
                        EmitCacheQuery();
                        cursor.Emit(OpCodes.Ldfld, colorField);
                    } break;
                    case "GetHairScale": {
                        EmitCacheQuery();
                        cursor.Emit(OpCodes.Ldfld, scaleField);
                    } break;
                    case "GetHairTexture": {
                        EmitCacheQuery();
                        cursor.Emit(OpCodes.Ldfld, textureField);
                    } break;
                    default: {
                        cursor.Index++;
                        continue;
                    }
                }
            }
        }

        private Color HairNodeColorHook(On.Celeste.PlayerHair.orig_GetHairColor orig, PlayerHair hair, int idx) {
            if(hairGetterHooksBypassCounter > 0) return orig(hair, idx);
            return GetHairNodeData(hair, idx).Color;
        }

        private Vector2 HairNodeScaleHook(On.Celeste.PlayerHair.orig_GetHairScale orig, PlayerHair hair, int idx) {
            if(hairGetterHooksBypassCounter > 0) return orig(hair, idx);
            return GetHairNodeData(hair, idx).Scale;
        }

        private MTexture HairNodeTextureHook(On.Celeste.PlayerHair.orig_GetHairTexture orig, PlayerHair hair, int idx) {
            if(hairGetterHooksBypassCounter > 0) return orig(hair, idx);
            return GetHairNodeData(hair, idx).Texture;
        }
    }
}