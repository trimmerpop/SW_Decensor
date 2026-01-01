Removes soft-mosaics from Unity games.

It's a plugin of [BepInEx](https://github.com/BepInEx/BepInEx).

file released as 3 types.
 - SW_Decensor_BE5.dll - for BepInEx 5 mono
 - SW_Decensor_BE6.dll - for BepInEx 6 mono
 - SW_Decensor_il2cpp.dll - for BepInEx 6 il2cpp

The goal was to complete the basic operation in version v0.7, and to complete finding mosaics through the UI in v1.0.
But, there are many concerns, such as how to find the UI and mosaics, and development has paused for now.

I'm thinking about creating a separate analysis tool.

This is a tool that allows you to make detailed settings, but many things are set to default values, so some games may have broken graphics with the initial settings.

### About Config file

#### Keywords = *moza*, *mosa*, blah blah

Try match KeyWords with GameObject name, Renderer name, Material name, Shader name. if matched, check GameObject can be disabled, check Shader's variables(check ShaderPropertiesFloat , ShaderPropertiesVector), Try Layer replace, ... and so on.

You can use wildcard(*) as 3 types like below.
  - *keyword*
  - keyword*
  - *keyword

If capital exists, compare with Case-Sensitive.

"mosa" match with "MoSa", "mosA", ...

"Mosa" not match with "mosa"

\## To Remove Keywords. check the Material name.

\# Setting type: String

\# Default value:

#### RemoveKeyWords = Moza_PenisPussy*, blah blah

If matched with GameObject name, try disable GameObject. matched material's name, try to remove it from Renderer. If set incorrectly, 
it can lead to undesirable results.

\## Set float value to Material Shader Properties.

\# Setting type: String

\# Default value:

#### ShaderPropertiesFloat =_CellSize=0.000001, _Pixelation=0.000001, blah blah

Sometimes you may get strange results by assigning values to the wrong Shader. It only applies to things that match KeyWords. Some games may appear to freeze (make the game unplayable) if you specify values that exceed the allowed values. 魔法少女(メスガキ)はやっぱりバトルでわからせたい(Magical Buster Girl, JSK Games) set _CellSize=0.000001, game freezed. Don't worry, I remove the shader which used by JSK games. However, this is a symptom that can appear in other games.

\## Set vector value to Material Shader Properties. seperator:))

\# Setting type: String

\# Default value:

#### ShaderPropertiesVector = _CellSize=0.000001, 0.000001, 0, 0: _Size=0.000001, 0.000001, 0, 0

Same as ShaderPropertiesFloat. Note that value sets are separated by ":".

\## Use maximumLOD value. 1: NOT use. If want use this feature, set to -2 or 0.

\# Setting type: Int32

\# Default value: 1

#### maximumLOD = 1

maximumLOD = -2. It's a like magic. Some games will disable the shader because of this. I don't know much about Unity, but sometimes giving this value -2 or 0 will eliminate the headache. However, it can also produce strange results. The default value of 1 means not used.

\## Use shader replace method. ex) Standard Mosaic=Standard

\# Setting type: String

\# Default value:

#### ShaderReplace =

Some games cannot delete materials, so they disable shaders by replacing them.

\## Use layer replace method. ex) Mosaic=Default

\# Setting type: String

\# Default value:

#### LayerReplace =
