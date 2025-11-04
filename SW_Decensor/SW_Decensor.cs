using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using BepInEx.Configuration;
using System.IO;
using System.Collections.Generic;
using Common;
using System.Reflection;
using HarmonyLib;
using Component = UnityEngine.Component;
using System.Threading.Tasks;









#if interop
using Il2CppInterop.Runtime;
    using Il2CppInterop.Runtime.Injection;
    using loader = SW_Decensor_il2cpp.loader;
    using Dictionary = Il2CppSystem.Collections.Generic;
    using Il2CppSystem.Collections;
#endif
#if mono
    using loader = SW_Decensor_BE5.loader;
    using Dictionary = System.Collections.Generic;
    using static MonoMod.Cil.RuntimeILReferenceBag.FastDelegateInvokers;
    //using static UnityEngine.EventSystems.EventTrigger;
    using System.Collections;
using System.Threading;
using UnityEngine.UI;
#endif

#nullable enable
namespace SW_Decensor
{
    public class SW_Decensor : MonoBehaviour
    {
        internal static SW_Decensor Instance { get; private set; } = new SW_Decensor();

        private static ConfigFile configFile { get; set; } = new ConfigFile($"{Paths.ConfigPath}\\{Common.Metadata.MODNAME}.ini", false);
        private const string SectionName = "Config";
        //public static ConfigEntry<bool> WorkOnSceneChanged { get; set; } = configFile.Bind<bool>(
        //        SectionName,
        //        "WorkOnSceneChanged",
        //        false,
        //        "Works on Scene changed only. this makes reduces lag."
        //        );
        public static ConfigEntry<bool> RendererOnlyCheckMode { get; set; } = configFile.Bind<bool>(
                SectionName,
                "RendererOnlyCheckMode",
                false,
                "Only checks Renderers. if set to false, Check GameObjects & Renderers"
                );
        public static ConfigEntry<string> KeyWords { get; set; } = configFile.Bind<string>(
                SectionName,
                "Keywords",
                string.Empty,
                "Be searched strings. Can use wildcard(*). If capital characters are used, treat as Case Sensitive."
                );   // 찾을 문자들
        public static ConfigEntry<string> RemoveKeyWords { get; set; } = configFile.Bind<string>(
                SectionName,
                "RemoveKeyWords",
                string.Empty,
                "Keywords for remove. Check the GameObject, Renderer, Material, Shader name."
                ); // 삭제할 문자들
        public static ConfigEntry<string> ShaderPropertiesFloat { get; set; } = configFile.Bind<string>(
                SectionName,
                "ShaderPropertiesFloat",
                DecensorTools.default_ShaderPropertiesFloat,
                "Set float value to Material Shader Properties."
                );  // material SetFloat
        public static ConfigEntry<string> ShaderPropertiesVector { get; set; } = configFile.Bind<string>(
                SectionName,
                "ShaderPropertiesVector",
                DecensorTools.default_ShaderPropertiesVector,
                $"Set vector value to Material Shader Properties. seperator({DecensorTools.separator_vector})"
                ); // material SetVector
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
                );   // shader 바꿔치기
        public static ConfigEntry<string> LayerReplace { get; set; } = configFile.Bind<string>(
                SectionName,
                "LayerReplace",
                string.Empty,
                "Use layer replace method. ex) Mosaic=Default"
                );    // Layer 바꿔치기
        //public static ConfigEntry<double> LoopTimeLimit { get; set; } = configFile.Bind<double>(
        //        SectionName,
        //        "LoopTimeLimit",
        //        new TimeSpan(0, 0, 0, 0, 1).TotalMilliseconds,
        //        "Time limit of process per update(). Unit:ms"
        //        ); // OnGUI() 당 처리 제한 시간
        //public static ConfigEntry<double> CheckInterval { get; set; } = configFile.Bind<double>(
        //        SectionName,
        //        "CheckInterval",
        //        new TimeSpan(0, 0, 0, 2).TotalMilliseconds,
        //        "Time interval between full checks. Unit:ms"
        //        ); // 한바퀴 처리 후 쉬는 시간

