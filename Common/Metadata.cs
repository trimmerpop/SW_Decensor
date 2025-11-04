using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;


#if interop
using Il2CppInterop.Runtime;
    using loader = SW_Decensor_il2cpp.loader;
#endif
#if mono
using loader = SW_Decensor_BE5.loader;
#endif


namespace Common
{
    internal static class Metadata
    {
        public const string AUTHOR = "kumarin";
        public const string VERSION = "0.7.3.1";

#if interop
        public const string MODNAME = "SW_Decensor_il2cpp";
#else
        public const string MODNAME = "SW_Decensor";
#endif
        public const string GUID = "com." + AUTHOR + "." + MODNAME;
    }

    enum FindWay_enum
    {
        Contains = 0,
        StartWith,
        EndWith,
        Equal
    }
    public struct KeyWord
    {
        public KeyWord(string key, int findway)
        {
            this.IsCaseSensitive = key != key.ToLower() && key != key.ToUpper();
            if (this.IsCaseSensitive)
                this.Key = key;
            else
                this.Key = key.ToLower();
            this.Findway = findway;
        }

        public int Findway { get; }
        public string Key { get; }
        public bool IsCaseSensitive { get; }
    }
    internal static class DecensorTools
    {
        public const char separator = ',';
        public const char separator_vector = ':';
        public const int switchToRendererModeCount = 10000;
        public static TimeSpan switchToRendererModeTimeSpan = new TimeSpan(0, 0, 0, 0, 4);  // GOS를 구하는 시간이 너무 길면 renderer 모드로.
        public const int switchToLayerReplaceAllCount = 30; // layer에 있는 GO들을 전부 옮길 것인가 판단
        public const int LayerReplacMode_All = -1;  // layer에 있는 GO들을 전부 옮길
        public const int LayerReplacMode_EA = -2;   // layer에 있는 GO중 이름이 걸리면 옮김
        public const int switchToSlow_GOSCountSame = 10;
        //public const string default_KeyWords = "*moz*, *mos*, *pixelat*, *censor*, *maz*, *masa*, *penis*, *vag*, *ちんこ*, *まんこ*, *モザイク*, *BlackCube*";
        public const string default_KeyWords = "*moza*, *mosa*, *mozi*, *mosi*, *mazi*, *masa*, *pixel*, *censor*, *cylinder*, *dildo*, *dick*, *penis*, *vag*, *pussy*, *tinpo*, *tama*, *ちんこ*, *まんこ*, *モザイク*, Capsule, xy.shape";
        // *masaco* : 'Sailing! I Want to Fuck the Big-Breasted Captain of My Dreams!'
        //public const string default_RemoceKeyWords = "Moza_PenisPussy*, PixelationMask, mozike, Pya/mosaic, Shader Graphs/MosaicShaderGraph, Shader Graphs/UniversalMosaic";
        public const string default_RemoceKeyWords = "Moza_PenisPussy*, PixelationMask, mozike, CensorCoochie, NakedBodyVag, PUSSY_MOZA, MozImage, TinMosaic, ManMosaic, MosaicFieldVagina, 马, 马赛克, CensorMaterial*, mosaic_rect_*, BlackCube*, color_black*, *_mosaic, *_censor*, StrictMosaic_Stencil*";
        //public const string default_ShaderPropertiesFloat = "Vector1_C962968C=0, Vector1_5709BF4F=0"; _MosaicDetail=0
        public const string default_ShaderPropertiesFloat = "_CellSize=0.000001, _Pixelation=0.000001, _Size=0.000001, _size=8192, _SizePer=8192, _BlockSize=0.000001, _Scale=0, _SecondMoza=0, _MosaicResolution=4096, _X1=1024, _Y1=1024, _PixelSize=1, _Mosa_On=0, _MosaPixSize=1, _MosaPixSize2=1, _ZTest=1, _MinPx=0.000001, _Ratio=0, _ZWrite=0";
        public const string default_ShaderPropertiesVector = "_CellSize=0.000001, 0.000001, 0, 0: _Size=0.000001, 0.000001, 0, 0";
        public const int default_MaximumLOD = 1;
        public static Dictionary<string, KeyWord> KeyWords { get; set; } = new Dictionary<string, KeyWord>();
        public static Dictionary<string, KeyWord> RemoveKeyWords { get; set; } = new Dictionary<string, KeyWord>();

