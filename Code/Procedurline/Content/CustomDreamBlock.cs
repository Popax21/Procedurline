using System.Reflection;
using Microsoft.Xna.Framework;

using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomDreamBlock : DreamBlock {
        public struct DreamBlockData {
            public bool FastMoving, OneUse, Below;
            public Color DeactivatedBackColor, ActivatedBackColor;
            public Color DeactivatedLineColor, ActivatedLineColor;
        }

        private static readonly FieldInfo DreamBlock_playerHasDreamDash = typeof(DreamBlock).GetField("playerHasDreamDash", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static void Load() {
            On.Celeste.DreamBlock.ActivateNoRoutine += ActivateNoRoutineHook;
            On.Celeste.DreamBlock.DeactivateNoRoutine += DeactivateNoRoutineHook;
            IL.Celeste.DreamBlock.Render += RenderModifier;
        }

        internal static void Unload() {
            On.Celeste.DreamBlock.ActivateNoRoutine -= ActivateNoRoutineHook;
            On.Celeste.DreamBlock.DeactivateNoRoutine -= DeactivateNoRoutineHook;
            IL.Celeste.DreamBlock.Render -= RenderModifier;
        }

        private static void ActivateNoRoutineHook(On.Celeste.DreamBlock.orig_ActivateNoRoutine orig, DreamBlock dreamBlock) {
            if(dreamBlock is CustomDreamBlock cDreamBlock && !cDreamBlock.IsActivated) {
                if(cDreamBlock.OnActivate()) orig(dreamBlock);
            } else orig(dreamBlock);
        }

        private static void DeactivateNoRoutineHook(On.Celeste.DreamBlock.orig_DeactivateNoRoutine orig, DreamBlock dreamBlock) {
            if(dreamBlock is CustomDreamBlock cDreamBlock && cDreamBlock.IsActivated) {
                if(cDreamBlock.OnDeactivate()) orig(dreamBlock);
            } else orig(dreamBlock);
        }

        private static void RenderModifier(ILContext ctx) {
            //Hook all accesses to the static DreamBlock color fields
            void HookColorAccesses(string colorName, string dataFieldName) {
                FieldInfo colorField = typeof(DreamBlock).GetField(colorName, BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo dataField = typeof(CustomDreamBlock).GetField(nameof(CustomDreamBlock.Data));
                FieldInfo dataColorField = typeof(DreamBlockData).GetField(dataFieldName);

                ILCursor cursor = new ILCursor(ctx);
                while(cursor.TryGotoNext(MoveType.Before, i => i.MatchLdsfld(colorField))) {
                    ILLabel notCustom = cursor.DefineLabel(), end = cursor.DefineLabel();

                    //Check if the dream block is a custom one
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Isinst, typeof(CustomDreamBlock));
                    cursor.Emit(OpCodes.Dup);
                    cursor.Emit(OpCodes.Brfalse, notCustom);

                    //Is a custom dream block, so load color from data
                    cursor.Emit(OpCodes.Ldfld, dataField);
                    cursor.Emit(OpCodes.Ldfld, dataColorField);
                    cursor.Emit(OpCodes.Jmp, end);

                    //Not a custom dream block, so call original method
                    cursor.MarkLabel(notCustom);
                    cursor.Emit(OpCodes.Pop);
                    cursor.Index++;
                    cursor.MarkLabel(end);
                }
            }
            HookColorAccesses("disabledBackColor", nameof(DreamBlockData.DeactivatedBackColor));
            HookColorAccesses("activeBackColor", nameof(DreamBlockData.ActivatedBackColor));
            HookColorAccesses("disabledLineColor", nameof(DreamBlockData.DeactivatedLineColor));
            HookColorAccesses("activeLineColor", nameof(DreamBlockData.ActivatedLineColor));
        }

        public readonly DreamBlockData Data;

        protected CustomDreamBlock(Vector2 position, float width, float height, Vector2? node, DreamBlockData data) : base(position, width, height, node, data.FastMoving, data.OneUse, data.Below) {
            Data = data;
        }

        /// <summary>
        /// Called when your dream block gets activated. Return false if you don't want to activate it.
        /// </summary>
        protected virtual bool OnActivate() => true;

        /// <summary>
        /// Called when your dream block gets deactivated. Return false if you don't want to deactivate it.
        /// </summary>
        protected virtual bool OnDeactivate() => true;

        public bool IsActivated => (bool) DreamBlock_playerHasDreamDash.GetValue(this);
    }
}