        static List<GameObject> GOS = new List<GameObject>();
        //static int GOS_index = -1;
        //static int GOS_count = 0;
        static TimeSpan tsLoopTimeLimit;
        //static TimeSpan tsLoopTimeLimit_check = TimeSpan.FromMilliseconds(5000); // 이시간이 지나도록 새로운 GOS를 구하지 않는다면 tsLoopTimeLimit를 조절
        static TimeSpan tsLoopTimeLimit_level_min = TimeSpan.FromMilliseconds(0.7f);
        static TimeSpan tsLoopTimeLimit_level_default = TimeSpan.FromMilliseconds(1.5f);
        static TimeSpan tsLoopTimeLimit_level_max = TimeSpan.FromMilliseconds(50);
        //static int tsLoopTimeLimit_level = 1;
        //static DateTime? LastGetGOS = null; // 마지막으로 GOS 구학 시각
        static TimeSpan tsCheckInterval;
        static DateTime? next_check = null;
        //static int? OldSceneId = null;
        static Dictionary<string, string> shader_replace = new Dictionary<string, string>();
        static Dictionary<int, int> layer_replace = new Dictionary<int, int>();
        // Layer에 갯수를 체크. layer에 GO갯수가 switchToLayerReplaceAllCount를 넘기면 전부 replace.(-1로 set)
        static Dictionary<int, int> layer_replace_count = new Dictionary<int, int>();
        static Dictionary<string, float> PropertiesFloat = new Dictionary<string, float>();
        static Dictionary<string, UnityEngine.Vector4> PropertiesVector = new Dictionary<string, UnityEngine.Vector4>();
        static bool bCheckLayer = false;    // LayerReplace 기능 사용여부. layer_replace의 갯수를 매번 세기에 따로 변수 만듦.
        static bool bInit_success = true;   // 각 초기값 초기화 완료?
        //static bool bNeedProcess = false;   // WorkOnSceneChanged 를 사용하는 경우, 이 값이 true일 때 처리함

        //static bool bIsForceSetValue = false;    // SetFloat 등을 호출 할 때 또 체크 안하도록
        static bool bIsOverUnity2019_3 = false;
        //static MethodInfo GetPropertyRangeLimits = null;
        //static bool bGetPropertyRangeLimits = false;
        //static MethodInfo GetPropertyName = null;
        // GetPropertyType가 null 인지 체크하는 식으로 했었으나, 유니티2017.3 에서 비교문 때문에 문제가 발생하여 따로 변수를 만들어 처리.
        static bool bGetPropertyType = false;
        static MethodInfo? GetPropertyType = null;
        static bool bFindPropertyIndex = false;
        static MethodInfo? FindPropertyIndex = null;
        static MethodInfo? GetActiveScene = null;

        static int GOS_count = 0;   // 처리한 GameObject count
        static Renderer[] renderers = new Renderer[0];
        static int renderers_count = 0;
        static int renderers_index = -1;
        static bool bIsFirstSet = false;    // 처음 ini 파일 만드는가?
        static int lastScene = -1;
        static int scene_loop_count = 0;
        static Dictionary<string, Vector2> PropertiesFloatLimit = new Dictionary<string, Vector2>();
        static bool bUseCoroutine = false;
        static bool bUseTask = true;
        static bool bGetting_GOS = false;
        static int lastCheckedCount = 0;
        static int lastCheckedSameCount = 0;
        static int GetGOS_TooLate_Count = 0;    // GOS를 구하는 시간이 switchToRendererModeTimeSpan 를 10번 넘기면 RendererOnlyMode 로.


