using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

using MonoMod.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public sealed class ProcedurlineModule : EverestModule {
        public const int HOOK_PRIO = -1000000;

        public static ProcedurlineModule Instance { get; private set; }
        public static string Name => Instance.Metadata.Name;
        public ProcedurlineModule() { Instance = this; }

        public override Type SettingsType => typeof(ProcedurlineSettings);
        public static ProcedurlineSettings Settings => (ProcedurlineSettings) Instance?._Settings;

        private DisposablePool dispPool;

        private GlobalManager globalManager;
        private TextureManager texManager;
        private SpriteManager spriteManager;
        private PlayerManager playerManager;

        private DataScope globalScope, sceneScope, levelScope, spriteScope, dynamicScope, playerScope;

        public override void Load() {
            dispPool = new DisposablePool();

            //Create components
            globalManager = dispPool.Add(new GlobalManager(Celeste.Instance));
            texManager = dispPool.Add(new TextureManager(Celeste.Instance));
            spriteManager = dispPool.Add(new SpriteManager(Celeste.Instance));
            playerManager = dispPool.Add(new PlayerManager(Celeste.Instance));

            //Create default scopes
            globalScope = dispPool.Add(new DataScope("$GLOBAL"));
            sceneScope = dispPool.Add(new DataScope("$SCENE"));
            levelScope = dispPool.Add(new DataScope("$LEVEL"));
            spriteScope = dispPool.Add(new DataScope("$SPRITE"));
            dynamicScope = dispPool.Add(new DataScope("$DYNAMIC"));
            playerScope = dispPool.Add(new DataScope("$PLAYER"));

            //Apply content hooks and patches
            foreach(Type type in typeof(ProcedurlineModule).Assembly.GetTypes()) {
                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllStatic | BindingFlags.DeclaredOnly)) {
                    if(method.GetCustomAttribute(typeof(ContentHookAttribute)) is ContentHookAttribute hookAttr) {
                        Type targetType = (hookAttr.TargetTypeName != null) ? Assembly.GetEntryAssembly().GetType(hookAttr.TargetTypeName, true, true) : type.BaseType;
                        dispPool.Add(new Hook(targetType.GetMethodRecursive(hookAttr.TargetMethodName, PatchUtils.BindAll), method));
                    }

                    foreach(ContentILHookAttribute ilHookAttr in method.GetCustomAttributes(typeof(ContentILHookAttribute))) {
                        Type targetType = (ilHookAttr.TargetTypeName != null) ? Assembly.GetEntryAssembly().GetType(ilHookAttr.TargetTypeName, true, true) : type.BaseType;
                        MethodInfo targetMethod = targetType.GetMethodRecursive(ilHookAttr.TargetMethodName, PatchUtils.BindAll);
                        if(ilHookAttr.HookStateMachine) targetMethod = targetMethod.GetStateMachineTarget();
                        dispPool.Add(new ILHook(targetMethod, (ILContext.Manipulator) method.CreateDelegate(typeof(ILContext.Manipulator))));
                    }
                }

                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllInstance | BindingFlags.DeclaredOnly)) {
                    if(method.GetCustomAttribute(typeof(ContentVirtualizeAttribute)) is ContentVirtualizeAttribute virtAttr) {
                        method.Virtualize(virtAttr.CallBase, dispPool);
                    }
                }

                foreach(PropertyInfo prop in type.GetProperties(PatchUtils.BindAll | BindingFlags.DeclaredOnly)) {
                    if(prop.GetCustomAttribute(typeof(ContentFieldProxyAttribute)) is ContentFieldProxyAttribute proxyAttr) {
                        prop.PatchFieldProxy(type.GetFieldRecursive(proxyAttr.TargetFieldName, PatchUtils.BindAll), dispPool);
                    }
                }

                foreach(PropertyInfo prop in type.GetProperties(PatchUtils.BindAllInstance | BindingFlags.DeclaredOnly)) {
                    foreach(ContentPatchSFXAttribute sfxAttr in prop.GetCustomAttributes(typeof(ContentPatchSFXAttribute))) {
                        type.BaseType.GetMethodRecursive(sfxAttr.TargetMethodName, PatchUtils.BindAllInstance).PatchSFX(prop, dispPool);
                    }
                }
            }

            //Call content initalizers
            foreach(Type type in typeof(ProcedurlineModule).Assembly.GetTypes()) {
                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllStatic)) {
                    if(method.GetCustomAttribute(typeof(ContentInitAttribute)) is ContentInitAttribute) method.Invoke(null, Array.Empty<object>());
                }
            }
        }

        public override void Unload() {
            //Call content uninitializers
            foreach(Type type in typeof(ProcedurlineModule).Assembly.GetTypes()) {
                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllStatic)) {
                    if(method.GetCustomAttribute(typeof(ContentInitAttribute)) is ContentUninitAttribute) method.Invoke(null, Array.Empty<object>());
                }
            }

            //Dispose disposables
            dispPool?.Dispose();
        }

        public override void LoadContent(bool firstLoad) {
            if(firstLoad) {
                //Create the empty texture
                TextureManager.EmptyTexture = new TextureHandle("EMPTY", TextureManager.GlobalScope, 1, 1, Color.Transparent);

                //Create the error texture
                TextureHandle errTex = TextureManager.ErrorTexture = new TextureHandle("ERROR", TextureManager.GlobalScope, 2, 2, Color.Transparent);

                TextureData errorData = new TextureData(2, 2);
                errorData[0,0] = errorData[1,1] = Color.Magenta;
                errorData[1,0] = errorData[0,1] = Color.Black;
                TextureManager.ErrorTexture.SetTextureData(errorData).ContinueWith(_ => errorData.Dispose());
            }
        }
        
        public static GlobalManager GlobalManager => Instance?.globalManager;
        public static TextureManager TextureManager => Instance?.texManager;
        public static SpriteManager SpriteManager => Instance?.spriteManager;
        public static PlayerManager PlayerManager => Instance?.playerManager;

        public static DataScope GlobalScope => Instance?.globalScope;
        public static DataScope SceneScope => Instance?.sceneScope;
        public static DataScope LevelScope => Instance?.levelScope;
        public static DataScope SpriteScope => Instance?.spriteScope;
        public static DataScope DynamicScope => Instance?.dynamicScope;
        public static DataScope PlayerScope => Instance?.playerScope;

        [Command("pl_invlscope", "Invalidates the specified Procedurline data scope (default: $GLOBAL)")]
        private static void INVLGLBL(string scope) {
            switch(scope?.ToUpper() ?? "$GLOBAL") {
                case "$GLOBAL": GlobalScope?.Invalidate(); break;
                case "$SCENE": SceneScope?.Invalidate(); break;
                case "$LEVEL": LevelScope?.Invalidate(); break;
                case "$SPRITE": SpriteScope?.Invalidate(); break;
                case "$DYNAMIC": DynamicScope?.Invalidate(); break;
                case "$PLAYER": PlayerScope?.Invalidate(); break;
                default: Celeste.Commands.Log("Unknown scope!"); break;
            }
        }
    }
}
