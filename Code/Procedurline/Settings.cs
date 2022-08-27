using YamlDotNet.Serialization;

namespace Celeste.Mod.Procedurline {
    public sealed class ProcedurlineSettings : EverestModuleSettings {
        public bool AsynchronousStaticProcessing { get; set; } = false;
        public bool AsynchronousDynamicProcessing { get; set; } = true;
        public bool UseThreadPool { get; set; } = true;
        public bool LogProcessingTimes { get; set; } = false;

        [SettingIgnore]
        public bool DebugTextureLeaks { get; set; } = false;

        [SettingIgnore]
        public int MaxTextureCacheSize { get; set; } = 256*1024*1024;

        [SettingIgnore]
        public int MinTextureCacheMargin { get; set; } = 512*1024*1024;

        [YamlIgnore]
        public int __MENU_BUTTONS { get; set; }
        public void Create__MENU_BUTTONS(TextMenu menu, bool inGame) {
            menu.Add(new TextMenu.Button("Evict Texture Cache").Pressed(() => {
                ProcedurlineModule.TextureManager.CacheEvictor.EvictAll();
            }));
            menu.Add(new TextMenu.Button("Reset Processing Cache").Pressed(() => {
                ProcedurlineModule.GlobalScope.Invalidate();
            }));
        }
    }
}