        public static void Setup()
        {
#if interop
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<SW_Decensor>();
            }
            catch { }
#endif
            GameObject obj = new GameObject(Metadata.MODNAME);
            DontDestroyOnLoad(obj);
            obj.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                obj.transform.parent = null;
                Instance = obj.AddComponent<SW_Decensor>();
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Error, $"instance Init Error. {e.Message}");
            }
        }


        private void Awake()
        {
            //tsLoopTimeLimit = TimeSpan.FromMilliseconds(LoopTimeLimit.Value);
            //tsCheckInterval = TimeSpan.FromMilliseconds(CheckInterval.Value);
            tsLoopTimeLimit = TimeSpan.FromMilliseconds(1);
            tsCheckInterval = TimeSpan.FromMilliseconds(500);

        }

        void Start()
        {
            try
            {
#if interop
                bUseTask = false;
#else
                try
                {
                    testAsync();
                }
                catch (Exception)
                {
                    bUseTask = false;
                }
#endif
                if (KeyWords.Value == string.Empty && RemoveKeyWords.Value == string.Empty)
                {
                    bIsFirstSet = true;

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

                // layer 명으로 layer_replace 구성
                string layer_set = LayerReplace_set();
                if (LayerReplace.Value != layer_set)    // 값이 없을 경우, 키워드로 찾아보기에 문자열이 바뀔 수 있음.
                    LayerReplace.Value = layer_set;
                foreach (KeyValuePair<int, int> item in layer_replace)
                {
                    layer_replace_count[item.Key] = 0;
                }
            }
            catch (System.TypeLoadException e)
            {
                loader.log(LogLevel.Error, e.Message);
            }
            catch (Exception e)
            {
                bInit_success = false;
                loader.log(LogLevel.Error, e.Message);
            }

            // Unity 버전 체크.
            if (string.Compare( Application.unityVersion, "2019.3" ) >= 0)
                bIsOverUnity2019_3 = true;

            Harmony harmony = new Harmony(Metadata.GUID + ".patch");
            //harmony.PatchAll();
            MethodInfo original, patch;
            //try
            //{
            //    original = AccessTools.Method(typeof(GameObject), "SetActive");
            //    patch = AccessTools.Method(typeof(GameObject_SetActive), "Postfix");
            //    harmony.Patch(original, postfix: new HarmonyMethod(patch));
            //}
            //catch (Exception e)
            //{
            //    loader.log(LogLevel.Warning, $"GameObject.SetActive() patch failed. Don't care it.\n{e.Message}");
            //}

#if USE_SETVALUE_HOOK   // 사용하지 않는 것이 안정적일 듯한.
            try
            {
                original = AccessTools.Method(typeof(Material), "SetFloat", new Type[] { typeof(string), typeof(float) });
                patch = AccessTools.Method(typeof(Material_SetFloat_string), "Prefix");
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Warning, $"Material.SetFloat() (string) patch failed. Don't care it.\n{e.Message}");
            }
#if intgerop || true
            try
            {
                original = AccessTools.Method(typeof(Material), "SetFloat", new Type[] { typeof(int), typeof(float) });
                patch = AccessTools.Method(typeof(Material_SetFloat_int), "Prefix");
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Warning, $"Material.SetFloat() (int) patch failed. Don't care it.\n{e.Message}");
            }
            try
            {
                original = AccessTools.Method(typeof(Material), "SetVector", new Type[] { typeof(int), typeof(Vector4) });
                patch = AccessTools.Method(typeof(Material_SetVector_int), "Prefix");
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Warning, $"Material.SetVector() (int) patch failed. Don't care it.\n{e.Message}");
            }
#endif
            try
            {
                original = AccessTools.Method(typeof(Material), "SetVector", new Type[] { typeof(string), typeof(Vector4) });
                patch = AccessTools.Method(typeof(Material_SetVector_string), "Prefix");
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                loader.log(LogLevel.Warning, $"Material.SetVector() (string) patch failed. Don't care it.\n{e.Message}");
            }