        public static string[][] KnownShaders =
        {
            new string[] { "Mogura/MoguraMosaicVCol", "Mogura/MoguraToonCoverVCol" },
            new string[] { "Mogura/MoguraToonVCol_MosaicTarget_NoOutline", "Mogura/MoguraToonVCol" },   // Ricca
            new string[] { "UnityChanToonShader/NoOutline/ToonColor_ShadingGradeMap_StencilMask", "UnityChanToonShader/NoOutline/ToonColor_ShadingGradeMap_StencilOut" },    // わからせりぃなちゃん
            new string[] { "LocalPixelize/LocalPixelizeByURPGrabPass", "SemiTransparent" },    // Ayamelluxie
            new string[] { "Shader Graphs/Mosaic", "Shader Graphs/Sweat" },    // やよいちゃんを開発したい!
            new string[] { "UnityChanToonShader/Toon_DoubleShadeWithFeather", "Toon/Basic" }    // Snow Brandia Fairy Later
        };
        public static string[][] KnownLayers =
        {
            new string[] { "Mosaic", "Default" },
            new string[] { "PlayerNoView", "Default" },  // ムカつく隣人の美人姉妹を催眠で犯る体験
            new string[] { "Dick", "Default" },  // Bocchi the Fakku Ex
            //new string[] { "Danmenzu_Vagina", "Danmenzu_Vagina" },   // でれあへ？妹発情催眠アプリ！ 문제 생김
            new string[] { "Pixelation", "Default" },   // Futa Succu ReaseLotte Adventure 4 -Mastema's Conspiracy-__
            new string[] { "Mask", "Default" }   // オフィス恐怖物語
        };
        public static string[] KnownLayers_NOT =    // 처리하면 안되는 layer 이름
        {
            "Danmenzu_Vagina"   // でれあへ？妹発情催眠アプリ！ v1.2.8
        };
        public static string[] KnownLayers_LayerReplacMode_All =    // LayerReplacMode_All로 처리해야할 layer 이름
        {
            "Mosaix"
        };
        public static string[] KnownShaders_Remove =    // 알려진 지울 shaders
        {
            "Pya/mosaic",   // JSK and many
            "Shader Graphs/StrictMosaic_URP",   // 露出大好き新人OL_かのんちゃんのオナチャレ！～社内編～
            "Shader Graphs/MosaicShaderGraph",  // look hac
            "Amplify/Amplify",   // hypnoApp
            "Shader Graphs/ShaderGraph_Mosaic",  // ねるこ こっそりママ化計画!! 今度はアイドルだ!!
            "Shader Graphs/UniversalMosaic", // ボクのそぼ濡れアドベンチャー v1.00
            "Shader Graphs/Mosaic",  // 洋館から逃げる
            "Custom/Mosaic", // EROTAS2 -Ordeal from Fairy-
            "Ist/MosaicField",
            //"Ist/MosaicField_R", // Insult Order
            //"FX/Censor", // Oedo Trigger. サドルの上の監獄 에서는 지우면 다시 나타남.
            "Live2D Cubism/Unlit Mosaic Como",   // Oshikake Shoujo
            "Custom\\Pixelate",  // 乱暴変態 ~対風紀委員会編~
            "Shader Graphs/mos", // 出航!憧れの巨乳船長とやりたい!射精放題‼
            "2DxFX/Standard/Pixel"  // Hounds of the Meteor
        };
        public static string[] KnownShaders_NotToSet =   // SetFloat 등을 하지 말아야 할 Shader들
        {
            "Poiyomi Toon",  // Dawn of Marionette
            "lilToon"   // JKレイプVR
        };

        public static string[] KnownShaders_MaximumLod__2 =   // MaximumLOD를 -2로 세팅해야 할 Shader들
        {
            "FX/Censor",
            "FX/Censor (Masked Smooth)",
            "FX/Censor (Masked Cutout)",
            "FX/CensorMask"
        };


