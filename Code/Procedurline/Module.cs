using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using MonoMod.Cil;
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

        private GlobalManager globalManager;
        private TextureManager texManager;
        private SpriteManager spriteManager;
        private PlayerManager playerManager;

        private DataScope globalScope, sceneScope, levelScope, staticScope, playerScope;

        private List<IDetour> contentHooks = new List<IDetour>();

        public override void Load() {
            //Create components
            globalManager = new GlobalManager(Celeste.Instance);
            texManager = new TextureManager(Celeste.Instance);
            spriteManager = new SpriteManager(Celeste.Instance);
            playerManager = new PlayerManager(Celeste.Instance);

            //Create default scopes
            globalScope = new DataScope("$GLOBAL");
            sceneScope = new DataScope("$SCENE");
            levelScope = new DataScope("$LEVEL");
            staticScope = new DataScope("$STATIC");
            playerScope = new DataScope("$PLAYER");

            //Apply content hooks and patches
            foreach(Type type in typeof(ProcedurlineModule).Assembly.GetTypes()) {
                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllStatic)) {
                    if(method.GetCustomAttribute(typeof(ContentHookAttribute)) is ContentHookAttribute hookAttr) {
                        Type targetType = (hookAttr.TargetTypeName != null) ? Assembly.GetEntryAssembly().GetType(hookAttr.TargetTypeName, true, true) : type.BaseType;
                        contentHooks.Add(new Hook(targetType.GetMethodRecursive(hookAttr.TargetMethodName, PatchUtils.BindAll), method));
                    }

                    if(method.GetCustomAttribute(typeof(ContentILHookAttribute)) is ContentILHookAttribute ilHookAttr) {
                        Type targetType = (ilHookAttr.TargetTypeName != null) ? Assembly.GetEntryAssembly().GetType(ilHookAttr.TargetTypeName, true, true) : type.BaseType;
                        contentHooks.Add(new ILHook(targetType.GetMethodRecursive(ilHookAttr.TargetMethodName, PatchUtils.BindAll), (ILContext.Manipulator) method.CreateDelegate(typeof(ILContext.Manipulator))));
                    }
                }

                foreach(MethodInfo method in type.GetMethods(PatchUtils.BindAllInstance)) {
                    if(method.GetCustomAttribute(typeof(ContentVirtualizeAttribute)) is ContentVirtualizeAttribute virtAttr) {
                        method.Virtualize(virtAttr.CallBase, contentHooks);
                    }
                }

                foreach(PropertyInfo prop in type.GetProperties(PatchUtils.BindAll)) {
                    if(prop.GetCustomAttribute(typeof(ContentFieldProxyAttribute)) is ContentFieldProxyAttribute proxyAttr) {
                        prop.PatchFieldProxy(type.GetFieldRecursive(proxyAttr.TargetFieldName, PatchUtils.BindAll), contentHooks);
                    }
                }

                foreach(PropertyInfo prop in type.GetProperties(PatchUtils.BindAllInstance)) {
                    foreach(ContentPatchSFXAttribute sfxAttr in prop.GetCustomAttributes(typeof(ContentPatchSFXAttribute)).Cast<ContentPatchSFXAttribute>()) {
                        Type targetType = (sfxAttr.TargetTypeName != null) ? Assembly.GetEntryAssembly().GetType(sfxAttr.TargetTypeName, true, true) : type.BaseType;
                        targetType.GetMethodRecursive(sfxAttr.TargetMethodName, PatchUtils.BindAll).PatchSFX(prop, contentHooks);
                    }
                }
            }
        }

        public override void Unload() {
            //Dispose content hooks
            foreach(IDetour hook in contentHooks) hook.Dispose();
            contentHooks.Clear();

            //Dispose default scopes
            globalScope?.Dispose();
            sceneScope?.Dispose();
            levelScope?.Dispose();
            playerScope?.Dispose();

            //Dispose components
            playerManager?.Dispose();
            spriteManager?.Dispose();
            texManager?.Dispose();
            globalManager?.Dispose();
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
        public static DataScope StaticScope => Instance?.staticScope;
        public static DataScope PlayerScope => Instance?.playerScope;

        [Command("pl_invlscope", "Invalidates the specified Procedurline data scope (default: $GLOBAL)")]
        private static void INVLGLBL(string scope) {
            switch(scope?.ToUpper() ?? "$GLOBAL") {
                case "$GLOBAL": GlobalScope?.Invalidate(); break;
                case "$SCENE": SceneScope?.Invalidate(); break;
                case "$LEVEL": LevelScope?.Invalidate(); break;
                case "$STATIC": StaticScope?.Invalidate(); break;
                case "$PLAYER": PlayerScope?.Invalidate(); break;
                default: Celeste.Commands.Log("Unknown scope!"); break;
            }
        }
    }
}
