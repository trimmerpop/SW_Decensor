using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using UnityEngine;
using Application = UnityEngine.Application;
using BepInEx.Configuration;
using IEnumerator = System.Collections.IEnumerator;
using Exception = System.Exception;
using TimeSpan = System.TimeSpan;
using DateTime = System.DateTime;
using Type = System.Type;
using MethodInfo = System.Reflection.MethodInfo;
using Action = System.Action;
using Array = System.Array;
using MissingMethodException = System.MissingMethodException;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using ArgumentNullException = System.ArgumentNullException;
using NullReferenceException = System.NullReferenceException;

#if bie6mono
using BepInEx.Unity.Mono;
#endif

#if interop
    using BepInEx.Unity.IL2CPP;
    using Il2CppInterop.Runtime;
    using Il2CppInterop.Runtime.Injection;
    using Dictionary = Il2CppSystem.Collections.Generic;
    using Il2CppSystem.Collections;
    using Il2CppSystem;
#else
    using Dictionary = System.Collections.Generic;
    using static MonoMod.Cil.RuntimeILReferenceBag.FastDelegateInvokers;
#endif

// Polyfills for older .NET runtimes that are missing compiler-generated attributes.
// This allows projects targeting newer C# versions to run on older Unity runtimes (like .NET 3.5)
// without encountering a TypeLoadException.
#if !bie6mono && !interop
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class StateMachineAttribute : Attribute
    {
        public Type StateMachineType { get; private set; }
        public StateMachineAttribute(Type stateMachineType) { StateMachineType = stateMachineType; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class IteratorStateMachineAttribute : StateMachineAttribute
    {
        public IteratorStateMachineAttribute(Type stateMachineType) : base(stateMachineType) { }
    }
}
#endif

#nullable enable
namespace SW_Decensor
{
    [BepInPlugin(Metadata.GUID, Metadata.MODNAME, Metadata.VERSION)]
#if interop
    public class loader : BasePlugin
#else
    public class loader : BaseUnityPlugin
#endif
    {
        private static bool ShouldLog(LogLevel lv, bool force)
        {
#if DEBUG
            return true;
#else
            return lv == LogLevel.Error || lv == LogLevel.Warning || force;
#endif
        }

        public static void log(LogLevel lv, object data, bool force = false)
        {
            if (ShouldLog(lv, force))
            {
                llog?.Log(lv, data);
            }
        }

        public static void log(LogLevel lv, System.Func<object> dataProvider, bool force = false)
        {
            if (ShouldLog(lv, force))
            {
                try
                {
                    llog?.Log(lv, dataProvider());
                }
                catch (Exception e)
                {
                    llog?.Log(LogLevel.Error, string.Format("Error generating log message: {0}", e.Message));
                }
            }
        }

        public static loader Instance;
        internal static ManualLogSource llog;

        private void Init()
        {
            Instance = this;
#if interop
            llog = this.Log;
#else
            llog = this.Logger;
#endif
            SW_Decensor.Init(this);
        }

#if interop
        public override void Load()
        {
            Init();
        }
#else
        internal void Awake()
        {
            Init();
        }
#endif
    }

    public class SW_Decensor : MonoBehaviour
    {
        internal static SW_Decensor Instance { get; private set; }
        public static loader Loader { get; private set; }

        private static ConfigFile configFile { get; set; } = new ConfigFile(Path.Combine(Paths.ConfigPath, string.Format("{0}.ini", Common.Metadata.MODNAME)), false);
        private const string SectionName = "Config";
        public static ConfigEntry<string> KeyWords { get; set; } = configFile.Bind<string>(
                SectionName,
                "Keywords",
                string.Empty,
                "Be searched strings. Can use wildcard(*). If capital characters are used, treat as Case Sensitive."
                );
        public static ConfigEntry<string> RemoveKeyWords { get; set; } = configFile.Bind<string>(
                SectionName,
                "RemoveKeyWords",
                string.Empty,
                "Keywords for remove. Check the GameObject, Renderer, Material, Shader name."
                );
        public static ConfigEntry<string> ShaderPropertiesFloat { get; set; } = configFile.Bind<string>(
                SectionName,
                "ShaderPropertiesFloat",
                DecensorTools.default_ShaderPropertiesFloat,
                "Set float value to Material Shader Properties."
                );
        public static ConfigEntry<string> ShaderPropertiesVector { get; set; } = configFile.Bind<string>(
                SectionName,
                "ShaderPropertiesVector",
                DecensorTools.default_ShaderPropertiesVector,
                string.Format("Set vector value to Material Shader Properties. seperator({0})", DecensorTools.separator_vector)
                );
        public static ConfigEntry<int> maximumLOD { get; set; } = configFile.Bind<int>(
                SectionName,
                "maximumLOD",
                DecensorTools.default_MaximumLOD,
                "Use maximumLOD value. 1: NOT use. If want use this feature, set to -2 or 0."
                );
        public static ConfigEntry<string> ShaderReplace { get; set; } = configFile.Bind<string>(
                SectionName,
                "ShaderReplace",
                string.Empty,
                "Use shader replace method. ex) Standard Mosaic=Standard"
                );
        public static ConfigEntry<string> LayerReplace { get; set; } = configFile.Bind<string>(
                SectionName,
                "LayerReplace",
                string.Empty,
                "Use layer replace method. ex) Mosaic=Default"
                );

        static List<GameObject> GOS = new List<GameObject>();
        static TimeSpan tsLoopTimeLimit;
        static TimeSpan tsLoopTimeLimit_level_min = TimeSpan.FromMilliseconds(0.7f);
        static TimeSpan tsLoopTimeLimit_level_default = TimeSpan.FromMilliseconds(1.5f);
        static TimeSpan tsLoopTimeLimit_level_max = TimeSpan.FromMilliseconds(50);
        static TimeSpan tsCheckInterval;
        static DateTime? next_check = null;
        static Dictionary<string, string> shader_replace = new Dictionary<string, string>();
        static Dictionary<int, int> layer_replace = new Dictionary<int, int>();
        static Dictionary<int, int> layer_replace_count = new Dictionary<int, int>();
        static Dictionary<string, float> PropertiesFloat = new Dictionary<string, float>();
        static Dictionary<string, Vector4> PropertiesVector = new Dictionary<string, Vector4>();
        static bool bCheckLayer = false;
        static bool bInit_success = true;
        static bool bIsOverUnity2019_3 = false;
        static bool bGetPropertyType = false;
        static MethodInfo? GetPropertyType = null;
        static bool bFindPropertyIndex = false;
        static MethodInfo? FindPropertyIndex = null;
        static MethodInfo? GetActiveScene = null;
        static PropertyInfo? sceneCountProperty = null;
        static MethodInfo? getSceneAtMethod = null;
        static MethodInfo? getRootGameObjectsMethod = null;
        static PropertyInfo? isLoadedProperty = null;
        static PropertyInfo? gameObjectSceneProperty = null;
        private static GameObject? ddolAnchor = null; // for DontDestroyOnLoad

        static System.Type? UI_Image_Type = null;
#if interop
        static Il2CppSystem.Type? Il2Cpp_UI_Image_Type = null;
#endif
        static bool bUI_Image_Type_Checked = false;
        static PropertyInfo? UI_Image_material_Prop = null;
        static PropertyInfo? UI_Image_enabled_Prop = null;

        static int GOS_count = 0;
        static int renderers_index = -1;
        static int lastScene = -1;
        static int scene_loop_count = 0;
        static bool bUseCoroutine = false;
        static bool bUseTask = false;
        static bool bGetting_GOS = false;
        static int lastCheckedCount = 0;
        static int lastCheckedSameCount = 0;

        internal static void Setup()
        {
#if interop
            ClassInjector.RegisterTypeInIl2Cpp<SW_Decensor>();
#endif

            GameObject obj = new GameObject(Metadata.MODNAME);
            DontDestroyOnLoad(obj);
            obj.hideFlags = HideFlags.HideAndDontSave;
            Instance = obj.AddComponent<SW_Decensor>();
        }

        public static void Init(loader loader)
        {
            if (Loader != null)
                throw new Exception(string.Format("{0} is already loaded.", Metadata.MODNAME));
            Loader = loader;
            Setup();
            tsLoopTimeLimit = TimeSpan.FromMilliseconds(1);
            tsCheckInterval = TimeSpan.FromMilliseconds(500);
        }

        void Start()
        {
            try
            {
                bUseTask = CheckAsyncTaskSupport();

                if (KeyWords.Value == string.Empty && RemoveKeyWords.Value == string.Empty)
                {
                    KeyWords.Value = DecensorTools.default_KeyWords;
                    RemoveKeyWords.Value = DecensorTools.default_RemoceKeyWords;

                    // add to RemoveKeyWords which known shaders.
                    foreach (string key in DecensorTools.KnownShaders_Remove)
                    {
                        if (!DecensorTools.RemoveKeyWords.ContainsKey(key) && Shader.Find(key) != null)
                        {
                            //loader.log(LogLevel.Info, $"RemoveKeyWords added {key}");
                            DecensorTools.Init_KeyWords(key, true);
                            RemoveKeyWords.Value = string.Format("{0}{1}{2}",
                                RemoveKeyWords.Value, RemoveKeyWords.Value == string.Empty ? "" : DecensorTools.separator + " ",
                                key);
                        }
                    }

                    // shader_replace 구성
                    ShaderReplace.Value = CheckKnownShaders();

                    // MaximumLOD
                    maximumLOD.Value = CheckKnownShaders_MaximumLOD();
                }

                DecensorTools.Init_KeyWords(KeyWords.Value);
                DecensorTools.Init_KeyWords(RemoveKeyWords.Value, true);

                //foreach (KeyValuePair<string, KeyWord> entry in DecensorTools.KeyWords)
                //{
                //    loader.log(LogLevel.Info, $"{entry.Key} {entry.Value.Findway} {entry.Value.IsCaseSensitive} {entry.Value.Key}");
                //}

                // Properties
                PropertiesFloat_set(ShaderPropertiesFloat.Value);
                PropertiesVector_set(ShaderPropertiesVector.Value);
                ShaderReplace_set();
                string layer_set = LayerReplace_set();
                if (LayerReplace.Value != layer_set)
                    LayerReplace.Value = layer_set;
                foreach (KeyValuePair<int, int> item in layer_replace)
                {
                    layer_replace_count[item.Key] = 0;
                }
            }
            catch (Exception e)
            {
                bInit_success = false;
                loader.log(LogLevel.Error, e.Message);
            }

            if (string.Compare(Application.unityVersion, "2019.3") >= 0)
                bIsOverUnity2019_3 = true;

            try
            {
                FindPropertyIndex = AccessTools.Method("UnityEngine.Shader:FindPropertyIndex");
                if (FindPropertyIndex != null)
                    bFindPropertyIndex = true;
            }
            catch (Exception) { }

            try
            {
                GetPropertyType = AccessTools.Method("UnityEngine.Shader:GetPropertyType", new Type[] { typeof(int) });
                if (GetPropertyType != null)
                    bGetPropertyType = true;
            }
            catch (Exception) { }

            try
            {
                GetActiveScene = AccessTools.Method("UnityEngine.SceneManagement.SceneManager:GetActiveScene");

                Type sceneManagerType = Type.GetType("UnityEngine.SceneManagement.SceneManager, UnityEngine.CoreModule");
                if (sceneManagerType != null)
                {
                    sceneCountProperty = sceneManagerType.GetProperty("sceneCount", BindingFlags.Public | BindingFlags.Static);
                    getSceneAtMethod = sceneManagerType.GetMethod("GetSceneAt", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);

                    Type sceneType = Type.GetType("UnityEngine.SceneManagement.Scene, UnityEngine.CoreModule");
                    if (sceneType != null)
                    {
                        isLoadedProperty = sceneType.GetProperty("isLoaded", BindingFlags.Public | BindingFlags.Instance);
                        getRootGameObjectsMethod = sceneType.GetMethod("GetRootGameObjects", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    }
                }
            }
            catch (Exception) { }

            try
            {
                if (!bUI_Image_Type_Checked)
                {
                    UI_Image_Type = System.Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
#if interop
                    Il2Cpp_UI_Image_Type = Il2CppSystem.Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
#endif
                    if (UI_Image_Type != null)
                    {
                        UI_Image_material_Prop = UI_Image_Type.GetProperty("material");
                        UI_Image_enabled_Prop = UI_Image_Type.GetProperty("enabled");
                    }
                    bUI_Image_Type_Checked = true;
                }
            }
            catch { }

            try
            {
                gameObjectSceneProperty = typeof(GameObject).GetProperty("scene");
            }
            catch { }

#if !interop
            try
            {
                StartCoroutine("DoMain_Iemu");
                bUseCoroutine = true;
            }
            catch { bUseCoroutine = false; }
#endif

            ddolAnchor = new GameObject("DDOL_Anchor_SW_Decensor");
            DontDestroyOnLoad(ddolAnchor);
            ddolAnchor.hideFlags = HideFlags.HideAndDontSave;
        }

        /// <summary>
        /// 런타임에 'System.Threading.Tasks.Task' 타입을 찾아보고,
        /// 존재한다면 리플렉션을 통해 Task.Run을 호출하여 async/await와 유사한 기능의 지원 여부를 확인합니다.
        /// 이를 통해 컴파일 타임에 async 키워드를 사용하지 않아 구버전과의 호환성을 유지합니다.
        /// </summary>
        bool CheckAsyncTaskSupport()
        {
            bool bUseAsync = false;
            try
            {
                // 'System.Threading.Tasks.Task' 타입이 현재 런타임에 존재하는지 확인
                Type taskType = Type.GetType("System.Threading.Tasks.Task");
                if (taskType == null)
                {
                    return false;
                }

                // Task.Run(Action) 메서드 정보 가져오기
                MethodInfo runMethod = taskType.GetMethod("Run", new[] { typeof(Action) });
                if (runMethod == null)
                {
                    return false;
                }

                // 실제 테스트 실행 (예: Thread.Sleep)
                Action testAction = () => { Thread.Sleep(1); };
                var taskObject = runMethod.Invoke(null, new object[] { testAction });

                // 여기까지 예외 없이 실행되면 Task.Run을 사용할 수 있다고 판단
                bUseAsync = true;
                loader.log(LogLevel.Info, "Async Task support detected.");
            }
            catch (Exception ex)
            {
                loader.log(LogLevel.Info, $"Async Task support check failed: {ex.Message}");
                bUseAsync = false;
            }
            return bUseAsync;
        }


        public enum ShaderPropertyType
        {
            Color = 0, Vector = 1, Float = 2, Range = 3, Texture = 4, TexEnv = 4
        }

        void PropertiesFloat_set(string str)
        {
            foreach (string item_ in str.Split(DecensorTools.separator))
            {
                string item = item_.Trim();
                string[] vari = item.Split('=');
                if (vari.Length == 2 && float.TryParse(vari[1], out float val))
                {
                    PropertiesFloat[vari[0]] = val;
                }
            }
        }

        void PropertiesVector_set(string str)
        {
            foreach (string item_ in str.Split(DecensorTools.separator_vector))
            {
                string item = item_.Trim();
                string[] vari = item.Split('=');
                if (vari.Length == 2)
                {
                    try
                    {
                        PropertiesVector[vari[0]] = vari[1].ToVector4(",", " ");
                    }
                    catch { }
                }
            }
        }

        void ShaderReplace_set()
        {
            if (ShaderReplace.Value != string.Empty)
            {
                foreach (string item_ in ShaderReplace.Value.Split(DecensorTools.separator))
                {
                    string item = item_.Trim();
                    string[] shader = item.Split('=');
                    if (shader.Length == 2 && Shader.Find(shader[0]) != null && Shader.Find(shader[1]) != null)
                    {
                        shader_replace[shader[0]] = shader[1];
                    }
                }
            }
        }

        string LayerReplace_set()
        {
            // 기존 값들 중 Layer에 없는 이름이라면 삭제처리.
            string layer_replace_str = string.Empty;
            if (LayerReplace.Value != string.Empty)
            {
                string[] key_str = LayerReplace.Value.Split(DecensorTools.separator);
                //foreach (string item in LayerReplace.Value.Split(DecensorTools.separator).Select(p => p.Trim()).ToArray())
                // 읽는 과정 중 Layer 이름이 존재하는 지 체크해서 없는 항목이라면 문자열에서 삭제

                foreach (string item_ in key_str)
                {
                    string item = item_.Trim();
                    string[] layer = item.Split('=');
                    if (layer.Count() == 2)
                    {
                        int org = LayerMask.NameToLayer(layer[0]);
                        int tgt = LayerMask.NameToLayer(layer[1]);
                        if (org >= 0 && tgt >= 0)
                        {
                            layer_replace[org] = tgt;
                            loader.log(LogLevel.Info, $"  Layer {layer[0]} {org} -> {layer[1]} {tgt}");
                            bCheckLayer = true;
                            layer_replace_str = string.Format("{0}{1}{2}={3}",
                                layer_replace_str, layer_replace_str == string.Empty ? "" : DecensorTools.separator + " ",
                                layer[0], layer[1]);
                        }
                    }
                }
            }
            else
            {
                // Layer replace 값이 없으면, 키워드에 해당하는 것이 있는지 체크 -> Default 로 빠구게 설정.
                int DefaultLayer = -1;
                //string[] layers = Enumerable.Range(0, 31).Select(index => LayerMask.LayerToName(index)).Where(l => !string.IsNullOrEmpty(l)).ToArray();
                List<string> layers = new List<string>();
                for (int index = 0; index <= 31; index++)
                {
                    string LayerName = LayerMask.LayerToName(index);
                    //loader.log(LogLevel.Info, $"  Layer {index} {LayerName}");
                    if (!string.IsNullOrEmpty(LayerName))
                        layers.Add(LayerName);
                }
                // 'Default' 가 있는지 체크
                foreach (string layer in layers)
                {
                    if (layer.ToLower() == "default" || layer.ToLower() == "normal")
                    {
                        DefaultLayer = LayerMask.NameToLayer(layer);
                        break;
                    }
                }
                // Default 못 찾으면 0 의 layer로 지정
                if (DefaultLayer == -1) DefaultLayer = 0;

                // Mosaic 이름이 있는 지 체크
                foreach (string layer in layers)
                {
                    // KnownLayers_NOT 에 있는 것은 제외
                    if (Array.FindIndex(DecensorTools.KnownLayers_NOT, x => x == layer) >= 0)
                        continue;
                    // KnownLayers 에 있는 것이면 추가.
                    int index = Array.FindIndex(DecensorTools.KnownLayers, x => x[0] == layer);
                    //loader.log(LogLevel.Info, $" lasyer {layer} {index}");
                    if (index >= 0)
                    {
                        int tgtLayer = LayerMask.NameToLayer(DecensorTools.KnownLayers[index][1]);
                        if (tgtLayer >= 0)
                        {
                            layer_replace[LayerMask.NameToLayer(layer)] = tgtLayer;
                            layer_replace_str = string.Format("{0}{1}{2}={3}",
                                layer_replace_str, layer_replace_str == string.Empty ? "" : DecensorTools.separator + " ",
                                layer, DecensorTools.KnownLayers[index][1]);
                        }
                    }
                    else
                    {
                        string found_layer;
                        found_layer = DecensorTools.MatchKeywords(layer);
                        if (found_layer != string.Empty)
                        {
                            layer_replace[LayerMask.NameToLayer(layer)] = DefaultLayer;
                            //loader.log(LogLevel.Info, $"   lasyer {LayerMask.NameToLayer(layer)} -> {DefaultLayer}");
                            layer_replace_str = string.Format("{0}{1}{2}={3}",
                                layer_replace_str, layer_replace_str == string.Empty ? "" : DecensorTools.separator + " ",
                                layer, LayerMask.LayerToName(DefaultLayer));
                        }
                    }
                }
            }
            if (layer_replace.Count() > 0)
                bCheckLayer = true;
            return layer_replace_str;
        }

        string CheckKnownShaders()
        {
            // 알려진 Shader 바꿔치기할 값을 시도
            string ret = string.Empty;
            foreach (string[] shad in DecensorTools.KnownShaders)
            {
                if (Shader.Find(shad[0]) != null && Shader.Find(shad[1]) != null)
                    ret = $"{ret}{(ret == string.Empty ? "" : DecensorTools.separator+" ")}{shad[0]}={shad[1]}";
            }
            return ret;
        }

        int CheckKnownShaders_MaximumLOD()
        {
            // 알려진 Shader MaximumLOD 값 설정
            foreach (string shad in DecensorTools.KnownShaders_MaximumLod__2)
            {
                if (Shader.Find(shad) != null)
                    return -2;
            }
            return DecensorTools.default_MaximumLOD;
        }

        void OnGUI()
        {
            if (!bUseCoroutine)
            {
                DoMain();
            }
        }

        IEnumerator DoMain_Iemu()
        {
            while (true)
            {
                yield return null;
                DoMain();
            }
        }

        void DoMain()
        {
            if (bGetting_GOS || !bInit_success) return;
            
            if (GetActiveScene != null)
            {
                var scene = GetActiveScene.Invoke(null, null);
                if (scene != null)
                {
                    var handleProp = scene.GetType().GetProperty("handle");
                    if (handleProp != null)
                    {
                        int sceneHandle = (int)handleProp.GetValue(scene, null);
                        if (lastScene != sceneHandle)
                        {
                            lastScene = sceneHandle;
                            GOS.Clear();
                            scene_loop_count = 0;
                        }
                    }
                }
            }

            tsLoopTimeLimit = (lastCheckedSameCount > DecensorTools.switchToSlow_GOSCountSame) ? tsLoopTimeLimit_level_min :
                              (scene_loop_count < 2) ? tsLoopTimeLimit_level_max : tsLoopTimeLimit_level_default;

            ProcessGameObjects();
        }

        void ProcessGameObjects()
        {
            if (next_check != null && GOS.Count > 0)
            {
                DateTime LoopLimit = DateTime.Now + tsLoopTimeLimit;
                do
                {
                    GameObject go = GOS.Last();
                    GOS.RemoveAt(GOS.Count - 1);

                    if (go != null)
                    {
                        Add_GOS_Child(go);
                        Decensor_GameObject(go);
                        GOS_count++;
                    }
                } while (GOS.Count > 0 && DateTime.Now < LoopLimit);
            }
            else if (next_check == null || next_check < DateTime.Now)
            {
                scene_loop_count = System.Math.Min(scene_loop_count + 1, 2);
                Get_GOS();
            }
        }

        //private object currentTask;
        //private IEnumerator WaitForTaskCoroutine()
        //{
        //    var task = currentTask;
        //    var isCompletedProp = task.GetType().GetProperty("IsCompleted");
        //    while (!(bool)isCompletedProp.GetValue(task, null))
        //    {
        //        yield return null;
        //    }

        //    var isFaultedProp = task.GetType().GetProperty("IsFaulted");
        //    if ((bool)isFaultedProp.GetValue(task, null))
        //    {
        //        var exceptionProp = task.GetType().GetProperty("Exception");
        //        var exception = exceptionProp.GetValue(task, null);
        //        loader.log(LogLevel.Error, exception);
        //    }
        //}

        void Add_GOS_Child(GameObject go)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GOS.Add(go.transform.GetChild(i).gameObject);
            }
        }

        void Get_GOS()
        {
            bGetting_GOS = true;
            
            GOS.Clear();
            bool gotRoots = false;

            if (sceneCountProperty != null && getSceneAtMethod != null && getRootGameObjectsMethod != null && isLoadedProperty != null)
            {
                try
                {
                    int sceneCount = (int)sceneCountProperty.GetValue(null, null);
                    for (int i = 0; i < sceneCount; i++)
                    {
                        object scene = getSceneAtMethod.Invoke(null, new object[] { i });
                        if (scene == null || !(bool)isLoadedProperty.GetValue(scene, null))
                        {
                            continue;
                        }

                        object sceneResult = getRootGameObjectsMethod.Invoke(scene, null);
                        if (sceneResult == null) continue;

#if interop
                        if (sceneResult is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> il2cppArray)
                        {
                            GOS.AddRange(il2cppArray);
                        }
                        else if (sceneResult is GameObject[] goArray)
                        {
                            GOS.AddRange(goArray);
                        }
#else
                        GOS.AddRange((GameObject[])sceneResult);
#endif
                    }

                    if (ddolAnchor != null && gameObjectSceneProperty != null)
                    {
                        var ddolScene = gameObjectSceneProperty.GetValue(ddolAnchor, null);
                        if (ddolScene != null)
                        {
                            object ddolResult = getRootGameObjectsMethod.Invoke(ddolScene, null);
                            if (ddolResult != null)
                            {
#if interop
                                if (ddolResult is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> il2cppArray)
                                {
                                    GOS.AddRange(il2cppArray);
                                }
                                else if (ddolResult is GameObject[] goArray)
                                {
                                    GOS.AddRange(goArray);
                                }
#else
                                GOS.AddRange((GameObject[])ddolResult);
#endif
                            }
                        }
                    }

                    GOS = GOS.Distinct().ToList();

                    gotRoots = true;
                }
                catch(Exception e)
                {
                    loader.log(LogLevel.Error, $"Error during multi-scene scan: {e.Message}");
                    gotRoots = false; // 에러 발생 시 폴백 로직을 타도록 설정
                }
            }

            if (!gotRoots)
            {
                loader.log(LogLevel.Debug, "Falling back to old root object finding method.");
                Transform[] trs;
#if interop
                var array = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Transform>());
                trs = array.Select(x => x.Cast<Transform>()).ToArray();
#else
                trs = UnityEngine.Object.FindObjectsOfType<Transform>();
#endif
                foreach (Transform xform in trs)
                {
                    if (xform.parent == null)
                        GOS.Add(xform.gameObject);
                }
            }

            lastCheckedSameCount = (lastCheckedCount == GOS.Count) ? lastCheckedSameCount + 1 : 0;
            lastCheckedCount = GOS.Count;

            GOS_count = 0;
            next_check = DateTime.Now + tsCheckInterval;
            
            bGetting_GOS = false;
        }

        static void Decensor_GameObject(GameObject go)
        {
            // GameObject
            //loader.log(LogLevel.Info, $"g {go.name} chidren {go.transform.childCount}");

            // RemoveKeyWords
            // Game Object transform.childCount > 0 이더라도 Renderer 포함이면 해당 Renderer를 disable
            bool bDisable = false;
            bool bExistsSpine = false;
            //bool bTransform = false;
            string found;
            found = DecensorTools.MatchKeywords(go.name, true);
            if (found != string.Empty && go.transform.childCount == 0)
            {
                // 삭제 키워드에 걸렸으며, chindCount == 0 이면 disable
                go.SetActive(false);
                Renderer ren = go.GetComponent<Renderer>();
                if (ren != null) ren.enabled = false;
                loader.log(LogLevel.Info, $"  found {go.name} {found} Removed.");
            }
            else
            {
                // GameObject에 childCount = 0 이면 disable 했으나, 일부겜에선 역효과. (おしおき・おさわり・妹分ちゃん)
                found = DecensorTools.MatchKeywords(go.name, true);
                //if (found != string.Empty)
                //    loader.log(LogLevel.Info, $"  found {go.name} {found} {go.transform.childCount}");

                // child가 없고, MeshRenderer가 하나만 있다면 disable
                if (go.transform.childCount == 0 && found != string.Empty)
                {
                    // renderer 가 들어간 콤포넌트 갯수 구하기.
                    // Live2D 의 renderer는 GetComponents<Renderer>() 로 구해지지 않음.
                    // Live2DCubismCore.dll 를 추가할 수 없고, Assembly-CSharp.dll를 추가하려면 유니티 버전에 맞는 파일들 구해야 함.
                    int render_count = 0;
                    Component[] cos = go.GetComponentsInChildren<Component>(true);
                    //bool bMeshExist = false;
                    foreach (Component co in cos)
                    {
#if interop
                        string type = co.GetIl2CppType().Name;
#else
                        string type = co.GetType().Name;
#endif
                        //loader.log(LogLevel.Info, $"  {go.name} comp {type}");

                        if (type.Contains("SkinnedMeshRenderer"))
                        {
                            render_count++;
                            bExistsSpine = true;    // SkinnedMeshRenderer를 가졌다면 지워지지 않게
                        }
                        else if ((type.Contains("SkinnedMeshRenderer") || !type.Contains("MeshRenderer"))
                            && type.Contains("Renderer"))
                            render_count++;
                        else if (type.Contains("Drawable") || type.Contains("Manager"))
                            render_count++;
                        else if (type.Contains("Bone") || type.Contains("Collider"))
                            bExistsSpine = true;
                        if (type.Contains("Spine") || type.Contains("Anima") || type.Contains("Camera"))
                            bExistsSpine = true;
                        //else if (type.Contains("Transform")
                        //    bTransform = true;
                    }

                    //// Transform 있다면 삭제 안되도록
                    //if (/*cos.Length == 1 &&*/ bTransform)
                    //    bExistsSpine = true;

                    // render가 없으면서 MeshRenderer만 있다면 disable. 
                    if (render_count == 0 && !bExistsSpine)
                        bDisable = true;
                    else if (render_count == 1 && !bExistsSpine)
                    {
                        // renderer가 1개이고 material도 한개인 경우, shader name이 KeyWords에 걸리면 disable 처리
                        Renderer[] rends = go.GetComponentsInChildren<Renderer>(false);
                        if (rends.Count() == 1)
                        {
                            bool bSharedMaterials = rends[0].sharedMaterials != null;
                            Material[] mats = (bSharedMaterials ? rends[0].sharedMaterials : rends[0].materials);
                            if (mats.Length == 1 && mats[0] != null && mats[0].shader != null)
                            {
                                string found_shader;
                                found_shader = DecensorTools.MatchKeywords(mats[0].shader.name);
                                if (found_shader != string.Empty)
                                    bDisable = true;
                            }
                        }
                    }
                }

                if (bDisable)
                {
                    go.SetActive(false);
                    loader.log(LogLevel.Info, $"  {go.name} disabled.");
                }
            }

            // layer replace
            if (bCheckLayer)
            {
                if (layer_replace.ContainsKey(go.layer))
                {
                    found = DecensorTools.MatchKeywords(go.name);
                    string LayerName = LayerMask.LayerToName(go.layer);
                    //loader.log(LogLevel.Info, $"  layer_replace_count {layer_replace_count[go.layer]}");
                    bool bReplace = false;
                    if (layer_replace_count[go.layer] == DecensorTools.LayerReplacMode_All)
                    {
                        bReplace = true;
                    }
                    else if (layer_replace_count[go.layer] == DecensorTools.LayerReplacMode_EA)
                    {
                        if (found != string.Empty)
                            bReplace = true;
                    }
                    else if (found != string.Empty)
                    {
                        layer_replace_count[go.layer] = DecensorTools.LayerReplacMode_EA;
                        bReplace = true;
                    }
                    else
                    {
                        if (Array.FindIndex(DecensorTools.KnownLayers_LayerReplacMode_All, x => x == LayerName) != -1)
                            layer_replace_count[go.layer] = DecensorTools.LayerReplacMode_All;
                        else
                        {
                            layer_replace_count[go.layer]++;
                            // DecensorTools.switchToLayerReplaceAllCount 를 넘기면 전부 replace로 처리
                            if (layer_replace_count[go.layer] > DecensorTools.switchToLayerReplaceAllCount)
                                layer_replace_count[go.layer] = DecensorTools.LayerReplacMode_All;
                        }
                    }
                    if (bReplace)
                    {
                        loader.log(LogLevel.Info, $"  {go.name} layer {go.layer} -> {layer_replace[go.layer]}");
                        //Camera.main.cullingMask |= 1 << (int)go.layer;    // layer에 필터가 걸린 경우가 있는 듯. (look)
                        go.layer = layer_replace[go.layer];
                    }
                }
            }
            //else
            //{
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
            if (renderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    //loader.log(LogLevel.Info, $"  r {renderer.name} {renderer.materials.Count()} {DecensorTools.MatchKeywords(renderer.name)}");
                    Decensor_renderer(renderer, bExistsSpine);
                }
            }

            // Image 처리
            try
            {
#if interop
                if (Il2Cpp_UI_Image_Type != null)
                {
                    var images = go.GetComponentsInChildren(Il2Cpp_UI_Image_Type, false);
                    if (images != null)
                    {
                        foreach (Component image in images)
                        {
                            Decensor_image(image);
                        }
                    }
                }
#else
                if (UI_Image_Type != null)
                {
                    Component[] images = go.GetComponentsInChildren(UI_Image_Type, false);
                    if (images != null)
                    {
                        foreach (Component image in images)
                        {
                            Decensor_image(image);
                        }
                    }
                }
#endif
            }
            catch (MissingMethodException) { }
        }

        static bool Decensor_renderer(Renderer renderer, bool bExistsSpine = false)
        {
            bool ret = true;    // 에러나면 false
            try
            {
                bool bMosaic = DecensorTools.MatchKeywords(renderer.name) != string.Empty;
                //loader.log(LogLevel.Info, $"  r {renderer.name} {renderer.materials.Count()} {bMosaic}");
                bool bSharedMaterials = renderer.sharedMaterials != null;
                Material[]? mats = (bSharedMaterials ? renderer.sharedMaterials : renderer.materials);
                bool bNotModToRemove = false;

                string type = renderer.GetType().Name;
                //if ((mats.Length > 1 && type != "MeshRenderer")
                //    || (mats.Length == 2 && type == "MeshRenderer"))
                //    // materials가 두개 일 때(MeshRenderer)만 shader 이름이 걸린 경우, 삭제하는 방향으로 처리.
                //    // materials가 두개 이상 일 때(MeshRenderer 이외) shader 이름이 걸린 경우, 삭제하는 방향으로 처리.
                //    bNotModToRemove = true;
                // Materials가 두개 이상이면 지워도 되는 것으로 간주.
                if (mats?.Length > 1) bNotModToRemove = true;

                List<Material> mats_added = new List<Material>();
                List<int> mats_remove_index = new List<int>();
                //foreach (Material mat in (bSharedMaterials ? renderer.sharedMaterials : renderer.materials))
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        //loader.log(LogLevel.Info, $"    m {mats[i].name} {mats[i].shader.name}");
                        // type == "UnityEngine.SkinnedMeshRenderer" 일때만 처리하는 것은 어떤가..?
                        if (Decensor_material(mats[i], bNotModToRemove || !bExistsSpine, type.Contains("MeshRenderer"), bMosaic))
                        {
                            loader.log(LogLevel.Info, $"    m {mats[i].name} {mats[i].shader.name} to be removed.");
                            bMosaic = true;

                            ////loader.log(LogLevel.Info, $" ---------- mats comp {mat.name} {(bSharedMaterials ? renderer.sharedMaterial.name : renderer.material.name)} {mat.name == (bSharedMaterials ? renderer.sharedMaterial.name : renderer.material.name)}");
                            //// mat이름이 renderer의 대표 material 이름과 같다면 살려둠.
                            //if (bNotModToRemove
                            //    && mats[i].name == (bSharedMaterials ? renderer.sharedMaterial.name : renderer.material.name)
                            //)
                            //{
                            //    //loader.log(LogLevel.Info, $" ---------- mats add {mats[i].name}");
                            //    mats_added.Add(mats[i]);
                            //}
                            //else
                            mats_remove_index.Add(i);
                        }
                        else
                            mats_added.Add(mats[i]);
                    }

                    // Unity 2021.3에서는 renderer.materials = 사용하면 ArgumentOutOfRangeException 에러남
                    if (bMosaic)
                    {
                        //loader.log(LogLevel.Info, $" ---------- {renderer.name} mats {mats.Count()} {mats_added.Count}");
                        if (mats.Count() > 0)
                        {
                            if (mats.Count() != mats_added.Count)
                            {
                                try
                                {
                                    if (bSharedMaterials)
                                    {
                                        renderer.sharedMaterials = mats_added.ToArray();
                                        // unity v5.0.2에서 Where() 쓰면 에러남. (千紗ちゃんを（セックスで）救う会)
                                        //renderer.sharedMaterials = mats.Where((x, Index) => mats_add_index.Contains(Index)).ToArray();
                                    }
                                    else
                                    {
                                        renderer.materials = mats_added.ToArray();
                                        //renderer.materials = mats.Where((x, Index) => mats_add_index.Contains(Index)).ToArray();
                                    }
                                    loader.log(LogLevel.Info, $"   {renderer.name} materials {mats.Count()} -> {mats_added.Count()}");
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    // 에러날 경우 renderQueue = 0 로 처리
                                    foreach (int index in mats_remove_index)
                                    {
                                        mats[index].renderQueue = 0;
                                        loader.log(LogLevel.Info, $"      {mats[index].name} {mats[index].shader.name} renderQueye 0");
                                    }
                                }
                            }
                        }
                        else
                        {
                            renderer.enabled = false;
                            //if (bSharedMaterials)
                            //    renderer.sharedMaterials = new Material[] { };
                            //else
                            //    renderer.materials = new Material[] { };
                        }
                    }
                }
            }
            catch (NullReferenceException e)
            {
                // Object reference not set to an instance of an object
                // 다음 것 처리
                loader.log(LogLevel.Error, $"Decensor_renderer NullReferenceException {renderer.name} " + e.Message);
                ret = false;
            }
            catch (MissingMethodException)
            {
                ret = false;
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Error, $"Decensor_renderer {e.GetType()}\n{e.Message}");
                ret = false;
            }
            return ret;
        }

        static void Decensor_image(Component image)
        {
            //loader.log(LogLevel.Info, $"  r {renderer.name} {renderer.materials.Count()} {foundR}");
            try
            {
                if (UI_Image_material_Prop != null && UI_Image_enabled_Prop != null)
                {
                    Material mat = (Material)UI_Image_material_Prop.GetValue(image, null);
                    if (Decensor_material(mat))
                    {
                        UI_Image_enabled_Prop.SetValue(image, false, null);
                    }
                }
            }
            catch (NullReferenceException) { }
            catch (Exception) { }
        }

        static bool Decensor_material(Material mat, bool bCanbeDeleted = false, bool bMeshRenderer = false, bool bForceCheckShader = false)
        {
            bool NeedDeleted = false;
            string foundM = string.Empty;
            string foundS = string.Empty;
            if (mat == null) return NeedDeleted;
            try
            {
                string mat_name = mat.name;
                string shader_name = string.Empty;
                if (mat.shader != null)
                    shader_name = mat.shader.name;
                if (mat.name.Length > 0 || bForceCheckShader)
                {
                    foundM = DecensorTools.MatchKeywords(mat_name, true);
                    foundS = DecensorTools.MatchKeywords(shader_name, true);

                    if (foundM == string.Empty && foundS == string.Empty || bForceCheckShader)
                    {
                        // shader 이름이 RemoveKeyWords에 있다면 일단 삭제하는 방향으로.
                        if (foundS != string.Empty)
                            NeedDeleted = true;

                        foundM = DecensorTools.MatchKeywords(mat_name);
                        foundS = DecensorTools.MatchKeywords(shader_name);

                        // Shader 이름이 걸렸는가 체크.
                        if (bCanbeDeleted && foundS != string.Empty)
                        {
                            loader.log(LogLevel.Info, $"      try remove  {mat_name} {shader_name} bCanbeDeleted {bCanbeDeleted} bMeshRenderer {bMeshRenderer}");
                            // 삭제 처리하면 다시 생성되어 오작동하게 됨.
                            if (bMeshRenderer)
                            {
                                mat.renderQueue = 0;
                            }
                            else
                                NeedDeleted = true;
                        }

                        // NeedDeleted = true 이라도 삭제처리가 안 되는 경우가 있음.
                        if (foundM != string.Empty || foundS != string.Empty || bForceCheckShader)
                        {
                            loader.log(LogLevel.Info, $"      found  {mat_name} {shader_name} {foundM != string.Empty} {foundS != string.Empty} NeedDeleted {NeedDeleted} index {GOS_count} {renderers_index}");

                            // shader replace 를 참조. 있다면 대체
                            if (!shader_replace.ContainsKey(shader_name))
                            {
                                string shader_rep = DecensorTools.GetDecensoredShaderName(shader_name);
                                if (shader_rep != string.Empty && shader_name != shader_rep)
                                {
                                    shader_replace[shader_name] = shader_rep;
                                    ShaderReplace.Value += (ShaderReplace.Value == string.Empty ? "" : $"{DecensorTools.separator} ") + string.Format("{0}={1}", shader_name, shader_rep);
                                    loader.log(LogLevel.Info, $"      shader replace added {shader_name} {shader_rep}");
                                }
                                else
                                    shader_replace[shader_name] = string.Empty;
                            }
                            if (shader_replace[shader_name] != string.Empty && shader_name != shader_replace[shader_name])
                            {
                                loader.log(LogLevel.Info, $"      shader replace {mat_name} {shader_name} -> {shader_replace[shader_name]}");
                                mat.shader = Shader.Find(shader_replace[shader_name]);
                                NeedDeleted = false;    // replace 되었으니 삭제는 안 하는 것으로.
                            }

                            // property 이름과 값이 정해져 있어야 함.
                            if (maximumLOD.Value != DecensorTools.default_MaximumLOD && mat.shader != null)
                            {
                                mat.shader.maximumLOD = maximumLOD.Value;
                            }
                            else if (Array.FindIndex(DecensorTools.KnownShaders_NotToSet, x => x == shader_name) == -1)
                            {
                                loader.log(LogLevel.Info, $"      shader check {mat_name} {shader_name}");
                                foreach (KeyValuePair<string, float> item in PropertiesFloat)
                                {
                                    if (mat.HasProperty(item.Key))
                                    {
                                        bool bDoSet = true;
                                        if (bGetPropertyType)
                                        {
                                            try
                                            {
                                                int? idx;
                                                if (bIsOverUnity2019_3 && bFindPropertyIndex)
                                                    idx = (int?)FindPropertyIndex?.Invoke(mat.shader, new object[] { item.Key });
                                                else
                                                    idx = Shader.PropertyToID(item.Key);
                                                if (idx != null)
                                                {
                                                    ShaderPropertyType type = (ShaderPropertyType)GetPropertyType?.Invoke(mat.shader, new object[] { idx });
                                                    //loader.log(LogLevel.Warning, $"  FindPropertyIndex {mat_name} {item.Key} {idx} {type}");
                                                    if (type != ShaderPropertyType.Float
                                                        && type != ShaderPropertyType.Range)
                                                    {
                                                        bDoSet = false;
                                                    }
                                                }
                                            }
                                            catch (TargetInvocationException)
                                            {
                                                bDoSet = false;
                                            }
                                            catch (Exception)
                                            {
                                                bDoSet = false;
                                            }
                                        }

                                        if (bDoSet)
                                        {
                                            var value = item.Value;
#if false // GetPropertyRangeLimits 로 체크. 사용가능한 버전이 있으나 굳이 사용해도 메리트가 ?
                                            if (bGetPropertyRangeLimits)
                                            {
                                                int index;
                                                if (bIsOverUnity2019_3 && FindPropertyIndex != null)
                                                    index = (int)FindPropertyIndex.Invoke(mat.shader, new object[] { item.Key });
                                                else
                                                {
                                                    index = Shader.PropertyToID(item.Key);
                                                }

                                                string shader_key = mat.shader.name + "?" + item.Key;
                                                Vector2 limit;
                                                if (!PropertiesFloatLimit.ContainsKey(shader_key))
                                                {
                                                    limit = (Vector2)GetPropertyRangeLimits.Invoke(mat.shader, new object[] { index });
                                                    PropertiesFloatLimit[shader_key] = limit;
                                                }
                                                else
                                                    limit = PropertiesFloatLimit[shader_key];
                                                if (value < limit.x && value < 1f && limit.x >= 1f)
                                                {
                                                    loader.log(LogLevel.Warning, $"  shader {mat_name} {item.Key} {value:0.000000} is less than {limit.x:0.000000}.");
                                                    value = limit.x;
                                                }
                                                else if (value > limit.y && limit.y <= 1f)
                                                {
                                                    loader.log(LogLevel.Warning, $"  shader {mat_name} {item.Key} {value} is larger than {limit.y}.");
                                                    if (limit.x == 0)
                                                        value = 0;
                                                    else
                                                        value = 0.000001f;
                                                }
                                            }
#endif
                                            //bIsForceSetValue = true;
                                            loader.log(LogLevel.Info, $"      {mat_name} {item.Key} set {value:0.0000000}");
                                            mat.SetFloat(item.Key, value);
                                            //bIsForceSetValue = false;
                                        }
                                    }
                                }
                                foreach (KeyValuePair<string, Vector4> item in PropertiesVector)
                                {
                                    if (mat.HasProperty(item.Key))
                                    {
                                        bool bDoSet = true;
                                        if (bGetPropertyType)
                                        {
                                            try
                                            {
                                                int? idx;
                                                if (bIsOverUnity2019_3 && bFindPropertyIndex)
                                                    idx = (int?)FindPropertyIndex?.Invoke(mat.shader, new object[] { item.Key });
                                                else
                                                    idx = Shader.PropertyToID(item.Key);
                                                if (idx != null)
                                                {
                                                    ShaderPropertyType type = (ShaderPropertyType)GetPropertyType?.Invoke(mat.shader, new object[] { idx });
                                                    if (type != ShaderPropertyType.Vector && type != ShaderPropertyType.Color)
                                                    {
                                                        bDoSet = false;
                                                    }
                                                }
                                            }
                                            catch (TargetInvocationException)
                                            {
                                                bDoSet = false;
                                            }
                                            catch (Exception)
                                            {
                                                bDoSet = false;
                                            }
                                        }

                                        if (bDoSet)
                                        {
                                            //bIsForceSetValue = true;
                                            loader.log(LogLevel.Info, $"      {mat_name} {item.Key} set {item.Value}");
                                            mat.SetVector(item.Key, item.Value);
                                            //bIsForceSetValue = false;
                                        }
                                    }
                                }
                            }

                        }
                    }
                    else
                    {
                        // Labyrinth of Estras v0.0.8 에서 Fox에 덮쳐지면 화면 깨짐
                        //if (bMeshRenderer)
                        //{
                        //    mat.renderQueue = 0;
                        //}
                        //else
                        NeedDeleted = true;
                        loader.log(LogLevel.Info, $"      {mat_name} {shader_name} NeedDeleted {NeedDeleted}");
                    }
                }
            }
            catch (NullReferenceException e)
            {
                loader.log(LogLevel.Error, $"Decensor Material " + e.Message);
            }

            catch (Exception e)
            {
                loader.log(LogLevel.Error, $"Decensor Material {e.GetType()}\n{e.Message}");
            }
            return NeedDeleted;
        }
    }
}

public static class StringVector4Extensions
{
    public static Vector4 ToVector4(this string str, params string[] delimiters)
    {
        if (string.IsNullOrEmpty(str)) throw new ArgumentNullException("str");
        if (delimiters == null || delimiters.Length == 0) throw new ArgumentNullException("delimiters");

        var parts = str.Split(delimiters, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) throw new System.FormatException("Input string must contain four numbers.");

        return new Vector4(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]));
    }
}