        public static void Init_KeyWords(string str = "", bool IsRemoveKeyWords = false)
        {
            string[] key_str = str.Split(separator);
            //foreach (string keyWord in str.Split(separator).Select(p => p.Trim()).ToArray())
            //loader.log(LogLevel.Info, $" key_str {key_str.Length}");
            if (key_str.Length > 0)
            {
                foreach (string keyWord_ in key_str)
                {
                    //loader.log(LogLevel.Info, $" key {keyWord_}");
                    string keyWord = keyWord_.Trim();
                    int length = keyWord.Length;
                    if (length > 0)
                    {
                        if (keyWord[0] == '*' && keyWord[length - 1] == '*')
                        {
                            if (IsRemoveKeyWords)
                                RemoveKeyWords[keyWord] = new KeyWord(keyWord.Substring(1, length - 2), 0);
                            else
                                KeyWords[keyWord] = new KeyWord(keyWord.Substring(1, length - 2), 0);
                        }
                        else if (keyWord[length - 1] == '*')
                        {
                            if (IsRemoveKeyWords)
                                RemoveKeyWords[keyWord] = new KeyWord(keyWord.Substring(0, length - 1), 1);
                            else
                                KeyWords[keyWord] = new KeyWord(keyWord.Substring(0, length - 1), 1);
                        }
                        else if (keyWord[0] == '*')
                        {
                            if (IsRemoveKeyWords)
                                RemoveKeyWords[keyWord] = new KeyWord(keyWord.Substring(1, length - 1), 2);
                            else
                                KeyWords[keyWord] = new KeyWord(keyWord.Substring(1, length - 1), 2);
                        }
                        else
                        {
                            if (IsRemoveKeyWords)
                                RemoveKeyWords[keyWord] = new KeyWord(keyWord.Substring(0, length), 3);
                            else
                                KeyWords[keyWord] = new KeyWord(keyWord.Substring(0, length), 3);
                        }
                    }
                }
            }
        }

        //public static string MatchKeywordAsync(string str, bool IsRemoveKeyword = false)
        //{
        //    Task<string> task = Task<string>.Factory.StartNew(() =>
        //        DecensorTools.MatchKeywords(str, IsRemoveKeyword));
        //    return task.Result;
        //}

        public static string MatchKeywords(string str, bool IsRemoveKeyWords = false)
        {
            string found = string.Empty;
            if (!string.IsNullOrEmpty(str))
            {
                foreach (KeyValuePair<string, KeyWord> entry in (IsRemoveKeyWords ? RemoveKeyWords : KeyWords))
                {
                    switch (entry.Value.Findway)
                    {
                        case 0:
                            if (entry.Value.IsCaseSensitive)
                            {
                                if (str.Contains(entry.Value.Key))
                                    found = str;
                            }
                            else if (str.ToLower().Contains(entry.Value.Key))
                                found = str;
                            break;
                        case 1:
                            if (entry.Value.IsCaseSensitive)
                            {
                                if (str.StartsWith(entry.Value.Key))
                                    found = str;
                            }
                            else if (str.ToLower().StartsWith(entry.Value.Key))
                                found = str;
                            break;
                        case 2:
                            if (entry.Value.IsCaseSensitive)
                            {
                                if (str.EndsWith(entry.Value.Key))
                                    found = str;
                            }
                            else if (str.ToLower().EndsWith(entry.Value.Key))
                                found = str;
                            break;
                        case 3:
                            if (entry.Value.IsCaseSensitive)
                            {
                                if (str == entry.Value.Key)
                                    found = str;
                            }
                            else if (str.ToLower() == entry.Value.Key)
                                found = str;
                            break;
                    }
                    if (found != string.Empty)
                        break;
                }
            }
            return found;
        }

        public static string GetDecensoredShaderName(string str)
        {
            string ret = string.Empty;
            string test = string.Empty;
            string[] key_str = str.Split(new char[] { ' ', '_' });
            //foreach (string keyWord in str.Split(' ').Select(p => p.Trim()).ToArray())
            foreach (string keyWord_ in key_str)
            {
                string keyWord = keyWord_.Trim();
                if (MatchKeywords(keyWord) == string.Empty)
                    test = string.Format("{0}{1}{2}", test, (test != string.Empty ? " " : ""), keyWord);
                if (str != test && Shader.Find(test) != null)
                    ret = test;
            }
            return ret;
        }

    }

    public class Size
    {
        public int width { get; set; }
        public int height { get; set; }
        public Size()
        {
            width = 0; height = 0;
        }
        public Size(int _width, int _height)
        {
            width = _width;
            height = _height;
        }
    }
}