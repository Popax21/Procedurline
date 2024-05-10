using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.Procedurline.Demo {
    public static class DemoMap {
        private static readonly MethodInfo AreaData_Get_int = typeof(AreaData).GetMethod(nameof(AreaData.Get), new Type[] { typeof(int) });

        public static int DemoMapID = -1;

        [ContentInit]
        private static void Init() => Everest.Events.Level.OnLoadLevel += LevelLoadHandler;

        [ContentUninit]
        private static void Uninit() => Everest.Events.Level.OnLoadLevel += LevelLoadHandler;

        private static void LevelLoadHandler(Level level, Player.IntroTypes playerIntro, bool isFromLoader) {
            if(level.Session.Area.ID != DemoMapID) return;

            //Add backdrops
            level.Foreground.Backdrops.Add(new Parallax(GFX.Misc["purplesunset"]) { Renderer = level.Foreground, Scroll = Vector2.One * 0.5f, Speed = Vector2.One * 4f, LoopX = true, LoopY = true, Alpha = 0.2f });
            level.Foreground.Backdrops.Add(new Parallax(GFX.Misc["mist"]) { Renderer = level.Foreground, Scroll = Vector2.One * 0.5f, Speed = new Vector2(16f, 8f), LoopX = true, LoopY = true, Alpha = 0.3f });
            level.Background.Backdrops.Add(new Parallax(GFX.Game["bgs/04/bg0"]) { Renderer = level.Background, Scroll = Vector2.UnitX * 0.2f, Speed = Vector2.UnitX * -16f, LoopX = true });
            level.Background.Backdrops.Add(new Parallax(GFX.Game["bgs/04/bg1"]) { Renderer = level.Background, Scroll = Vector2.UnitX * 0.4f, Speed = Vector2.UnitX * -24f, LoopX = true });

            //Add entities
            float entityX = level.Bounds.Left;
            float entityY = level.Bounds.Bottom - 8f * 8;
            level.Entities.Add(new DemoBooster(new Vector2(entityX + 4f * 8, entityY)));
            level.Entities.Add(new DemoRefill(new Vector2(entityX + 8f * 8, entityY)));
            level.Entities.Add(new DemoDreamBlock(new Vector2(entityX + 12f * 8, entityY), 8f * 8, 4f * 8));
            level.Entities.Add(new SimpleRefill(new Vector2(entityX + 24f * 8, entityY)));
            level.Entities.Add(new SimpleBooster(new Vector2(entityX + 28f * 8, entityY)));
        }


        [ContentILHook("Celeste.Mod.UI.OuiHelper_ChapterSelect_LevelSet", "Enter", true)]
        [ContentILHook("Celeste.Mod.UI.OuiMapList", "ReloadItems")]
        [ContentILHook("Celeste.Mod.UI.OuiMapSearch", "ReloadItems")]
        private static void HideChapterModifier(ILContext ctx) {
            //Hook calls to AreaData.Get
            ILCursor cursor = new ILCursor(ctx);
            while(cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(AreaData_Get_int))) {
                ILLabel idMismatch = cursor.DefineLabel(), skip = cursor.DefineLabel();

                //Check if the area ID is the Procedurline demo map
                cursor.MoveAfterLabels();
                cursor.Emit(OpCodes.Dup);
                cursor.Emit(OpCodes.Ldsfld, typeof(DemoMap).GetField(nameof(DemoMap.DemoMapID)));
                cursor.Emit(OpCodes.Ceq);
                cursor.Emit(OpCodes.Brfalse, idMismatch);

                //Force the result to be null
                cursor.Emit(OpCodes.Pop);
                cursor.Emit(OpCodes.Ldnull);
                cursor.Emit(OpCodes.Br, skip);

                cursor.MarkLabel(idMismatch);
                cursor.Index++;
                cursor.MarkLabel(skip);
            }
        }

        //We have to fix Everest's bugs ._.
        [ContentILHook("Celeste.Mod.UI.OuiHelper_ChapterSelect_LevelSet", "Enter", true)]
        private static void EverestHotFix1(ILContext ctx) {
            //Find the AreaData variable
            int areaDataVarIdx = 0;
            new ILCursor(ctx).GotoNext(i => i.MatchCallOrCallvirt(AreaData_Get_int)).GotoNext(i => i.MatchStloc(out areaDataVarIdx));

            //Find the faulty check and fix it
            ILCursor cursor = new ILCursor(ctx);
            if(!cursor.TryGotoNext(MoveType.After, i => i.MatchLdloc(areaDataVarIdx), i => i.MatchBrfalse(out _))) {
                Logger.Log("PLdemo", "Everest OuiHelper_ChapterSelect_LevelSet.Enter hotfix not required");
                return;
            }
            Logger.Log(LogLevel.Warn, "PLdemo", "Applying Everest OuiHelper_ChapterSelect_LevelSet.Enter hotfix...");

            ILLabel skipLabel = null;
            cursor.Clone().GotoNext(i => i.MatchBrfalse(out skipLabel));
            cursor.Instrs[cursor.Index-1].Operand = skipLabel;
        }

        [ContentILHook("Celeste.Mod.UI.OuiMapSearch", "ReloadItems")]
        private static void EverestHotFix2(ILContext ctx) {
            //Find the name access
            ILCursor cursor = new ILCursor(ctx);
            if(!cursor.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt(AreaData_Get_int), i => i.MatchLdfld(typeof(AreaData), nameof(AreaData.Name)))) {
                Logger.Log("PLdemo", "Everest OuiMapSearch.ReloadItems hotfix not required");
                return;
            }
            Logger.Log(LogLevel.Warn, "PLdemo", "Applying Everest OuiMapSearch.ReloadItems hotfix...");

            //Find the continue branch
            ILLabel continueLabel = null;
            cursor.Clone().GotoNext(i => i.MatchBrfalse(out continueLabel));

            //Insert a null check
            VariableDefinition areaDataVar = new VariableDefinition(ctx.Import(typeof(AreaData)));
            ctx.Body.Variables.Add(areaDataVar);
            cursor.Index--;
            cursor.MoveAfterLabels();
            cursor.Emit(OpCodes.Dup);
            cursor.Emit(OpCodes.Stloc, areaDataVar);
            cursor.Emit(OpCodes.Ldnull);
            cursor.Emit(OpCodes.Ceq);
            cursor.Emit(OpCodes.Brtrue, continueLabel);
            cursor.Emit(OpCodes.Ldloc, areaDataVar);
        }

        [ContentHook("Celeste.AreaData", "Load")]
        private static void AreaDataLoadHook(On.Celeste.AreaData.orig_Load orig) {
            orig();

            //Build area data
            AreaData area = new AreaData() {
                ID = DemoMapID = AreaData.Areas.Count,
                SID = "Procedurline/pl_demo_map",
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
            AreaData.Areas.Add(area);

            //Build level data
            LevelData level = new LevelData(new BinaryPacker.Element() { Attributes = new Dictionary<string, object>(), Children = new List<BinaryPacker.Element>() }) {
                Name = "demo",
                Music = SFX.music_city,
                MusicLayers = new float[] { 1f, 1f, 1f, 1f },
                Bounds = new Rectangle(0, 0, 8*64, 8*32)
            };
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
            map.RegenerateLevelsByNameCache();
        }
    }
}