#endif

            //try
            //{
            //    GetPropertyRangeLimits = AccessTools.Method("UnityEngine.Shader:GetPropertyRangeLimits");
            //    Type[] args = GetPropertyRangeLimits.GetGenericArguments();
            //    if (args.Length >= 0)
            //        bGetPropertyRangeLimits = true;
            //}
            //catch (Exception)
            //{
            //    loader.log(LogLevel.Warning, $"Can't use GetPropertyRangeLimits. Don't care it.");
            //}

            //GetPropertyName = AccessTools.Method("UnityEngine.Rendering:GetPropertyName");
            try
            {
                FindPropertyIndex = AccessTools.Method("UnityEngine.Shader:FindPropertyIndex");
                Type[] args = FindPropertyIndex.GetGenericArguments();
                if (args.Length >= 0)
                    bFindPropertyIndex = true;
            }
            catch (Exception)
            {
                loader.log(LogLevel.Warning, $"Can't use FindPropertyIndex. Don't care it.");
            }

            // 아래 줄을 넣으면 유니티 2017.3에서는
            // MissingMethodException: Method not found: 'System.Reflection.MethodInfo.op_Inequality'.
            // 에러나면서 이 start()가 무효화 됨..
            //loader.log(LogLevel.Warning, $"GetPropertyType {GetPropertyType != null}");
            try
            {
                GetPropertyType = AccessTools.Method("UnityEngine.Shader:GetPropertyType", new Type[] { typeof(int) });
                Type[] args = GetPropertyType.GetGenericArguments();
                //loader.log(LogLevel.Warning, $"  GetPropertyType type {args.Length}");
                if (args.Length >= 0)
                    bGetPropertyType = true;
            }
            catch (NullReferenceException)
            {
                try
                {
                    GetPropertyType = AccessTools.Method("UnityEngine.Rendering:GetPropertyType", new Type[] { typeof(int) });
                    Type[] args = GetPropertyType.GetGenericArguments();
                    if (args.Length >= 0)
                        bGetPropertyType = true;
                }
                catch (Exception)
                {
                    loader.log(LogLevel.Warning, $"Can't use GetPropertyType. Don't care it.");
                }
            }
            catch (Exception)
            {
                loader.log(LogLevel.Warning, $"Can't use GetPropertyType. Don't care it.");
            }

            //loader.log(LogLevel.Info, $"bGetPropertyType {bGetPropertyType}");

            try
            {

                GetActiveScene = AccessTools.Method("UnityEngine.SceneManagement.SceneManager:GetActiveScene");
            }
            catch (Exception)
            {
                loader.log(LogLevel.Warning, $"Can't use GetActiveScene. Don't care it.");
            }

            //var scenemanager = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
            //                    from type in assembly.GetTypes()
            //                    where type.Name == "SceneManager" && type.GetMethods().Any(m => m.Name == "GetActiveScene")
            //                    select type);
            //if (scenemanager.Count() == 1)
            //{
            //    Type scene = scenemanager.GetType();
            //    Type[] interfaces = scene.GetInterfaces();
            //    foreach (Type type in interfaces)
            //    {
            //        loader.log(LogLevel.Warning, $"scene type {type}");
            //    }
            //}

#if !interop
            try
            {
                StartCoroutine("DoMain_Iemu");
                bUseCoroutine = true;
            }
            catch { }
