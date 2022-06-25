using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Handles global state, hooks, scopes, and other miscellaneous things.
    /// </summary>
    public sealed class GlobalManager : GameComponent {
        private readonly Action<GameTime> Game_Update;
        private LinkedList<Task> blockingTasks = new LinkedList<Task>();

        internal GlobalManager(Game game) : base(game) {
            game.Components.Add(this);

            //Create a delegate for the base Game.Update() method
            MethodInfo updateMethod = typeof(Game).GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            IntPtr updatePtr = updateMethod.MethodHandle.GetFunctionPointer();

            //Sketchy hidden internal delegate constructor (converts a function pointer to a delegate ._.)
            Game_Update = (Action<GameTime>) Activator.CreateInstance(typeof(Action<GameTime>), game, updatePtr);

            //Install hooks
            On.Monocle.Engine.Update += EngineUpdateHook;
            On.Monocle.Engine.Draw += EngineDrawHook;
            On.Monocle.Scene.End += SceneEndHook;
            On.Celeste.Level.Reload += LevelReloadHook;
        }

        protected override void Dispose(bool disposing) {
            //Remove hooks
            On.Monocle.Engine.Update -= EngineUpdateHook;
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

        private void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Monocle.Engine engine, GameTime time) {
            //Check if all blocking tasks have completed
            lock(blockingTasks) {
                for(LinkedListNode<Task> node = blockingTasks.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    if(node.Value.IsCompleted) {
                        blockingTasks.Remove(node);
                    } else {
                        //Still invoke base.Update()
                        Game_Update(time);
                        return;
                    }
                }
            }

            orig(engine, time);
        }

        private void EngineDrawHook(On.Monocle.Engine.orig_Draw orig, Monocle.Engine engine, GameTime time) {
            //Check if all blocking tasks have completed
            lock(blockingTasks) {
                for(LinkedListNode<Task> node = blockingTasks.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    if(node.Value.IsCompleted) {
                        //Only the update hook removes tasks to avoid edge cases
                    } else {
                        //Don't render anything at all
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