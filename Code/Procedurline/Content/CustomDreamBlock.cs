using System;
using System.Reflection;
using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using Monocle;

namespace Celeste.Mod.Procedurline {
    [TrackedAs(typeof(DreamBlock), true)]
    public abstract class CustomDreamBlock : DreamBlock {
        public struct ParticleData {
            public Vector2 Position;
            public int Layer;
            public float TimeOffset;
            public Color Color;
        }

        public static readonly Type DreamParticleType = typeof(DreamBlock).GetNestedType("DreamParticle", BindingFlags.NonPublic);
        public static readonly FieldInfo DreamParticle_Position = DreamParticleType.GetField("Position", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo DreamParticle_Layer = DreamParticleType.GetField("Layer", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo DreamParticle_TimeOffset = DreamParticleType.GetField("TimeOffset", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo DreamParticle_Color = DreamParticleType.GetField("Color", BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// Gets a <c>DreamParticle</c>'s <see cref="ParticleData" />
        /// </summary>
        public static ParticleData GetParticleData(object particle) {
            if(!DreamParticleType.IsInstanceOfType(particle)) throw new ArgumentException("Not a DreamParticle");
            return new ParticleData() {
                Position = (Vector2) DreamParticle_Position.GetValue(particle),
                Layer = (int) DreamParticle_Layer.GetValue(particle),
                TimeOffset = (float) DreamParticle_TimeOffset.GetValue(particle),
                Color = (Color) DreamParticle_Color.GetValue(particle),
            };
        }
    
        /// <summary>
        /// Creates a <c>DreamParticle</c> from <see cref="ParticleData" />
        /// </summary>
        public static object FromParticleData(ParticleData data) {
            object particle = Activator.CreateInstance(DreamParticleType);
            DreamParticle_Position.SetValue(particle, data.Position);
            DreamParticle_Layer.SetValue(particle, data.Layer);
            DreamParticle_TimeOffset.SetValue(particle, data.TimeOffset);
            DreamParticle_Color.SetValue(particle, data.Color);
            return particle;
        }

        public struct DreamBlockData {
            public Color DeactivatedBackColor, ActivatedBackColor;
            public Color DeactivatedLineColor, ActivatedLineColor;
            public Color[][] ParticleColors;
        }

        public static readonly Color VanillaDeactivatedBackColor = Calc.HexToColor("1f2e2d");
        public static readonly Color VanillaActivatedBackColor = Color.Black;
        public static readonly Color VanillaDeactivatedLineColor = Calc.HexToColor("6a8480");
        public static readonly Color VanillaActivatedLineColor = Color.White;

        public static readonly Color[][] VanillaParticleColors = new Color[][] {
            new Color[] { Calc.HexToColor("ffef11"), Calc.HexToColor("ff00d0"), Calc.HexToColor("08a310") },
            new Color[] { Calc.HexToColor("5fcde4"), Calc.HexToColor("7fb25e"), Calc.HexToColor("e0564c") },
            new Color[] { Calc.HexToColor("5b6ee1"), Calc.HexToColor("cc3b3b"), Calc.HexToColor("7daa64") }
        };

        private static readonly FieldInfo Player_dreamBlock = typeof(Player).GetField("dreamBlock", BindingFlags.NonPublic | BindingFlags.Instance);

        [ContentILHookAttribute("Render")]
        private static void RenderModifier(ILContext ctx) {
            //Hook all accesses to the static DreamBlock color fields
            void HookColorAccesses(string colorName, string dataFieldName) {
                FieldInfo colorField = typeof(DreamBlock).GetField(colorName, BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo dataField = typeof(CustomDreamBlock).GetField(nameof(CustomDreamBlock.Data));
                FieldInfo dataColorField = typeof(DreamBlockData).GetField(dataFieldName);

                bool didPatch = false;
                ILCursor cursor = new ILCursor(ctx);
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(colorField))) {
                    ILLabel notCustom = cursor.DefineLabel();

                    //Check if the dream block is a custom one
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Is a custom dream block, so load color from data
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Ldfld, dataField);
                    cursor.Emit(OpCodes.Ldfld, dataColorField);

                    cursor.MarkLabel(notCustom);
                    didPatch = true;
                }
                if(!didPatch) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock color '{colorName}'!");
            }
            HookColorAccesses("disabledBackColor", nameof(DreamBlockData.DeactivatedBackColor));
            HookColorAccesses("activeBackColor", nameof(DreamBlockData.ActivatedBackColor));
            HookColorAccesses("disabledLineColor", nameof(DreamBlockData.DeactivatedLineColor));
            HookColorAccesses("activeLineColor", nameof(DreamBlockData.ActivatedLineColor));
        }

        [ContentILHookAttribute("Celeste.Player", "DreamDashBegin")]
        private static void DreamDashBeginModifier(ILContext ctx) {
            VariableDefinition blockVar = new VariableDefinition(ctx.Import(typeof(CustomDreamBlock)));
            ctx.Body.Variables.Add(blockVar);

            void PatchSFX(string sfx, PropertyInfo sfxProp) {
                bool didPatch = false;
                ILCursor cursor = new ILCursor(ctx);
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchLdstr(sfx))) {
                    ILLabel notCustom = cursor.DefineLabel(), gotDreamBlock = cursor.DefineLabel();

                    //Load the dream block variable and check if it's null
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Dup);
                    cursor.Emit(OpCodes.Brtrue, gotDreamBlock);

                    //Try to collide with custom dream block and store it in the variable
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Call, typeof(Entity).GetMethod(nameof(Entity.CollideFirst), Type.EmptyTypes).MakeGenericMethod(typeof(DreamBlock)));
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Dup);
                    cursor.Emit(OpCodes.Stloc, blockVar);

