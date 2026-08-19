
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.0.30319.33440
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit StardewValley.Menus.ChatTextBox
       extends StardewValley.Menus.TextBox
{
  .field public class StardewValley.Menus.IClickableMenu parentMenu
  .field public class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> finalText
  .field public float32 currentWidth
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D textBoxTexture,
                               class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D caretTexture,
                               class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont font,
                               valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color textColor) cil managed
  {
    // Code size       23 (0x17)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  newobj     instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::.ctor()
    IL_0006:  stfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_000b:  ldarg.0
    IL_000c:  ldarg.1
    IL_000d:  ldarg.2
    IL_000e:  ldarg.3
    IL_000f:  ldarg.s    textColor
    IL_0011:  call       instance void StardewValley.Menus.TextBox::.ctor(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                          class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                          class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont,
                                                                          valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_0016:  ret
  } // end of method ChatTextBox::.ctor

  .method public hidebysig instance void 
          reset() cil managed
  {
    // Code size       23 (0x17)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldc.r4     0.0
    IL_0006:  stfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
    IL_000b:  ldarg.0
    IL_000c:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0011:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::Clear()
    IL_0016:  ret
  } // end of method ChatTextBox::reset

  .method public hidebysig instance void 
          setText(string text) cil managed
  {
    // Code size       14 (0xe)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void StardewValley.Menus.ChatTextBox::reset()
    IL_0006:  ldarg.0
    IL_0007:  ldarg.1
    IL_0008:  callvirt   instance void StardewValley.Menus.TextBox::RecieveTextInput(string)
    IL_000d:  ret
  } // end of method ChatTextBox::setText

  .method public hidebysig virtual instance void 
          RecieveTextInput(string text) cil managed
  {
    // Code size       186 (0xba)
    .maxstack  3
    .locals init (class StardewValley.Menus.ChatSnippet V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0006:  callvirt   instance int32 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Count()
    IL_000b:  brtrue.s   IL_0027

    IL_000d:  ldarg.0
    IL_000e:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0013:  ldstr      ""
    IL_0018:  call       valuetype StardewValley.LocalizedContentManager/LanguageCode StardewValley.LocalizedContentManager::get_CurrentLanguageCode()
    IL_001d:  newobj     instance void StardewValley.Menus.ChatSnippet::.ctor(string,
                                                                              valuetype StardewValley.LocalizedContentManager/LanguageCode)
    IL_0022:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::Add(!0)
    IL_0027:  ldarg.0
    IL_0028:  ldfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
    IL_002d:  call       valuetype StardewValley.LocalizedContentManager/LanguageCode StardewValley.LocalizedContentManager::get_CurrentLanguageCode()
    IL_0032:  call       class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.ChatBox::messageFont(valuetype StardewValley.LocalizedContentManager/LanguageCode)
    IL_0037:  ldarg.1
    IL_0038:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont::MeasureString(string)
    IL_003d:  ldfld      float32 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::X
    IL_0042:  add
    IL_0043:  ldarg.0
    IL_0044:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0049:  ldc.i4.s   16
    IL_004b:  sub
    IL_004c:  conv.r4
    IL_004d:  blt.un.s   IL_0050

    IL_004f:  ret

    IL_0050:  ldarg.0
    IL_0051:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0056:  call       !!0 [System.Linq]System.Linq.Enumerable::Last<class StardewValley.Menus.ChatSnippet>(class [System.Runtime]System.Collections.Generic.IEnumerable`1<!!0>)
    IL_005b:  stloc.0
    IL_005c:  ldloc.0
    IL_005d:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_0062:  brfalse.s  IL_0078

    IL_0064:  ldloc.0
    IL_0065:  dup
    IL_0066:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_006b:  ldarg.1
    IL_006c:  call       string [System.Runtime]System.String::Concat(string,
                                                                      string)
    IL_0071:  stfld      string StardewValley.Menus.ChatSnippet::message
    IL_0076:  br.s       IL_008e

    IL_0078:  ldarg.0
    IL_0079:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_007e:  ldarg.1
    IL_007f:  call       valuetype StardewValley.LocalizedContentManager/LanguageCode StardewValley.LocalizedContentManager::get_CurrentLanguageCode()
    IL_0084:  newobj     instance void StardewValley.Menus.ChatSnippet::.ctor(string,
                                                                              valuetype StardewValley.LocalizedContentManager/LanguageCode)
    IL_0089:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::Add(!0)
    IL_008e:  ldarg.0
    IL_008f:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0094:  call       !!0 [System.Linq]System.Linq.Enumerable::Last<class StardewValley.Menus.ChatSnippet>(class [System.Runtime]System.Collections.Generic.IEnumerable`1<!!0>)
    IL_0099:  ldarg.0
    IL_009a:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_009f:  call       !!0 [System.Linq]System.Linq.Enumerable::Last<class StardewValley.Menus.ChatSnippet>(class [System.Runtime]System.Collections.Generic.IEnumerable`1<!!0>)
    IL_00a4:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_00a9:  call       string StardewValley.Utility::FilterDirtyWordsIfStrictPlatform(string)
    IL_00ae:  stfld      string StardewValley.Menus.ChatSnippet::message
    IL_00b3:  ldarg.0
    IL_00b4:  call       instance void StardewValley.Menus.ChatTextBox::updateWidth()
    IL_00b9:  ret
  } // end of method ChatTextBox::RecieveTextInput

  .method public hidebysig virtual instance void 
          RecieveTextInput(char inputChar) cil managed
  {
    // Code size       23 (0x17)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarga.s   inputChar
    IL_0003:  call       instance string [System.Runtime]System.Char::ToString()
    IL_0008:  dup
    IL_0009:  brtrue.s   IL_0011

    IL_000b:  pop
    IL_000c:  ldstr      ""
    IL_0011:  callvirt   instance void StardewValley.Menus.TextBox::RecieveTextInput(string)
    IL_0016:  ret
  } // end of method ChatTextBox::RecieveTextInput

  .method public hidebysig virtual instance void 
          RecieveCommandInput(char command) cil managed
  {
    // Code size       27 (0x1b)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_0006:  brfalse.s  IL_0013

    IL_0008:  ldarg.1
    IL_0009:  ldc.i4.8
    IL_000a:  bne.un.s   IL_0013

    IL_000c:  ldarg.0
    IL_000d:  call       instance void StardewValley.Menus.ChatTextBox::backspace()
    IL_0012:  ret

    IL_0013:  ldarg.0
    IL_0014:  ldarg.1
    IL_0015:  call       instance void StardewValley.Menus.TextBox::RecieveCommandInput(char)
    IL_001a:  ret
  } // end of method ChatTextBox::RecieveCommandInput

  .method public hidebysig instance void 
          backspace() cil managed
  {
    // Code size       146 (0x92)
    .maxstack  4
    .locals init (class StardewValley.Menus.ChatSnippet V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0006:  callvirt   instance int32 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Count()
    IL_000b:  ldc.i4.0
    IL_000c:  ble.s      IL_008b

    IL_000e:  ldarg.0
    IL_000f:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0014:  call       !!0 [System.Linq]System.Linq.Enumerable::Last<class StardewValley.Menus.ChatSnippet>(class [System.Runtime]System.Collections.Generic.IEnumerable`1<!!0>)
    IL_0019:  stloc.0
    IL_001a:  ldloc.0
    IL_001b:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_0020:  brfalse.s  IL_006a

    IL_0022:  ldloc.0
    IL_0023:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_0028:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_002d:  ldc.i4.1
    IL_002e:  ble.s      IL_0050

    IL_0030:  ldloc.0
    IL_0031:  ldloc.0
    IL_0032:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_0037:  ldloc.0
    IL_0038:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_003d:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0042:  ldc.i4.1
    IL_0043:  sub
    IL_0044:  callvirt   instance string [System.Runtime]System.String::Remove(int32)
    IL_0049:  stfld      string StardewValley.Menus.ChatSnippet::message
    IL_004e:  br.s       IL_008b

    IL_0050:  ldarg.0
    IL_0051:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0056:  ldarg.0
    IL_0057:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_005c:  callvirt   instance int32 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Count()
    IL_0061:  ldc.i4.1
    IL_0062:  sub
    IL_0063:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::RemoveAt(int32)
    IL_0068:  br.s       IL_008b

    IL_006a:  ldloc.0
    IL_006b:  ldfld      int32 StardewValley.Menus.ChatSnippet::emojiIndex
    IL_0070:  ldc.i4.m1
    IL_0071:  beq.s      IL_008b

    IL_0073:  ldarg.0
    IL_0074:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0079:  ldarg.0
    IL_007a:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_007f:  callvirt   instance int32 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Count()
    IL_0084:  ldc.i4.1
    IL_0085:  sub
    IL_0086:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::RemoveAt(int32)
    IL_008b:  ldarg.0
    IL_008c:  call       instance void StardewValley.Menus.ChatTextBox::updateWidth()
    IL_0091:  ret
  } // end of method ChatTextBox::backspace

  .method public hidebysig instance void 
          receiveEmoji(int32 emoji) cil managed
  {
    // Code size       49 (0x31)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
    IL_0006:  ldc.r4     40.
    IL_000b:  add
    IL_000c:  ldarg.0
    IL_000d:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0012:  ldc.i4.s   16
    IL_0014:  sub
    IL_0015:  conv.r4
    IL_0016:  ble.un.s   IL_0019

    IL_0018:  ret

    IL_0019:  ldarg.0
    IL_001a:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_001f:  ldarg.1
    IL_0020:  newobj     instance void StardewValley.Menus.ChatSnippet::.ctor(int32)
    IL_0025:  callvirt   instance void class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::Add(!0)
    IL_002a:  ldarg.0
    IL_002b:  call       instance void StardewValley.Menus.ChatTextBox::updateWidth()
    IL_0030:  ret
  } // end of method ChatTextBox::receiveEmoji

  .method public hidebysig instance void 
          updateWidth() cil managed
  {
    // Code size       118 (0x76)
    .maxstack  3
    .locals init (valuetype [System.Collections]System.Collections.Generic.List`1/Enumerator<class StardewValley.Menus.ChatSnippet> V_0,
             class StardewValley.Menus.ChatSnippet V_1)
    IL_0000:  ldarg.0
    IL_0001:  ldc.r4     0.0
    IL_0006:  stfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
    IL_000b:  ldarg.0
    IL_000c:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0011:  callvirt   instance valuetype [System.Collections]System.Collections.Generic.List`1/Enumerator<!0> class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::GetEnumerator()
    IL_0016:  stloc.0
    .try
    {
      IL_0017:  br.s       IL_005c

      IL_0019:  ldloca.s   V_0
      IL_001b:  call       instance !0 valuetype [System.Collections]System.Collections.Generic.List`1/Enumerator<class StardewValley.Menus.ChatSnippet>::get_Current()
      IL_0020:  stloc.1
      IL_0021:  ldloc.1
      IL_0022:  ldfld      string StardewValley.Menus.ChatSnippet::message
      IL_0027:  brfalse.s  IL_0049

      IL_0029:  ldloc.1
      IL_002a:  call       valuetype StardewValley.LocalizedContentManager/LanguageCode StardewValley.LocalizedContentManager::get_CurrentLanguageCode()
      IL_002f:  call       class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.ChatBox::messageFont(valuetype StardewValley.LocalizedContentManager/LanguageCode)
      IL_0034:  ldloc.1
      IL_0035:  ldfld      string StardewValley.Menus.ChatSnippet::message
      IL_003a:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont::MeasureString(string)
      IL_003f:  ldfld      float32 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::X
      IL_0044:  stfld      float32 StardewValley.Menus.ChatSnippet::myLength
      IL_0049:  ldarg.0
      IL_004a:  ldarg.0
      IL_004b:  ldfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
      IL_0050:  ldloc.1
      IL_0051:  ldfld      float32 StardewValley.Menus.ChatSnippet::myLength
      IL_0056:  add
      IL_0057:  stfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
      IL_005c:  ldloca.s   V_0
      IL_005e:  call       instance bool valuetype [System.Collections]System.Collections.Generic.List`1/Enumerator<class StardewValley.Menus.ChatSnippet>::MoveNext()
      IL_0063:  brtrue.s   IL_0019

      IL_0065:  leave.s    IL_0075

    }  // end .try
    finally
    {
      IL_0067:  ldloca.s   V_0
      IL_0069:  constrained. valuetype [System.Collections]System.Collections.Generic.List`1/Enumerator<class StardewValley.Menus.ChatSnippet>
      IL_006f:  callvirt   instance void [System.Runtime]System.IDisposable::Dispose()
      IL_0074:  endfinally
    }  // end handler
    IL_0075:  ret
  } // end of method ChatTextBox::updateWidth

  .method public hidebysig virtual instance void 
          Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
               [opt] bool drawShadow) cil managed
  {
    .param [2] = bool(true)
    // Code size       734 (0x2de)
    .maxstack  13
    .locals init (float32 V_0,
             valuetype [System.Runtime]System.TimeSpan V_1,
             int32 V_2)
    IL_0000:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.GameTime StardewValley.Game1::currentGameTime
    IL_0005:  callvirt   instance valuetype [System.Runtime]System.TimeSpan [MonoGame.Framework]Microsoft.Xna.Framework.GameTime::get_TotalGameTime()
    IL_000a:  stloc.1
    IL_000b:  ldloca.s   V_1
    IL_000d:  call       instance float64 [System.Runtime]System.TimeSpan::get_TotalMilliseconds()
    IL_0012:  ldc.r8     1000.
    IL_001b:  rem
    IL_001c:  ldc.r8     500.
    IL_0025:  clt.un
    IL_0027:  ldc.i4.0
    IL_0028:  ceq
    IL_002a:  ldarg.0
    IL_002b:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0030:  brfalse    IL_0117

    IL_0035:  ldarg.1
    IL_0036:  ldarg.0
    IL_0037:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_003c:  ldarg.0
    IL_003d:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0042:  ldarg.0
    IL_0043:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0048:  ldc.i4.s   16
    IL_004a:  ldarg.0
    IL_004b:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_0050:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0055:  ldc.i4.0
    IL_0056:  ldc.i4.0
    IL_0057:  ldc.i4.s   16
    IL_0059:  ldarg.0
    IL_005a:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_005f:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0064:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_0069:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_006e:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_0073:  ldarg.1
    IL_0074:  ldarg.0
    IL_0075:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_007a:  ldarg.0
    IL_007b:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0080:  ldc.i4.s   16
    IL_0082:  add
    IL_0083:  ldarg.0
    IL_0084:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0089:  ldarg.0
    IL_008a:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_008f:  ldc.i4.s   32
    IL_0091:  sub
    IL_0092:  ldarg.0
    IL_0093:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_0098:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_009d:  ldc.i4.s   16
    IL_009f:  ldc.i4.0
    IL_00a0:  ldc.i4.4
    IL_00a1:  ldarg.0
    IL_00a2:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_00a7:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_00ac:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_00b1:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_00b6:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_00bb:  ldarg.1
    IL_00bc:  ldarg.0
    IL_00bd:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_00c2:  ldarg.0
    IL_00c3:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_00c8:  ldarg.0
    IL_00c9:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_00ce:  add
    IL_00cf:  ldc.i4.s   16
    IL_00d1:  sub
    IL_00d2:  ldarg.0
    IL_00d3:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_00d8:  ldc.i4.s   16
    IL_00da:  ldarg.0
    IL_00db:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_00e0:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_00e5:  ldarg.0
    IL_00e6:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_00eb:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Bounds()
    IL_00f0:  ldfld      int32 [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::Width
    IL_00f5:  ldc.i4.s   16
    IL_00f7:  sub
    IL_00f8:  ldc.i4.0
    IL_00f9:  ldc.i4.s   16
    IL_00fb:  ldarg.0
    IL_00fc:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_0101:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0106:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_010b:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_0110:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_0115:  br.s       IL_0148

    IL_0117:  ldarg.0
    IL_0118:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_011d:  ldc.i4.s   32
    IL_011f:  sub
    IL_0120:  ldarg.0
    IL_0121:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0126:  ldc.i4.s   112
    IL_0128:  sub
    IL_0129:  ldc.i4.s   10
    IL_012b:  add
    IL_012c:  ldarg.0
    IL_012d:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0132:  ldc.i4.s   80
    IL_0134:  add
    IL_0135:  ldarg.0
    IL_0136:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_013b:  ldc.i4.0
    IL_013c:  ldc.i4.1
    IL_013d:  ldnull
    IL_013e:  ldc.i4.0
    IL_013f:  ldc.i4.1
    IL_0140:  ldc.i4.m1
    IL_0141:  ldc.i4.m1
    IL_0142:  ldc.i4.m1
    IL_0143:  call       void StardewValley.Game1::drawDialogueBox(int32,
                                                                   int32,
                                                                   int32,
                                                                   int32,
                                                                   bool,
                                                                   bool,
                                                                   string,
                                                                   bool,
                                                                   bool,
                                                                   int32,
                                                                   int32,
                                                                   int32)
    IL_0148:  brfalse.s  IL_0186

    IL_014a:  ldarg.0
    IL_014b:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_0150:  brfalse.s  IL_0186

    IL_0152:  ldarg.1
    IL_0153:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::staminaRect
    IL_0158:  ldarg.0
    IL_0159:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_015e:  ldc.i4.s   16
    IL_0160:  add
    IL_0161:  ldarg.0
    IL_0162:  ldfld      float32 StardewValley.Menus.ChatTextBox::currentWidth
    IL_0167:  conv.i4
    IL_0168:  add
    IL_0169:  ldc.i4.2
    IL_016a:  sub
    IL_016b:  ldarg.0
    IL_016c:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0171:  ldc.i4.8
    IL_0172:  add
    IL_0173:  ldc.i4.4
    IL_0174:  ldc.i4.s   32
    IL_0176:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_017b:  ldarg.0
    IL_017c:  ldfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_0181:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_0186:  ldc.r4     0.0
    IL_018b:  stloc.0
    IL_018c:  ldc.i4.0
    IL_018d:  stloc.2
    IL_018e:  br         IL_02cc

    IL_0193:  ldarg.0
    IL_0194:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0199:  ldloc.2
    IL_019a:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_019f:  ldfld      int32 StardewValley.Menus.ChatSnippet::emojiIndex
    IL_01a4:  ldc.i4.m1
    IL_01a5:  beq        IL_023e

    IL_01aa:  ldarg.1
    IL_01ab:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.ChatBox::emojiTexture
    IL_01b0:  ldarg.0
    IL_01b1:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_01b6:  conv.r4
    IL_01b7:  ldloc.0
    IL_01b8:  add
    IL_01b9:  ldc.r4     12.
    IL_01be:  add
    IL_01bf:  ldarg.0
    IL_01c0:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_01c5:  ldc.i4.s   12
    IL_01c7:  add
    IL_01c8:  conv.r4
    IL_01c9:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::.ctor(float32,
                                                                                                  float32)
    IL_01ce:  ldarg.0
    IL_01cf:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_01d4:  ldloc.2
    IL_01d5:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_01da:  ldfld      int32 StardewValley.Menus.ChatSnippet::emojiIndex
    IL_01df:  ldc.i4.s   9
    IL_01e1:  mul
    IL_01e2:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.ChatBox::emojiTexture
    IL_01e7:  callvirt   instance int32 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Width()
    IL_01ec:  rem
    IL_01ed:  ldarg.0
    IL_01ee:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_01f3:  ldloc.2
    IL_01f4:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_01f9:  ldfld      int32 StardewValley.Menus.ChatSnippet::emojiIndex
    IL_01fe:  ldc.i4.s   9
    IL_0200:  mul
    IL_0201:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.ChatBox::emojiTexture
    IL_0206:  callvirt   instance int32 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Width()
    IL_020b:  div
    IL_020c:  ldc.i4.s   9
    IL_020e:  mul
    IL_020f:  ldc.i4.s   9
    IL_0211:  ldc.i4.s   9
    IL_0213:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0218:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_021d:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_0222:  ldc.r4     0.0
    IL_0227:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::get_Zero()
    IL_022c:  ldc.r4     4.
    IL_0231:  ldc.i4.0
    IL_0232:  ldc.r4     0.99000001
    IL_0237:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color,
                                                                                                              float32,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                              float32,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteEffects,
                                                                                                              float32)
    IL_023c:  br.s       IL_02b4

    IL_023e:  ldarg.0
    IL_023f:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0244:  ldloc.2
    IL_0245:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_024a:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_024f:  brfalse.s  IL_02b4

    IL_0251:  ldarg.1
    IL_0252:  call       valuetype StardewValley.LocalizedContentManager/LanguageCode StardewValley.LocalizedContentManager::get_CurrentLanguageCode()
    IL_0257:  call       class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.ChatBox::messageFont(valuetype StardewValley.LocalizedContentManager/LanguageCode)
    IL_025c:  ldarg.0
    IL_025d:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_0262:  ldloc.2
    IL_0263:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_0268:  ldfld      string StardewValley.Menus.ChatSnippet::message
    IL_026d:  ldarg.0
    IL_026e:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0273:  conv.r4
    IL_0274:  ldloc.0
    IL_0275:  add
    IL_0276:  ldc.r4     12.
    IL_027b:  add
    IL_027c:  ldarg.0
    IL_027d:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0282:  ldc.i4.s   12
    IL_0284:  add
    IL_0285:  conv.r4
    IL_0286:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::.ctor(float32,
                                                                                                  float32)
    IL_028b:  call       class StardewValley.Farmer StardewValley.Game1::get_player()
    IL_0290:  ldfld      string StardewValley.Farmer::defaultChatColor
    IL_0295:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.ChatMessage::getColorFromName(string)
    IL_029a:  ldc.r4     0.0
    IL_029f:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::get_Zero()
    IL_02a4:  ldc.r4     1.
    IL_02a9:  ldc.i4.0
    IL_02aa:  ldc.r4     0.99000001
    IL_02af:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::DrawString(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont,
                                                                                                                    string,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color,
                                                                                                                    float32,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                                    float32,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteEffects,
                                                                                                                    float32)
    IL_02b4:  ldloc.0
    IL_02b5:  ldarg.0
    IL_02b6:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_02bb:  ldloc.2
    IL_02bc:  callvirt   instance !0 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Item(int32)
    IL_02c1:  ldfld      float32 StardewValley.Menus.ChatSnippet::myLength
    IL_02c6:  add
    IL_02c7:  stloc.0
    IL_02c8:  ldloc.2
    IL_02c9:  ldc.i4.1
    IL_02ca:  add
    IL_02cb:  stloc.2
    IL_02cc:  ldloc.2
    IL_02cd:  ldarg.0
    IL_02ce:  ldfld      class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet> StardewValley.Menus.ChatTextBox::finalText
    IL_02d3:  callvirt   instance int32 class [System.Collections]System.Collections.Generic.List`1<class StardewValley.Menus.ChatSnippet>::get_Count()
    IL_02d8:  blt        IL_0193

    IL_02dd:  ret
  } // end of method ChatTextBox::Draw

} // end of class StardewValley.Menus.ChatTextBox

// *********** DISASSEMBLY COMPLETE ***********************