#endif
        }


        async void testAsync()
        {
            await new Task(() => { Thread.Sleep(1); });
        }

        public enum ShaderPropertyType
        {
            Color = 0,
            Vector = 1,
            Float = 2,
            Range = 3,
            Texture = 4,  // TexEnv -> Texture
            TexEnv = 4
        }

        void PropertiesFloat_set(string str)
        {
            string[] key_str = str.Split(DecensorTools.separator);
            foreach (string item_ in key_str)
            {
                string item = item_.Trim();
                string[] vari = item.Split('=');
                if (vari.Count() == 2)
                {
                    float val;
                    // may be key's value can't be 0.
                    //int key = Shader.PropertyToID(vari[0]);
                    if (float.TryParse(vari[1], out val))
                    {
                        PropertiesFloat[vari[0]] = val;
                    }
                }
            }
            //foreach (KeyValuePair<int, float> item in PropertiesFloat)
            //{
            //    loader.log(LogLevel.Info, $"  float  {item.Key}: {item.Value:0.#######}");
            //}
        }
        void PropertiesVector_set(string str)
        {
            string[] key_str = str.Split(DecensorTools.separator_vector);
            foreach (string item_ in key_str)
            {
                string item = item_.Trim();
                string[] vari = item.Split('=');
                if (vari.Count() == 2)
                {
                    try
                    {
                        var val = vari[1].ToVector4(",", " ");
                        PropertiesVector[vari[0]] = val;
                    }
                    catch { }
                }
            }
            //foreach(KeyValuePair<int, Vector4> item in PropertiesVector)
            //{
            //    loader.log(LogLevel.Info, $"  vector  {item.Key}: {item.Value}");
            //}
        }

        void ShaderReplace_set()
        {
            // shader에 등록된 이름들이 있는 지 체크. shader_replace 구성.
            if (ShaderReplace.Value != string.Empty)
            {
                string[] key_str = ShaderReplace.Value.Split(DecensorTools.separator);
                foreach (string item_ in key_str)
                {
                    string item = item_.Trim();
                    string[] shader = item.Split('=');
                    if (shader.Count() == 2)
                    {
                        bool bFound = true;
                        if (Shader.Find(shader[0]) == null)
                        {
                            loader.log(LogLevel.Info, $"Shader Replace : {shader[0]} not found.");
                            bFound = false;
                        }
                        if (Shader.Find(shader[1]) == null)
                        {
                            loader.log(LogLevel.Info, $"Shader Replace : {shader[1]} not found.");
                            bFound = false;
                        }
                        if (bFound)
                            shader_replace[shader[0]] = shader[1];
                    }
                }
            }
            //foreach (KeyValuePair<string, string> item in shader_replace)
            //{
            //    loader.log(LogLevel.Info, $"  shader replace  {item.Key} -> {item.Value}");
            //}
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
                                layer_replace_str, layer_replace_str == string.Empty ? "" : DecensorTools.separator+" ",
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
            int ret = DecensorTools.default_MaximumLOD;
            foreach (string shad in DecensorTools.KnownShaders_MaximumLod__2)
            {
                if (Shader.Find(shad) != null)
                {
                    ret = -2;
                    break;
                }
            }
            return ret;
        }

        public static string RGB(string text, string RGB = "FFFFFF")
        {
            return string.Concat(new string[]
            {
                "<color=#",
                RGB,
                ">",
                text,
                "</color>"
            });
        }


        //public Vector2 scrollPosition;
        //public string longString = "메세지";
        //Size panelSize = new Size(200, 600);
        void OnGUI()
        {
            //scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(panelSize.width), GUILayout.Height(panelSize.height));
            //{
            //    GUILayout.Label(longString, new GUILayoutOption[] { });
            //    if (GUILayout.Button("지우기", new GUILayoutOption[] { }))
            //        longString = "";
            //}
            //GUILayout.EndScrollView();

            //if (GUILayout.Button("메세지 추가", new GUILayoutOption[]
            //{
            //    GUILayout.ExpandWidth(true),
            //    GUILayout.ExpandHeight(true)
            //}))
            //    longString += "\n헬로우 월드";

            //GUILayout.BeginArea(new Rect((float)(Screen.width / 2 - panelSize.width / 2), (float)(Screen.height / 2 - panelSize.height / 2), (float)panelSize.width, (float)panelSize.height), RGB("GlitterInvitationClient by Kalybr50", "FFFFFF"), new GUIStyle());
            //GUILayout.Space(30f);
            //GUILayout.BeginVertical(new GUILayoutOption[]
            //{
            //        GUILayout.ExpandWidth(true),
            //        GUILayout.ExpandHeight(true)
            //});
            //GUILayout.Label("Alice:", new GUILayoutOption[]
            //{
            //            GUILayout.Width(200f),
            //            GUILayout.Height(30f)
            //});
            //GUILayout.EndVertical();
            //GUILayout.EndArea();

            if (!bUseCoroutine)
            {
                DoMain();
            }

        }

#if !interop
        IEnumerator DoMain_Iemu()
        {
            while (true)
            {
                yield return null;
                //yield return new WaitForEndOfFrame();
                DoMain();
            }
        }