                    //Check if we got a custom dream block
                    cursor.MarkLabel(gotDreamBlock);
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Replace SFX with the SFX property's value
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Callvirt, sfxProp.GetMethod);

                    cursor.MarkLabel(notCustom);
                    didPatch = true;
                }
                if(!didPatch) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock SFX '{sfx}'!");
            }
            PatchSFX("event:/char/madeline/dreamblock_enter", typeof(CustomDreamBlock).GetProperty(nameof(EnterSFX), PatchUtils.BindAllInstance));
            PatchSFX("event:/char/madeline/dreamblock_travel", typeof(CustomDreamBlock).GetProperty(nameof(TravelSFX), PatchUtils.BindAllInstance));
        }

        [ContentILHookAttribute("Celeste.Player", "DreamDashEnd")]
        private static void DreamDashEndModifer(ILContext ctx) {
            VariableDefinition blockVar = new VariableDefinition(ctx.Import(typeof(CustomDreamBlock)));
            ctx.Body.Variables.Add(blockVar);

            ILCursor cursor = new ILCursor(ctx);

            //Store the current custom dream block reference before the field is reset
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
            cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
            cursor.Emit(OpCodes.Stloc, blockVar);

            //Patch call to OnPlayerExit
            {
                bool didPatchPlayerExit = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(typeof(DreamBlock).GetMethod(nameof(DreamBlock.OnPlayerExit))))) {
                    ILLabel notCustom = cursor.DefineLabel();

                    //Check if we got a custom dream block
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Call OnExit
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldnull);
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetMethod(nameof(OnExit), PatchUtils.BindAllInstance));

                    cursor.MarkLabel(notCustom);
                    cursor.Index++;
                    didPatchPlayerExit = true;
                }
                if(!didPatchPlayerExit) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock OnPlayerExit call!");
            }

            //Patch exit SFX
            {
                bool didPatchSFX = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchLdstr("event:/char/madeline/dreamblock_exit"))) {
                    ILLabel notCustom = cursor.DefineLabel();

                    //Check if we got a custom dream block
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Replace SFX with the SFX property's value
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldloc, blockVar);
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetProperty(nameof(ExitSFX), PatchUtils.BindAllInstance).GetMethod);

                    cursor.MarkLabel(notCustom);
                    didPatchSFX = true;
                }
                if(!didPatchSFX) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock exit SFX!");
            }
        }

        [ContentILHookAttribute("Celeste.Player", "DreamDashUpdate")]
        private static void DreamDashUpdateModifer(ILContext ctx) {
            VariableDefinition prevBlockVar = new VariableDefinition(ctx.Import(typeof(DreamBlock)));
            VariableDefinition prevPosVar = new VariableDefinition(ctx.Import(typeof(Vector2)));
            ctx.Body.Variables.Add(prevBlockVar);
            ctx.Body.Variables.Add(prevPosVar);

            ILCursor cursor = new ILCursor(ctx);

            //Store the current dream block reference before the field is changed
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
            cursor.Emit(OpCodes.Stloc, prevBlockVar);

            //Store the current player position before it's updated
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(Player).GetField(nameof(Player.Position)));
            cursor.Emit(OpCodes.Stloc, prevPosVar);

            //Add call to UpdatePlayer
            {
                ILLabel notCustom = cursor.DefineLabel();

                //Check if we are in a custom dream block
                cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                cursor.Emit(OpCodes.Brfalse, notCustom);

                //Call UpdatePlayer
                cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetMethod(nameof(UpdatePlayer), PatchUtils.BindAllInstance));

                cursor.MarkLabel(notCustom);
            }

            //NOP out regular update logic for custom dream blocks
            void NOPCall(MethodInfo method) {
                bool didPatch = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(method))) {
                    ILLabel notCustom = cursor.DefineLabel(), end = cursor.DefineLabel();

                    //Check if we are in a custom dream block
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Pop arguments
                    int numParams = method.GetParameters().Length;
                    if(!method.IsStatic) numParams++;
                    for(int i = 0; i < numParams; i++) cursor.Emit(OpCodes.Pop);

                    cursor.Emit(OpCodes.Br, end); 

                    cursor.MarkLabel(notCustom);
                    cursor.Index++;
                    cursor.MarkLabel(end);
                    didPatch = true;
                }
                if(!didPatch) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock call of '{method}'!");
            }
            NOPCall(typeof(Player).GetMethod(nameof(Player.NaiveMove)));
            NOPCall(typeof(Input).GetMethod(nameof(Input.Rumble)));

            //Patch the dreamBlock field assignment
            {
                bool didPatchDreamBlockChange = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchStfld(Player_dreamBlock))) {
                    ILLabel prevNotCustom = cursor.DefineLabel(), newNotCustom = cursor.DefineLabel(), noChange = cursor.DefineLabel();

                    //Check if the dream block has changed
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Ceq);
                    cursor.Emit(OpCodes.Brtrue, noChange);

                    //Check if we previously were in a custom dream block
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, prevNotCustom);

                    //Call OnExit
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetMethod(nameof(OnExit), PatchUtils.BindAllInstance));

                    //Check if we've entered a custom dream block
                    cursor.MarkLabel(prevNotCustom);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, newNotCustom);

                    //Update the looping travel SFX
                    cursor.Emit(OpCodes.Ldarg_0);

                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, typeof(Player).GetField("dreamSfxLoop", BindingFlags.NonPublic | BindingFlags.Instance));

                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetProperty(nameof(TravelSFX), PatchUtils.BindAllInstance).GetMethod);

                    cursor.Emit(OpCodes.Call, typeof(Player).GetMethod(nameof(Player.Loop)));

                    //Call OnEnter
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetMethod(nameof(OnEnter), PatchUtils.BindAllInstance));

                    //Update the previous dream block variable
                    cursor.MarkLabel(newNotCustom);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, Player_dreamBlock);
                    cursor.Emit(OpCodes.Stloc, prevBlockVar);

                    cursor.MarkLabel(noChange);
                    didPatchDreamBlockChange = true;
                }
                if(!didPatchDreamBlockChange) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock switchover!");
            }

            //Patch bounce check
            {
                bool didPatchBounceCheck = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt(typeof(Player).GetMethod("DreamDashedIntoSolid", BindingFlags.NonPublic | BindingFlags.Instance)))) {
                    int prevIdx = cursor.Index;
                    if(!cursor.TryGotoNext(MoveType.After, i => i.MatchLdfld(typeof(Assists).GetField(nameof(Assists.Invincible))))) {
                        cursor.Index = prevIdx;
                        continue;
                    }
                    ILLabel notCustom = cursor.DefineLabel();

                    //Check if we previously were in a custom dream block
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Call OnCollideSolid
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldloc, prevPosVar);
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetMethod(nameof(OnCollideSolid), PatchUtils.BindAllInstance));
                    cursor.Emit(OpCodes.Ret);

                    cursor.MarkLabel(notCustom);
                    cursor.Index = prevIdx;
                    didPatchBounceCheck = true;
                }
                if(!didPatchBounceCheck) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock bounce check!");
            }

            //Patch bounce SFX
            {
                bool didPatchSFX = false;
                cursor.Index = 0;
                while(cursor.TryGotoNext(MoveType.After, i => i.MatchLdstr("event:/game/general/assist_dreamblockbounce"))) {
                    ILLabel notCustom = cursor.DefineLabel();

                    //Check if we previously were in a custom dream block
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Replace SFX with the SFX property's value
                    cursor.Emit(OpCodes.Pop);
                    cursor.Emit(OpCodes.Ldloc, prevBlockVar);
                    cursor.Emit(OpCodes.Castclass, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Callvirt, typeof(CustomDreamBlock).GetProperty(nameof(BounceSFX), PatchUtils.BindAllInstance).GetMethod);

                    cursor.MarkLabel(notCustom);
                    didPatchSFX = true;
                }
                if(!didPatchSFX) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Couldn't patch DreamBlock bounce SFX!");
            }
        }

        public readonly bool FastMoving, OneUse, Below;
        public DreamBlockData Data;

        protected CustomDreamBlock(Vector2 position, float width, float height, Vector2? node, bool fastMoving, bool oneUse, bool below, DreamBlockData data) : base(position, width, height, node, fastMoving, oneUse, below) {
            FastMoving = fastMoving;
            OneUse = oneUse;
            Below = below;
            Data = data;
        }

        /// <summary>
        /// Creates particles for the dream block. By default, this matches the vanilla behaviour.
        /// </summary>
        protected virtual ParticleData[] CreateParticles() {
            ParticleData[] particles = new ParticleData[(int) ((Width / 8f) * (Height / 8f) * 0.7f)];
            for(int i = 0; i < particles.Length; i++) {
                particles[i].Position = new Vector2(Calc.Random.NextFloat(Width), Calc.Random.NextFloat(Height));
                particles[i].Layer = Calc.Random.Choose(0, 1, 1, 2, 2, 2);
                particles[i].TimeOffset = Calc.Random.NextFloat();
                particles[i].Color = Color.LightGray * (0.5f + particles[i].Layer / 2);
                if(IsActivated) particles[i].Color = Calc.Random.Choose(Data.ParticleColors[particles[i].Layer]);
            }
            return particles;
        }

        /// <summary>
        /// Called when the player enters the dream block
        /// </summary>
        protected virtual void OnEnter(Player player, DreamBlock fromDreamBlock) {}

        /// <summary>
        /// Called when the player exits the dream block
        /// </summary>
        protected virtual void OnExit(Player player, DreamBlock toDreamBlock) {}

        /// <summary>
        /// Called when the player hits a solid while in the dream block
        /// </summary>
        /// <returns>
        /// The state the player should enter
        /// </returns>
        protected virtual int OnCollideSolid(Player player, Vector2 oldPos) {
            if(SaveData.Instance.Assists.Invincible) {
                player.Position = oldPos;
                player.Speed *= -1f;
                player.Play(BounceSFX, null, 0f);
            } else {
                player.Die(Vector2.Zero, false, true);
            }
            return Player.StDreamDash;
        }

        /// <summary>
        /// Called to run the player's update logic while in the dream block. By default, this executes vanilla movement code.
        /// </summary>
        protected virtual void UpdatePlayer(Player player) {
            Input.Rumble(RumbleStrength.Light, RumbleLength.Medium);
            player.NaiveMove(player.Speed * Engine.DeltaTime);
        }

        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new IEnumerator Activate() => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new IEnumerator FastActivate() => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void ActivateNoRoutine() {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new IEnumerator Deactivate() => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new IEnumerator FastDeactivate() => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void DeactivateNoRoutine() {}

        [ContentVirtualize(false)] protected virtual new void Setup() {
            ParticleData[] pdata = CreateParticles();

            Particles = Array.CreateInstance(DreamParticleType, pdata.Length);
            for(int i = 0; i < pdata.Length; i++) Particles.SetValue(FromParticleData(pdata[i]), i);
        }
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void OnPlayerExit(Player player) {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void OneUseDestroy() {}

        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual float LineAmplitude(float seed, float index) => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void WobbleLine(Vector2 from, Vector2 to, float offset) {}

        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new bool BlockedCheck() => default;
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual bool TryActorWiggleUp(Entity entity) => default;

        protected virtual string EnterSFX => "event:/char/madeline/dreamblock_enter";
        protected virtual string TravelSFX => "event:/char/madeline/dreamblock_travel";
        protected virtual string BounceSFX => "event:/game/general/assist_dreamblockbounce";
        protected virtual string ExitSFX => "event:/char/madeline/dreamblock_exit";

        [ContentFieldProxy("playerHasDreamDash")] public bool IsActivated { [MethodImpl(MethodImplOptions.NoInlining)] get; }

        [ContentFieldProxy("whiteFill")] protected float WhiteFill { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
        [ContentFieldProxy("whiteHeight")] protected float WhiteHeight { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }

        [ContentFieldProxy("particles")] protected Array Particles { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
        [ContentFieldProxy("particleTextures")] protected MTexture[] ParticleTextures { [MethodImpl(MethodImplOptions.NoInlining)] get; }
    }
}