using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Handles global state, hooks, scopes, and other miscellaneous things.
    /// </summary>
    public sealed class GlobalManager : GameComponent {
        private LinkedList<Task> blockingTasks = new LinkedList<Task>();

        internal GlobalManager(Game game) : base(game) {
            game.Components.Add(this);

            //Install hooks
            using(new DetourContext(ProcedurlineModule.HOOK_PRIO)) {
                IL.Monocle.Engine.Update += EngineUpdateModifier;
                On.Monocle.Engine.Draw += EngineDrawHook;
                On.Monocle.Scene.End += SceneEndHook;
                On.Celeste.Level.Reload += LevelReloadHook;
            }
        }

        protected override void Dispose(bool disposing) {
            //Remove hooks
            IL.Monocle.Engine.Update -= EngineUpdateModifier;
            On.Monocle.Engine.Draw -= EngineDrawHook;

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        /// <summary>
        /// Prevents the engine (and as such, the game) from updating until the given task completes.
        /// This is better than <see cref="Task.Wait()" /> as it prevents deadlocks when the task is waiting for another Procedurline component, which is still updated this way.
        /// </summary>
        public void BlockEngineOnTask(Task task) {
            lock(blockingTasks) blockingTasks.AddLast(task);
        }

        private void EngineUpdateModifier(ILContext ctx) {
            ILCursor cursor = new ILCursor(ctx);

            cursor.EmitDelegate<Func<bool>>(() => {
                //Check if all blocking tasks have completed
                lock(blockingTasks) {
                    for(LinkedListNode<Task> node = blockingTasks.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                        if(node.Value.IsCompleted) {
                            blockingTasks.Remove(node);
                        } else {
                            //Invoke base.Update()
                            return false;
                        }
                    }
                }

                //Continue like usual
                return true;
            });

            //Check if we should call base.Update
            ILLabel continueLabel = cursor.DefineLabel();
            cursor.Emit(OpCodes.Brtrue, continueLabel);

            //Call base.Update()
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldarg_1);
            cursor.Emit(OpCodes.Call, typeof(Game).GetMethod("Update", PatchUtils.BindAllInstance));
            cursor.Emit(OpCodes.Ret);

            cursor.MarkLabel(continueLabel);
        }

        private void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Monocle.Engine engine, GameTime time) {
            //Check if all blocking tasks have completed
            lock(blockingTasks) {
                for(LinkedListNode<Task> node = blockingTasks.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    if(node.Value.IsCompleted) {
                        //Only the update hook removes tasks to avoid edge cases
                    } else {
                        //Don't render anything at all, but also don't clear
                        //This effectively causes a lag frame
                        return;
                    }
                }
            }

            orig(engine, time);
        }

        private void SceneEndHook(On.Monocle.Scene.orig_End orig, Monocle.Scene scene) {
            orig(scene);
            ProcedurlineModule.SceneScope?.Invalidate();
            ProcedurlineModule.LevelScope?.Invalidate();
        }

        private void LevelReloadHook(On.Celeste.Level.orig_Reload orig, Level level) {
            orig(level);
            ProcedurlineModule.LevelScope?.Invalidate();
        }
    }
}