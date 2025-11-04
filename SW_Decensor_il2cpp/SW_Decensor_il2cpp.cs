using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;


namespace SW_Decensor_il2cpp
{
    [BepInPlugin(Common.Metadata.GUID, Common.Metadata.MODNAME, Common.Metadata.VERSION)]
    public class loader : BasePlugin
    {
        public static void log(LogLevel lv, object data, bool force = false)
        {
#if DEBUG
            llog?.Log(lv, data);
#else
            if (lv == LogLevel.Error || lv == LogLevel.Warning || force)
                llog?.Log(lv, data);
#endif
            return;
        }

        internal static ManualLogSource? llog;

        public override void Load()
        {
            llog = base.Log;
            SW_Decensor.SW_Decensor.Setup();
        }
    }
}
