using YamlDotNet.Serialization;

namespace Celeste.Mod.Procedurline {
    public sealed class ProcedurlineSettings : EverestModuleSettings {
        public bool AsynchronousStaticProcessing { get; set; } = false;
        public bool AsynchronousDynamicProcessing { get; set; } = true;
        public bool UseThreadPool { get; set; } = true;
        public bool LogProcessingTimes { get; set; } = false;

        [YamlIgnore]
        public int ResetCache { get; set; }
        public void CreateResetCacheEntry(TextMenu menu, bool inGame) {
            menu.Add(new TextMenu.Button("Reset Cache").Pressed(() => {
                ProcedurlineModule.GlobalScope.Invalidate();
            }));
        }
    }
}