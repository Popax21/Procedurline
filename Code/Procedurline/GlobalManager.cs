using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Handles global state, hooks, scopes, and other miscellaneous things
    /// </summary>
    public sealed class GlobalManager : GameComponent {
        public static readonly TaskCreationOptions ForceQueue = TaskCreationOptions.LongRunning;

        /// <summary>
        /// Implements a task scheduler which executes tasks on the main thread
        /// </summary>
        public sealed class MainThreadTaskScheduler : TaskScheduler {
            private readonly LinkedList<Task> tasks = new LinkedList<Task>();
            private LinkedListNode<Task> stopNode = null;

            public void RunTasks() {
                if(!MainThreadHelper.IsMainThread) throw new InvalidOperationException("Not on main thread!");

                //Reset the stop node
                lock(tasks) stopNode = null;

                while(true) {
                    //Dequeue a task
                    Task task;
                    lock(tasks) {
                        if(tasks.Last == stopNode) break;
                        task = tasks.First.Value;
                        tasks.RemoveFirst();
                    }

                    //Execute it
                    TryExecuteTask(task);
                }
            }

            protected override IEnumerable<Task> GetScheduledTasks() {
                lock(tasks) return tasks;
            }

            protected override void QueueTask(Task task) {
                lock(tasks) {
                    //Check if this is a ForceQueue task, and we're on the main thread
                    if(MainThreadHelper.IsMainThread && (task.CreationOptions & ForceQueue) != 0) {
                        //Update the stop node
                        LinkedListNode<Task> node = tasks.AddLast(task);
                        stopNode ??= node;
                    } else {
                        //Enqueue before the stop node
                        if(stopNode != null) tasks.AddBefore(stopNode, task);
                        else tasks.AddLast(task);
                    }
                }
            }

            protected override bool TryDequeue(Task task) {
                lock(tasks) return tasks.Remove(task);
            }

            protected override bool TryExecuteTaskInline(Task task, bool wasQueued) {
                if(!MainThreadHelper.IsMainThread) return false;

                //We can't inline ForceQueue tasks
                if((task.CreationOptions & ForceQueue) != 0) return false;

                if(wasQueued && !TryDequeue(task)) return false;
                return TryExecuteTask(task);
            }
        }

        public readonly MainThreadTaskScheduler MainThreadScheduler;
        public readonly TaskFactory MainThreadTaskFactory;

        private LinkedList<Task> blockingTasks = new LinkedList<Task>();

        internal GlobalManager(Game game) : base(game) {
            game.Components.Add(this);

            //Setup main thread task scheduler/factory
            MainThreadScheduler = new MainThreadTaskScheduler();
            MainThreadTaskFactory = new TaskFactory(MainThreadScheduler);

            //Install hooks
            using(ProcedurlineModule.HOOK_CONTEXT.Use()) {
                On.Monocle.Engine.Update += EngineUpdateHook;
                On.Monocle.Engine.Draw += EngineDrawHook;
                On.Monocle.Scene.End += SceneEndHook;
                On.Celeste.Level.Reload += LevelReloadHook;
            }
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
            //Allow the main thread scheduler to execute tasks
            MainThreadScheduler.RunTasks();

            //Check if all blocking tasks have completed
            lock(blockingTasks) {
                for(LinkedListNode<Task> node = blockingTasks.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    if(node.Value.IsCompleted) {
                        blockingTasks.Remove(node);
                    } else {
                        //Still update the main thread helper
                        MainThreadHelper.Instance?.Update(time);
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