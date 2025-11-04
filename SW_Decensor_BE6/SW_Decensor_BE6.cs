using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using SW_Decensor;
using UnityEngine;

#nullable enable
namespace SW_Decensor_BE5
{
    [BepInPlugin(Common.Metadata.GUID, Common.Metadata.MODNAME, Common.Metadata.VERSION)]
    public class loader : BaseUnityPlugin
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

        public void Awake()
        {
            llog = this.Logger;
            SW_Decensor.SW_Decensor.Setup();
        }

    }
}
