using System;
using System.Reflection;
using System.Collections.Generic;

using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {

    [AttributeUsage(AttributeTargets.Property)]
    public class WildcardContentAttribute : Attribute {
        public WildcardContentAttribute(string dirPath, Type assetType, string defaultSelection) {
            DirectoryPath = dirPath;
            AssetType = assetType;
            DefaultSelection = defaultSelection;
        }

        public string DirectoryPath { get; private set; }
        public Type AssetType { get; private set; }
        public string DefaultSelection { get; private set; }
    }

    public class SettingsContentHandler : IDisposable {
        public static void LoadWildcardContent(string dirPath, Type assetType, Action<ModAsset, string> loadContent) {
            ModAsset dirAsset = Everest.Content.Get(dirPath, true);
            if(dirAsset != null) foreach(ModAsset asset in dirAsset.Children) {
                if(asset.Type != assetType) continue;
                loadContent(asset, asset.PathVirtual.Substring(asset.PathVirtual.LastIndexOf('/')+1));
            }
        }

        private class SettingsContent {
            private Dictionary<string, object> values = new Dictionary<string, object>();
            private List<string> keys = new List<string>();
            private string selection;

            public SettingsContent(EverestModule module, PropertyInfo prop) {
                Module = module;
                Property = prop;
                Attribute = prop.GetCustomAttribute<WildcardContentAttribute>();

                //Load elements
                MethodInfo deserializeMethod = typeof(ModAsset).GetMethod(nameof(ModAsset.Deserialize)).MakeGenericMethod(Property.PropertyType);
                LoadWildcardContent(Attribute.DirectoryPath, Attribute.AssetType, (ModAsset asset, string name) => {
                    Logger.Log(module.Metadata.Name, $"Loading value '{name}' for setting '{Property.Name}'");
                    values[name] = deserializeMethod.Invoke(asset, new object[] {});
                    keys.Add(name);
                });

                //Set initial value
                Selection = Attribute.DefaultSelection;
            }

            public EverestModule Module { get; private set; }
            public PropertyInfo Property { get; private set; }
            public WildcardContentAttribute Attribute { get; private set; }
            public string Selection {
                get => selection;
                set {
                    selection = value;

                    //Set value
                    object val;
                    if(!values.TryGetValue(selection, out val)) Logger.Log(LogLevel.Warn, Module.Metadata.Name, $"Invalid selection '{Attribute.DefaultSelection}' for setting '{Property.Name}'");
                    else Property.SetValue(Module._Settings, val);
                }
            }

            public List<string> Keys => keys;
            public Dictionary<string, object> Values => values;
        }

        private Hook menuCreationHook;
        private Dictionary<string, SettingsContent> contentSettings = new Dictionary<string, SettingsContent>();

        public SettingsContentHandler(EverestModule module, Dictionary<string, string> selections) {
            //Create content instances
            foreach(PropertyInfo prop in module._Settings.GetType().GetProperties()) {
                //If the property has a WildcardContent attribute, create instance
                if(prop.GetCustomAttribute<WildcardContentAttribute>() != null) {
                    SettingsContent content = new SettingsContent(module, prop);
                    contentSettings.Add(prop.Name, content);

                    //Set selection base on settings
                    string sel;
                    if(selections.TryGetValue(prop.Name, out sel)) content.Selection = sel;
                }
            }

            //Add menu creation hook
            Type anonInner = typeof(EverestModule).GetNestedType("<>c__DisplayClass60_0", BindingFlags.Public | BindingFlags.NonPublic);
            menuCreationHook = new Hook(anonInner.GetMethod("<CreateModMenuSection>g__CreateItem|0", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), new Func<Func<object, PropertyInfo, string, object, TextMenu.Item>, object, PropertyInfo, string, object, TextMenu.Item>((orig, self, prop, name, settingsObj) => {
                //If the property corresponds to a content setting, intercept the call
                SettingsContent content;
                if(!contentSettings.TryGetValue(prop.Name, out content)) return orig(self, prop, name, settingsObj);
                if(!ShowSettings) return null;

                //Get some variables from "this"
                EverestModuleSettings settings = (EverestModuleSettings) anonInner.GetField("settings").GetValue(self);
                bool inGame = (bool) anonInner.GetField("inGame").GetValue(self);
                string nameDefaultPrefix = (string) anonInner.GetField("nameDefaultPrefix").GetValue(self);

                //Some standard logic
                SettingInGameAttribute attribInGame;
                if((attribInGame = prop.GetCustomAttribute<SettingInGameAttribute>()) != null && attribInGame.InGame != inGame) return null;
                if(prop.GetCustomAttribute<SettingIgnoreAttribute>() != null) return null;
                if(name == null) {
                    name = prop.GetCustomAttribute<SettingNameAttribute>()?.Name ?? $"{nameDefaultPrefix}{prop.Name.ToLowerInvariant()}";
                    name = name.DialogCleanOrNull() ?? prop.Name.SpacedPascalCase();
                }

                //Create item
                return new TextMenu.Slider(name, (int i) => {
                    string sel = content.Keys[i];
                    return
                        $"{nameDefaultPrefix}{prop.Name.ToLowerInvariant()}_{sel.ToLowerInvariant()}".DialogCleanOrNull() ??
                        sel;
                }, 0, content.Keys.Count-1, content.Keys.IndexOf(selections.GetValueSafe(prop.Name, content.Attribute.DefaultSelection)))
                .Change((int i) => selections[prop.Name] = content.Selection = content.Keys[i]);
            }));
        }

        public void Dispose() {
            //Clear content
            contentSettings.Clear();

            //Remove hooks
            if(menuCreationHook != null) menuCreationHook.Dispose();
            menuCreationHook = null;
        }

        public bool ShowSettings { get; set; } = true;
    }
}