#endif

        void DoMain()
        {
            if (!bGetting_GOS)
            {
                if (!bInit_success) return; // 유니티 버전에 따라 실패할 경우가 있음.
                // scene buildIndex 로 scene이 바뀌었는지 체크
                int SceneNum;
                SceneNum = Application.loadedLevel;
                if (lastScene != SceneNum)
                {
                    // scene 이 바뀌면 loop_1 reset
                    lastScene = SceneNum;
                    if (RendererOnlyCheckMode.Value)
                        renderers_index = -1;
                    else
                        GOS.Clear();
                    scene_loop_count = 0;
                    //loader.log(LogLevel.Info, $"--------  Scene Changed {lastScene}");
                }

                //// LastGetGOS를 체크하여 tsLoopTimeLimit 조절
                //if (LastGetGOS != null && tsLoopTimeLimit_level < tsLoopTimeLimit_level_max && DateTime.Now > LastGetGOS + tsLoopTimeLimit_check)
                //{
                //    tsLoopTimeLimit_level = tsLoopTimeLimit_level >= tsLoopTimeLimit_level_max ? tsLoopTimeLimit_level_max : tsLoopTimeLimit_level++;
                //    tsLoopTimeLimit_check += TimeSpan.FromMilliseconds(5000);
                //    tsLoopTimeLimit = TimeSpan.FromMilliseconds(tsLoopTimeLimit_level);
                //    //loader.log(LogLevel.Warning, $"--------  tsLoopTimeLimit {tsLoopTimeLimit} --------");
                //}

                if (lastCheckedSameCount > DecensorTools.switchToSlow_GOSCountSame)
                    tsLoopTimeLimit = tsLoopTimeLimit_level_min;
                else
                {
                    // scene_loop_count 에 따라 tsLoopTimeLimit 조절
                    switch (scene_loop_count)
                    {
                        case 0:
                        case 1:
                            tsLoopTimeLimit = tsLoopTimeLimit_level_max;
                            break;
                        case 2:
                            tsLoopTimeLimit = tsLoopTimeLimit_level_default;
                            break;
                    }
                }
                //loader.log(LogLevel.Info, $"--------  scene_loop_count {scene_loop_count}");

                if (bIsFirstSet && !RendererOnlyCheckMode.Value && GOS_count > DecensorTools.switchToRendererModeCount)
                {
                    loader.log(LogLevel.Warning, $"  GOS count {GOS_count}. Switch to RendererOnlyCheckMode");
                    SwitchToRendererOnlyMode();
                }

                if (RendererOnlyCheckMode.Value)
                {
                    // renderers mode
                    if (next_check != null && renderers_index >= 0 && renderers_count > 0)
                    {
                        DateTime LoopLimit = DateTime.Now + tsLoopTimeLimit;
                        try
                        {
                            do
                            {
                                if (Decensor_renderer(renderers[renderers_index]))
                                    renderers_index++;
                                else
                                    renderers_index = -1;
                            } while (renderers_index < renderers_count && (DateTime.Now < LoopLimit));
                        }
                        catch (NullReferenceException)
                        {
                            renderers_index = -1;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            renderers_index = -1;
                        }
                        catch (Exception e)
                        {
                            loader.log(LogLevel.Error, $"Renderer Mode. {e.GetType()}\n{e.Message}");
                            renderers_index = -1;
                        }
                    }
                    else if (next_check == null || next_check < DateTime.Now)
                    {
                        if (renderers_index == -1)
                            scene_loop_count = 0;
                        else
                            scene_loop_count = scene_loop_count > 1 ? scene_loop_count : scene_loop_count + 1;

                        if (!bGetting_GOS)
                        {
                            if (bUseTask)
                                Get_Renderer_Async();
                            else
                                Get_Renderer();
                        }
                    }
                }
                else
                {
                    // GOS mode
                    if (next_check != null && GOS.Count > 0)
                    {
                        DateTime LoopLimit = DateTime.Now + tsLoopTimeLimit;
                        do
                        {
                            GameObject go = GOS.Last();
                            GOS.RemoveAt(GOS.Count - 1);

                            if (go != null)
                            {
                                if (bUseTask)
                                    Add_GOS_Child_Async(go);
                                else
                                    Add_GOS_Child(go);
                                Decensor_GameObject(go);
                                GOS_count++;
                            }
                        } while (GOS.Count > 0 && (DateTime.Now < LoopLimit));
                    }
                    else if (next_check == null || next_check < DateTime.Now)
                    {
                        scene_loop_count = scene_loop_count > 1 ? scene_loop_count : scene_loop_count + 1;

                        // 더 빨리 root의 GameObject를 구할 방법은 없을까. 10000건 넘어가면 lag 발생 (GlitterInvitation)
                        //foreach (Transform xform in UnityEngine.Object.FindObjectsOfType<Transform>().Where(x => x.parent == null).ToArray())

                        if (!bGetting_GOS)
                        {
                            if (bUseTask)
                                Get_GOS_Async();
                            else
                                Get_GOS();
                        }
                    }
                }
            }
        }


        static async void Get_Renderer_Async()
        {
            if (!bGetting_GOS)
            {
                Task task = Task.Factory.StartNew(() => Get_Renderer());
                Thread.Sleep(1);
                await task;
            }
        }

        static void Get_Renderer()
        {
            bGetting_GOS = true;
#if interop
            var array = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Renderer>());
            renderers = array.Select(x => x.Cast<Renderer>()).ToArray();
