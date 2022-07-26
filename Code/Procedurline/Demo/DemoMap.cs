using System;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Text;

namespace Celeste.Mod.Procedurline.Demo {
    public static class DemoMap {
        public static int DemoMapID = -1;

        [ContentInit]
        private static void Init() => Everest.Events.Level.OnLoadLevel += LevelLoadHandler;

        [ContentUninit]
        private static void Uninit() => Everest.Events.Level.OnLoadLevel += LevelLoadHandler;

        private static void LevelLoadHandler(Level level, Player.IntroTypes playerIntro, bool isFromLoader) {
            if(level.Session.Area.ID != DemoMapID) return;

            //Add backdrops
            level.Foreground.Backdrops.Add(new Parallax(GFX.Misc["purplesunset"]) { Renderer = level.Foreground, Scroll = Vector2.One * 0.5f, Speed = Vector2.One * 4f, LoopX = true, LoopY = true, Alpha = 0.5f });
            level.Foreground.Backdrops.Add(new Parallax(GFX.Misc["mist"]) { Renderer = level.Foreground, Scroll = Vector2.One * 0.5f, Speed = new Vector2(16f, 8f), LoopX = true, LoopY = true, Alpha = 0.3f });
            level.Background.Backdrops.Add(new Parallax(GFX.Game["bgs/04/bg0"]) { Renderer = level.Background, Scroll = Vector2.UnitX * 0.2f, Speed = Vector2.UnitX * -16f, LoopX = true });
            level.Background.Backdrops.Add(new Parallax(GFX.Game["bgs/04/bg1"]) { Renderer = level.Background, Scroll = Vector2.UnitX * 0.4f, Speed = Vector2.UnitX * -24f, LoopX = true });

            //Add entities
            float entityX = level.Bounds.Left;
            float entityY = level.Bounds.Bottom - 8f * 8;
            level.Entities.Add(new DemoBooster(new Vector2(entityX + 4f * 8, entityY)));
        }

        [ContentILHook("Celeste.Mod.UI.OuiHelper_ChapterSelect_LevelSet", "Enter", true)]
        private static void ChapterSelectModifier(ILContext ctx) {
            ILCursor cursor = new ILCursor(ctx);

            //Find the variable storing the current AreaData index
            MethodInfo AreaData_Get = typeof(AreaData).GetMethod(nameof(AreaData.Get), new Type[] { typeof(int) });
            cursor.GotoNext(MoveType.Before, i => i.MatchLdloc(out _), i => i.MatchCall(AreaData_Get));
            VariableReference idVar = (VariableReference) cursor.Instrs[cursor.Index].Operand;

            //Hook calls to string.op_Inequality
            while(cursor.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt(typeof(string).GetMethod("op_Inequality")))) {
                ILLabel idMismatch = cursor.DefineLabel();

                //Check if the current area ID is the Procedurline demo map
                cursor.Emit(OpCodes.Ldloc, idVar);
                cursor.Emit(OpCodes.Ldsfld, typeof(DemoMap).GetField(nameof(DemoMap.DemoMapID)));
                cursor.Emit(OpCodes.Ceq);
                cursor.Emit(OpCodes.Brfalse, idMismatch);

                //Force the result to be false
                cursor.Emit(OpCodes.Pop);
                cursor.Emit(OpCodes.Ldc_I4_0);

                cursor.MarkLabel(idMismatch);
            }
        }

        [ContentHook("Celeste.AreaData", "Load")]
        private static void AreaDataLoadHook(On.Celeste.AreaData.orig_Load orig) {
            orig();

            //Build area data
            AreaData area = new AreaData() {
                ID = DemoMapID = AreaData.Areas.Count,
                Name = "pl_demo_map",
                Icon = "areas/null",
                Interlude_Safe = true,
                IntroType = Player.IntroTypes.WakeUp,
                Wipe = (lvl, wipeIn, onComplete) => new AngledWipe(lvl, wipeIn, onComplete),
                Mode = new ModeProperties[3] {
                    new ModeProperties() {
                        PoemID = null,
                        Path = "pl_demo_map",
                        Checkpoints = null,
                        Inventory = PlayerInventory.Default,
                        AudioState = new AudioState(SFX.music_city, SFX.NONE)
                    },
                    null,
                    null
                }
            };
            area.SetSID("Procedurline/pl_demo_map");
            AreaData.Areas.Add(area);

            //Build level data
            LevelData level = new LevelData(new BinaryPacker.Element() { Attributes = new Dictionary<string, object>(), Children = new List<BinaryPacker.Element>() });
            level.Name = "demo";
            level.Music = SFX.music_city;
            level.MusicLayers = new float[] { 1f, 1f, 1f, 1f };
            level.Bounds = new Rectangle(0, 0, 8*64, 8*32);
            level.Spawns.Add(new Vector2(level.Bounds.Left + 4, level.Bounds.Bottom - 8*4));

            //Setup solids
            int tileWidth = level.TileBounds.Width, tileHeight = level.TileBounds.Height;
            StringBuilder solidsBuilder = new StringBuilder();
            for(int y = 0; y < tileHeight; y++) {
                for(int x = 0; x < tileWidth; x++) {
                    if(y >= tileHeight - 4) solidsBuilder.Append('a');
                }
                solidsBuilder.AppendLine();
            }
            level.Solids = solidsBuilder.ToString();

            //Build map data
            MapData map = area.Mode[0].MapData = new MapData(area.ToKey());
            map.Levels.Add(level);
            map.Bounds = level.Bounds;
        }
    }
}