#else
            renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
#endif
            if (lastCheckedCount == renderers_count)
                lastCheckedSameCount = lastCheckedSameCount > DecensorTools.switchToSlow_GOSCountSame ? lastCheckedSameCount = DecensorTools.switchToSlow_GOSCountSame + 1 : lastCheckedSameCount + 1;
            else
                lastCheckedSameCount = 0;
            lastCheckedCount = renderers_count;

            renderers_count = renderers.Length;
            loader.log(LogLevel.Info, $"--------  Renderers {renderers_count} {lastCheckedSameCount} --------");
            if (renderers_count > 0)
                renderers_index = 0;
            next_check = DateTime.Now + tsCheckInterval;
            bGetting_GOS = false;
        }


        static async void Add_GOS_Child_Async(GameObject go)
        {
            Task task = Task.Factory.StartNew(() => Add_GOS_Child(go));
            await task;
        }
        static void Add_GOS_Child(GameObject go)
        {
            bGetting_GOS = true;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject go_ = go.transform.GetChild(i).gameObject;
                if (go_ != null)
                    GOS.Add(go_);
            }
            bGetting_GOS = false;
        }


        static async void Get_GOS_Async()
        {
            if (!bGetting_GOS)
            {
                Task task = Task.Factory.StartNew(() => Get_GOS());
                Thread.Sleep(1);
                await task;
            }
        }

        static void Get_GOS()
        {
            bGetting_GOS = true;
            DateTime? now = null;
            if (bIsFirstSet && !RendererOnlyCheckMode.Value)
                now = DateTime.Now;
            GOS.Clear();
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

            if (lastCheckedCount == GOS_count)
                lastCheckedSameCount = lastCheckedSameCount > DecensorTools.switchToSlow_GOSCountSame ? lastCheckedSameCount = DecensorTools.switchToSlow_GOSCountSame + 1 : lastCheckedSameCount + 1;
            else
                lastCheckedSameCount = 0;
            lastCheckedCount = GOS_count;

            loader.log(LogLevel.Info, $"--------  GOS(root) {GOS.Count} prev total {GOS_count} {lastCheckedSameCount} --------");
            GOS_count = 0;
            //LastGetGOS = DateTime.Now;
            next_check = DateTime.Now + tsCheckInterval;
            if (bIsFirstSet && !RendererOnlyCheckMode.Value)
            {
                if ((DateTime.Now - now) > DecensorTools.switchToRendererModeTimeSpan)
                {
                    GetGOS_TooLate_Count++;
                    if (GetGOS_TooLate_Count > 10)
                    {
                        loader.log(LogLevel.Warning, $"  Get_GOS takes too longtome.({DateTime.Now - now}) Switch to RendererOnlyCheckMode");
                        SwitchToRendererOnlyMode();
                    }
                }
            }
            bGetting_GOS = false;
        }

        static void SwitchToRendererOnlyMode()
        {
            GOS.Clear();
            GOS_count = 0;
            RendererOnlyCheckMode.Value = true;
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
                UnityEngine.UI.Image[] images = go.GetComponentsInChildren<UnityEngine.UI.Image>(false);
                if (images != null)
                {
                    foreach (UnityEngine.UI.Image image in images)
                    {
                        Decensor_image(image);
                    }
                }
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

                List <Material> mats_added = new List<Material>();
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

        static void Decensor_image(UnityEngine.UI.Image image)
        {
            //loader.log(LogLevel.Info, $"  r {renderer.name} {renderer.materials.Count()} {foundR}");
            try
            {
                if (Decensor_material(image.material))
                    image.enabled = false;
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
                            loader.log(LogLevel.Info, $"      found  {mat_name} {shader_name} {foundM!=string.Empty} {foundS != string.Empty} NeedDeleted {NeedDeleted} index {GOS_count} {renderers_index}");

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

#if USE_SETVALUE_HOOK
        [HarmonyPatch(typeof(Material), "SetFloat", new Type[] { typeof(string), typeof(float) })]
        public class Material_SetFloat_string
        {
            [HarmonyPrefix]
            public static void Prefix(Material __instance, string name, ref float value)
            {
                //loader.log(LogLevel.Info, $"  Material {__instance.name} {name} {value} {bIsForceSetValue}");
                if (!bIsForceSetValue
                    && PropertiesFloat.ContainsKey(name)
                    && (__instance != null && __instance.shader != null
                        && (DecensorTools.MatchKeywords(__instance.name) != string.Empty
                        || DecensorTools.MatchKeywords(__instance.shader.name) != string.Empty)
                    )
                )
                {
                    //loader.log(LogLevel.Info, $"  Material {__instance.name} {name} {value} {bIsForceSetValue}");
                    value = PropertiesFloat[name];
                }
            }
        }

        [HarmonyPatch(typeof(Material), "SetFloat", new Type[] { typeof(int), typeof(float) })]
        public class Material_SetFloat_int
        {
            [HarmonyPrefix]
            public static void Prefix(Material __instance, int nameID, ref float value)
            {
                if (GetPropertyName != null)
                {
                    string name = (string)GetPropertyName.Invoke(__instance.shader, new object[] { nameID });
                    //loader.log(LogLevel.Info, $"  Material {__instance.name} {name} {value} {bIsForceSetValue}");

                    if (!bIsForceSetValue
                        && PropertiesFloat.ContainsKey(name)
                        && (__instance != null && __instance.shader != null
                            && (DecensorTools.MatchKeywords(__instance.name) != string.Empty
                            || DecensorTools.MatchKeywords(__instance.shader.name) != string.Empty)
                        )
                    )
                    {
                        //loader.log(LogLevel.Info, $"  Material {__instance.name} {nameID} {value} {bIsForceSetValue}");
                        value = PropertiesFloat[name];
                    }
                }
            }
        }
        [HarmonyPatch(typeof(Material), "SetVector", new Type[] { typeof(int), typeof(Vector4) })]
        public class Material_SetVector_int
        {
            [HarmonyPrefix]
            public static void Prefix(Material __instance, int nameID, ref Vector4 value)
            {
                if (GetPropertyName != null)
                {
                    string name = (string)GetPropertyName.Invoke(__instance.shader, new object[] { nameID });

                    if (!bIsForceSetValue
                        && PropertiesFloat.ContainsKey(name)
                        && (__instance != null && __instance.shader != null
                            && (DecensorTools.MatchKeywords(__instance.name) != string.Empty
                            || DecensorTools.MatchKeywords(__instance.shader.name) != string.Empty)
                        )
                    )
                    {
                        //loader.log(LogLevel.Info, $"  Material {__instance.name} {nameID} {value} {bIsForceSetValue}");
                        value = PropertiesVector[name];
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Material), "SetVector", new Type[] { typeof(string), typeof(Vector4) })]
        public class Material_SetVector_string
        {
            [HarmonyPrefix]
            public static bool Prefix(Material __instance, string name, ref Vector4 value)
            {
                __instance.SetVector(Shader.PropertyToID(name), value);
                return false;
            }
        }
#endif

        //[HarmonyPatch(typeof(GameObject), "SetActive", new Type[] { typeof(bool) })]
        //public class GameObject_SetActive
        //{
        //    [HarmonyPostfix]
        //    public static void Postfix(GameObject __instance, bool value)
        //    {
        //        //loader.log(LogLevel.Info, $"  GameObject {__instance.name} {value}");
        //        if (value)
        //        {
        //            Decensor_GameObject(__instance);
        //        }
        //    }
        //}

    }
}
public static class StringVector4Extensions
{
    public static Vector4 ToVector4(this string str, params string[] delimiters)
    {
        if (str == null) throw new ArgumentNullException("string is null");
        if (string.IsNullOrEmpty(str)) throw new FormatException("string is empty");
        if (string.IsNullOrWhiteSpace(str)) throw new FormatException("string is just white space");

        if (delimiters == null) throw new ArgumentNullException("delimiters are null");
        if (delimiters.Length <= 0) throw new InvalidOperationException("missing delimiters");

        var rslt = str
        .Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
        .Select(float.Parse)
        .ToArray()
        ;

        if (rslt.Length != 4)
            throw new FormatException("The input string does not follow" +
                                        "the required format for the string." +
                                        "There has to be four floats inside" +
                                        "the string delimited by one of the" +
                                        "requested delimiters. input string: " +
                                        str);
        return new Vector4(rslt[0], rslt[1], rslt[2], rslt[3]);
    }
}
