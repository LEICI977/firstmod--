
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.0.30319.33440
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit StardewValley.Menus.TextBox
       extends [System.Runtime]System.Object
       implements StardewValley.IKeyboardSubscriber
{
  .field family class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D _textBoxTexture
  .field family class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D _caretTexture
  .field family class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont _font
  .field family valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color _textColor
  .field private int32 '<X>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private int32 '<Y>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private int32 '<Width>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private int32 '<Height>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private bool '<PasswordBox>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public bool numbersOnly
  .field public int32 textLimit
  .field public bool limitWidth
  .field private string _text
  .field private string '<TitleText>k__BackingField'
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private class StardewValley.Menus.TextBoxEvent OnEnterPressed
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private class StardewValley.Menus.TextBoxEvent OnTabPressed
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private class StardewValley.Menus.TextBoxEvent OnBackspacePressed
  .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private bool _showKeyboard
  .field private bool _selected
  .method public hidebysig specialname instance class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont 
          get_Font() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_0006:  ret
  } // end of method TextBox::get_Font

  .method public hidebysig specialname instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color 
          get_TextColor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_0006:  ret
  } // end of method TextBox::get_TextColor

  .method public hidebysig specialname instance int32 
          get_X() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      int32 StardewValley.Menus.TextBox::'<X>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_X

  .method public hidebysig specialname instance void 
          set_X(int32 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      int32 StardewValley.Menus.TextBox::'<X>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_X

  .method public hidebysig specialname instance int32 
          get_Y() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      int32 StardewValley.Menus.TextBox::'<Y>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_Y

  .method public hidebysig specialname instance void 
          set_Y(int32 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      int32 StardewValley.Menus.TextBox::'<Y>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_Y

  .method public hidebysig specialname instance int32 
          get_Width() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      int32 StardewValley.Menus.TextBox::'<Width>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_Width

  .method public hidebysig specialname instance void 
          set_Width(int32 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      int32 StardewValley.Menus.TextBox::'<Width>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_Width

  .method public hidebysig specialname instance int32 
          get_Height() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      int32 StardewValley.Menus.TextBox::'<Height>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_Height

  .method public hidebysig specialname instance void 
          set_Height(int32 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      int32 StardewValley.Menus.TextBox::'<Height>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_Height

  .method public hidebysig specialname instance bool 
          get_PasswordBox() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      bool StardewValley.Menus.TextBox::'<PasswordBox>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_PasswordBox

  .method public hidebysig specialname instance void 
          set_PasswordBox(bool 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      bool StardewValley.Menus.TextBox::'<PasswordBox>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_PasswordBox

  .method public hidebysig specialname instance string 
          get_Text() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_0006:  ret
  } // end of method TextBox::get_Text

  .method public hidebysig specialname instance void 
          set_Text(string 'value') cil managed
  {
    // Code size       135 (0x87)
    .maxstack  5
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      string StardewValley.Menus.TextBox::_text
    IL_0007:  ldarg.0
    IL_0008:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_000d:  brtrue.s   IL_001a

    IL_000f:  ldarg.0
    IL_0010:  ldstr      ""
    IL_0015:  stfld      string StardewValley.Menus.TextBox::_text
    IL_001a:  ldarg.0
    IL_001b:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_0020:  ldstr      ""
    IL_0025:  call       bool [System.Runtime]System.String::op_Inequality(string,
                                                                           string)
    IL_002a:  brfalse.s  IL_0086

    IL_002c:  ldarg.0
    IL_002d:  ldarg.0
    IL_002e:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_0033:  call       string StardewValley.Utility::FilterDirtyWordsIfStrictPlatform(string)
    IL_0038:  stfld      string StardewValley.Menus.TextBox::_text
    IL_003d:  ldarg.0
    IL_003e:  ldfld      bool StardewValley.Menus.TextBox::limitWidth
    IL_0043:  brfalse.s  IL_0086

    IL_0045:  ldarg.0
    IL_0046:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_004b:  ldarg.0
    IL_004c:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_0051:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont::MeasureString(string)
    IL_0056:  ldfld      float32 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::X
    IL_005b:  ldarg.0
    IL_005c:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0061:  ldc.i4.s   21
    IL_0063:  sub
    IL_0064:  conv.r4
    IL_0065:  ble.un.s   IL_0086

    IL_0067:  ldarg.0
    IL_0068:  ldarg.0
    IL_0069:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_006e:  ldc.i4.0
    IL_006f:  ldarg.0
    IL_0070:  ldfld      string StardewValley.Menus.TextBox::_text
    IL_0075:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_007a:  ldc.i4.1
    IL_007b:  sub
    IL_007c:  callvirt   instance string [System.Runtime]System.String::Substring(int32,
                                                                                  int32)
    IL_0081:  call       instance void StardewValley.Menus.TextBox::set_Text(string)
    IL_0086:  ret
  } // end of method TextBox::set_Text

  .method public hidebysig specialname instance string 
          get_TitleText() cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      string StardewValley.Menus.TextBox::'<TitleText>k__BackingField'
    IL_0006:  ret
  } // end of method TextBox::get_TitleText

  .method public hidebysig specialname instance void 
          set_TitleText(string 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  stfld      string StardewValley.Menus.TextBox::'<TitleText>k__BackingField'
    IL_0007:  ret
  } // end of method TextBox::set_TitleText

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D textBoxTexture,
                               class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D caretTexture,
                               class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont font,
                               valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color textColor) cil managed
  {
    // Code size       88 (0x58)
    .maxstack  2
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.m1
    IL_0002:  stfld      int32 StardewValley.Menus.TextBox::textLimit
    IL_0007:  ldarg.0
    IL_0008:  ldc.i4.1
    IL_0009:  stfld      bool StardewValley.Menus.TextBox::limitWidth
    IL_000e:  ldarg.0
    IL_000f:  ldstr      ""
    IL_0014:  stfld      string StardewValley.Menus.TextBox::_text
    IL_0019:  ldarg.0
    IL_001a:  call       instance void [System.Runtime]System.Object::.ctor()
    IL_001f:  ldarg.0
    IL_0020:  ldarg.1
    IL_0021:  stfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0026:  ldarg.1
    IL_0027:  brfalse.s  IL_0041

    IL_0029:  ldarg.0
    IL_002a:  ldarg.1
    IL_002b:  callvirt   instance int32 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Width()
    IL_0030:  call       instance void StardewValley.Menus.TextBox::set_Width(int32)
    IL_0035:  ldarg.0
    IL_0036:  ldarg.1
    IL_0037:  callvirt   instance int32 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Height()
    IL_003c:  call       instance void StardewValley.Menus.TextBox::set_Height(int32)
    IL_0041:  ldarg.0
    IL_0042:  ldarg.2
    IL_0043:  stfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_caretTexture
    IL_0048:  ldarg.0
    IL_0049:  ldarg.3
    IL_004a:  stfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_004f:  ldarg.0
    IL_0050:  ldarg.s    textColor
    IL_0052:  stfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_0057:  ret
  } // end of method TextBox::.ctor

  .method public hidebysig instance void 
          SelectMe() cil managed
  {
    // Code size       8 (0x8)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.1
    IL_0002:  call       instance void StardewValley.Menus.TextBox::set_Selected(bool)
    IL_0007:  ret
  } // end of method TextBox::SelectMe

  .method public hidebysig instance void 
          Update() cil managed
  {
    // Code size       115 (0x73)
    .maxstack  5
    .locals init (valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Point V_0,
             valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle V_1)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       int32 StardewValley.Game1::getMouseX()
    IL_0007:  call       int32 StardewValley.Game1::getMouseY()
    IL_000c:  call       instance void [MonoGame.Framework]Microsoft.Xna.Framework.Point::.ctor(int32,
                                                                                                int32)
    IL_0011:  ldloca.s   V_1
    IL_0013:  ldarg.0
    IL_0014:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0019:  ldarg.0
    IL_001a:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_001f:  ldarg.0
    IL_0020:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0025:  ldarg.0
    IL_0026:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_002b:  call       instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0030:  ldloca.s   V_1
    IL_0032:  ldloc.0
    IL_0033:  call       instance bool [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::Contains(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Point)
    IL_0038:  brfalse.s  IL_0043

    IL_003a:  ldarg.0
    IL_003b:  ldc.i4.1
    IL_003c:  call       instance void StardewValley.Menus.TextBox::set_Selected(bool)
    IL_0041:  br.s       IL_004a

    IL_0043:  ldarg.0
    IL_0044:  ldc.i4.0
    IL_0045:  call       instance void StardewValley.Menus.TextBox::set_Selected(bool)
    IL_004a:  ldarg.0
    IL_004b:  ldfld      bool StardewValley.Menus.TextBox::_showKeyboard
    IL_0050:  brfalse.s  IL_0072

    IL_0052:  call       class StardewValley.Options StardewValley.Game1::get_options()
    IL_0057:  ldfld      bool StardewValley.Options::gamepadControls
    IL_005c:  brfalse.s  IL_006b

    IL_005e:  ldsfld     bool StardewValley.Game1::lastCursorMotionWasMouse
    IL_0063:  brtrue.s   IL_006b

    IL_0065:  ldarg.0
    IL_0066:  call       void StardewValley.Game1::showTextEntry(class StardewValley.Menus.TextBox)
    IL_006b:  ldarg.0
    IL_006c:  ldc.i4.0
    IL_006d:  stfld      bool StardewValley.Menus.TextBox::_showKeyboard
    IL_0072:  ret
  } // end of method TextBox::Update

  .method public hidebysig newslot virtual 
          instance void  Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
                              [opt] bool drawShadow) cil managed
  {
    .param [2] = bool(true)
    // Code size       656 (0x290)
    .maxstack  12
    .locals init (bool V_0,
             string V_1,
             valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 V_2,
             valuetype [System.Runtime]System.TimeSpan V_3,
             int32 V_4)
    IL_0000:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.GameTime StardewValley.Game1::currentGameTime
    IL_0005:  callvirt   instance valuetype [System.Runtime]System.TimeSpan [MonoGame.Framework]Microsoft.Xna.Framework.GameTime::get_TotalGameTime()
    IL_000a:  stloc.3
    IL_000b:  ldloca.s   V_3
    IL_000d:  call       instance float64 [System.Runtime]System.TimeSpan::get_TotalMilliseconds()
    IL_0012:  ldc.r8     1000.
    IL_001b:  rem
    IL_001c:  ldc.r8     500.
    IL_0025:  clt.un
    IL_0027:  ldc.i4.0
    IL_0028:  ceq
    IL_002a:  stloc.0
    IL_002b:  ldarg.0
    IL_002c:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_0031:  stloc.1
    IL_0032:  ldarg.0
    IL_0033:  call       instance bool StardewValley.Menus.TextBox::get_PasswordBox()
    IL_0038:  brfalse.s  IL_0066

    IL_003a:  ldstr      ""
    IL_003f:  stloc.1
    IL_0040:  ldc.i4.0
    IL_0041:  stloc.s    V_4
    IL_0043:  br.s       IL_0057

    IL_0045:  ldloc.1
    IL_0046:  ldstr      bytearray (22 20 )                                           // " 
    IL_004b:  call       string [System.Runtime]System.String::Concat(string,
                                                                      string)
    IL_0050:  stloc.1
    IL_0051:  ldloc.s    V_4
    IL_0053:  ldc.i4.1
    IL_0054:  add
    IL_0055:  stloc.s    V_4
    IL_0057:  ldloc.s    V_4
    IL_0059:  ldarg.0
    IL_005a:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_005f:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0064:  blt.s      IL_0045

    IL_0066:  ldarg.0
    IL_0067:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_006c:  brfalse    IL_0153

    IL_0071:  ldarg.1
    IL_0072:  ldarg.0
    IL_0073:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0078:  ldarg.0
    IL_0079:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_007e:  ldarg.0
    IL_007f:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0084:  ldc.i4.s   16
    IL_0086:  ldarg.0
    IL_0087:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_008c:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0091:  ldc.i4.0
    IL_0092:  ldc.i4.0
    IL_0093:  ldc.i4.s   16
    IL_0095:  ldarg.0
    IL_0096:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_009b:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_00a0:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_00a5:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_00aa:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_00af:  ldarg.1
    IL_00b0:  ldarg.0
    IL_00b1:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_00b6:  ldarg.0
    IL_00b7:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_00bc:  ldc.i4.s   16
    IL_00be:  add
    IL_00bf:  ldarg.0
    IL_00c0:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_00c5:  ldarg.0
    IL_00c6:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_00cb:  ldc.i4.s   32
    IL_00cd:  sub
    IL_00ce:  ldarg.0
    IL_00cf:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_00d4:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_00d9:  ldc.i4.s   16
    IL_00db:  ldc.i4.0
    IL_00dc:  ldc.i4.4
    IL_00dd:  ldarg.0
    IL_00de:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_00e3:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_00e8:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_00ed:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_00f2:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_00f7:  ldarg.1
    IL_00f8:  ldarg.0
    IL_00f9:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_00fe:  ldarg.0
    IL_00ff:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0104:  ldarg.0
    IL_0105:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_010a:  add
    IL_010b:  ldc.i4.s   16
    IL_010d:  sub
    IL_010e:  ldarg.0
    IL_010f:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0114:  ldc.i4.s   16
    IL_0116:  ldarg.0
    IL_0117:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_011c:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0121:  ldarg.0
    IL_0122:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0127:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D::get_Bounds()
    IL_012c:  ldfld      int32 [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::Width
    IL_0131:  ldc.i4.s   16
    IL_0133:  sub
    IL_0134:  ldc.i4.0
    IL_0135:  ldc.i4.s   16
    IL_0137:  ldarg.0
    IL_0138:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_013d:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0142:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>::.ctor(!0)
    IL_0147:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color [MonoGame.Framework]Microsoft.Xna.Framework.Color::get_White()
    IL_014c:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [System.Runtime]System.Nullable`1<valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle>,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_0151:  br.s       IL_0184

    IL_0153:  ldarg.0
    IL_0154:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0159:  ldc.i4.s   32
    IL_015b:  sub
    IL_015c:  ldarg.0
    IL_015d:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0162:  ldc.i4.s   112
    IL_0164:  sub
    IL_0165:  ldc.i4.s   10
    IL_0167:  add
    IL_0168:  ldarg.0
    IL_0169:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_016e:  ldc.i4.s   80
    IL_0170:  add
    IL_0171:  ldarg.0
    IL_0172:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_0177:  ldc.i4.0
    IL_0178:  ldc.i4.1
    IL_0179:  ldnull
    IL_017a:  ldc.i4.0
    IL_017b:  ldc.i4.1
    IL_017c:  ldc.i4.m1
    IL_017d:  ldc.i4.m1
    IL_017e:  ldc.i4.m1
    IL_017f:  call       void StardewValley.Game1::drawDialogueBox(int32,
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
    IL_0184:  ldarg.0
    IL_0185:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_018a:  ldloc.1
    IL_018b:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont::MeasureString(string)
    IL_0190:  stloc.2
    IL_0191:  br.s       IL_01a8

    IL_0193:  ldloc.1
    IL_0194:  ldc.i4.1
    IL_0195:  callvirt   instance string [System.Runtime]System.String::Substring(int32)
    IL_019a:  stloc.1
    IL_019b:  ldarg.0
    IL_019c:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_01a1:  ldloc.1
    IL_01a2:  callvirt   instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont::MeasureString(string)
    IL_01a7:  stloc.2
    IL_01a8:  ldloc.2
    IL_01a9:  ldfld      float32 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::X
    IL_01ae:  ldarg.0
    IL_01af:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_01b4:  conv.r4
    IL_01b5:  bgt.s      IL_0193

    IL_01b7:  ldloc.0
    IL_01b8:  brfalse.s  IL_01f6

    IL_01ba:  ldarg.0
    IL_01bb:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_01c0:  brfalse.s  IL_01f6

    IL_01c2:  ldarg.1
    IL_01c3:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::staminaRect
    IL_01c8:  ldarg.0
    IL_01c9:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_01ce:  ldc.i4.s   16
    IL_01d0:  add
    IL_01d1:  ldloc.2
    IL_01d2:  ldfld      float32 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::X
    IL_01d7:  conv.i4
    IL_01d8:  add
    IL_01d9:  ldc.i4.2
    IL_01da:  add
    IL_01db:  ldarg.0
    IL_01dc:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_01e1:  ldc.i4.8
    IL_01e2:  add
    IL_01e3:  ldc.i4.4
    IL_01e4:  ldc.i4.s   32
    IL_01e6:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_01eb:  ldarg.0
    IL_01ec:  ldfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_01f1:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::Draw(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                                              valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_01f6:  ldarg.2
    IL_01f7:  brfalse.s  IL_0243

    IL_01f9:  ldarg.1
    IL_01fa:  ldloc.1
    IL_01fb:  ldarg.0
    IL_01fc:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_0201:  ldarg.0
    IL_0202:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0207:  ldc.i4.s   16
    IL_0209:  add
    IL_020a:  conv.r4
    IL_020b:  ldarg.0
    IL_020c:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0211:  ldarg.0
    IL_0212:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0217:  brtrue.s   IL_021c

    IL_0219:  ldc.i4.8
    IL_021a:  br.s       IL_021e

    IL_021c:  ldc.i4.s   12
    IL_021e:  add
    IL_021f:  conv.r4
    IL_0220:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::.ctor(float32,
                                                                                                  float32)
    IL_0225:  ldarg.0
    IL_0226:  ldfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_022b:  ldc.r4     1.
    IL_0230:  ldc.r4     -1.
    IL_0235:  ldc.i4.m1
    IL_0236:  ldc.i4.m1
    IL_0237:  ldc.r4     1.
    IL_023c:  ldc.i4.3
    IL_023d:  call       void StardewValley.Utility::drawTextWithShadow(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch,
                                                                        string,
                                                                        class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont,
                                                                        valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                        valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color,
                                                                        float32,
                                                                        float32,
                                                                        int32,
                                                                        int32,
                                                                        float32,
                                                                        int32)
    IL_0242:  ret

    IL_0243:  ldarg.1
    IL_0244:  ldarg.0
    IL_0245:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::_font
    IL_024a:  ldloc.1
    IL_024b:  ldarg.0
    IL_024c:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0251:  ldc.i4.s   16
    IL_0253:  add
    IL_0254:  conv.r4
    IL_0255:  ldarg.0
    IL_0256:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_025b:  ldarg.0
    IL_025c:  ldfld      class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Menus.TextBox::_textBoxTexture
    IL_0261:  brtrue.s   IL_0266

    IL_0263:  ldc.i4.8
    IL_0264:  br.s       IL_0268

    IL_0266:  ldc.i4.s   12
    IL_0268:  add
    IL_0269:  conv.r4
    IL_026a:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::.ctor(float32,
                                                                                                  float32)
    IL_026f:  ldarg.0
    IL_0270:  ldfld      valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::_textColor
    IL_0275:  ldc.r4     0.0
    IL_027a:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2 [MonoGame.Framework]Microsoft.Xna.Framework.Vector2::get_Zero()
    IL_027f:  ldc.r4     1.
    IL_0284:  ldc.i4.0
    IL_0285:  ldc.r4     0.99000001
    IL_028a:  callvirt   instance void [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteBatch::DrawString(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont,
                                                                                                                    string,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color,
                                                                                                                    float32,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Vector2,
                                                                                                                    float32,
                                                                                                                    valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteEffects,
                                                                                                                    float32)
    IL_028f:  ret
  } // end of method TextBox::Draw

  .method public hidebysig newslot virtual 
          instance void  RecieveTextInput(char inputChar) cil managed
  {
    // Code size       267 (0x10b)
    .maxstack  3
    .locals init (valuetype [System.Runtime]System.Nullable`1<int32> V_0)
    IL_0000:  ldarg.0
    IL_0001:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_0006:  brfalse    IL_010a

    IL_000b:  ldarg.0
    IL_000c:  ldfld      bool StardewValley.Menus.TextBox::numbersOnly
    IL_0011:  brfalse.s  IL_001e

    IL_0013:  ldarg.1
    IL_0014:  call       bool [System.Runtime]System.Char::IsDigit(char)
    IL_0019:  brfalse    IL_010a

    IL_001e:  ldarg.0
    IL_001f:  ldfld      int32 StardewValley.Menus.TextBox::textLimit
    IL_0024:  ldc.i4.m1
    IL_0025:  beq.s      IL_003d

    IL_0027:  ldarg.0
    IL_0028:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_002d:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0032:  ldarg.0
    IL_0033:  ldfld      int32 StardewValley.Menus.TextBox::textLimit
    IL_0038:  bge        IL_010a

    IL_003d:  call       uint8 StardewValley.Game1::get_gameMode()
    IL_0042:  ldc.i4.3
    IL_0043:  beq        IL_00f2

    IL_0048:  ldarg.1
    IL_0049:  ldc.i4.s   42
    IL_004b:  bgt.un.s   IL_0061

    IL_004d:  ldarg.1
    IL_004e:  ldc.i4.s   34
    IL_0050:  beq        IL_00dd

    IL_0055:  ldarg.1
    IL_0056:  ldc.i4.s   36
    IL_0058:  beq.s      IL_00c7

    IL_005a:  ldarg.1
    IL_005b:  ldc.i4.s   42
    IL_005d:  beq.s      IL_0088

    IL_005f:  br.s       IL_00de

    IL_0061:  ldarg.1
    IL_0062:  ldc.i4.s   43
    IL_0064:  beq.s      IL_0072

    IL_0066:  ldarg.1
    IL_0067:  ldc.i4.s   60
    IL_0069:  beq.s      IL_00b4

    IL_006b:  ldarg.1
    IL_006c:  ldc.i4.s   61
    IL_006e:  beq.s      IL_009e

    IL_0070:  br.s       IL_00de

    IL_0072:  ldstr      "slimeHit"
    IL_0077:  ldloca.s   V_0
    IL_0079:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_007f:  ldloc.0
    IL_0080:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_0085:  pop
    IL_0086:  br.s       IL_00f2

    IL_0088:  ldstr      "hammer"
    IL_008d:  ldloca.s   V_0
    IL_008f:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_0095:  ldloc.0
    IL_0096:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_009b:  pop
    IL_009c:  br.s       IL_00f2

    IL_009e:  ldstr      "coin"
    IL_00a3:  ldloca.s   V_0
    IL_00a5:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_00ab:  ldloc.0
    IL_00ac:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_00b1:  pop
    IL_00b2:  br.s       IL_00f2

    IL_00b4:  ldstr      "crystal"
    IL_00b9:  ldc.i4.0
    IL_00ba:  newobj     instance void valuetype [System.Runtime]System.Nullable`1<int32>::.ctor(!0)
    IL_00bf:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_00c4:  pop
    IL_00c5:  br.s       IL_00f2

    IL_00c7:  ldstr      "money"
    IL_00cc:  ldloca.s   V_0
    IL_00ce:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_00d4:  ldloc.0
    IL_00d5:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_00da:  pop
    IL_00db:  br.s       IL_00f2

    IL_00dd:  ret

    IL_00de:  ldstr      "cowboy_monsterhit"
    IL_00e3:  ldloca.s   V_0
    IL_00e5:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_00eb:  ldloc.0
    IL_00ec:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_00f1:  pop
    IL_00f2:  ldarg.0
    IL_00f3:  ldarg.0
    IL_00f4:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_00f9:  ldarga.s   inputChar
    IL_00fb:  call       instance string [System.Runtime]System.Char::ToString()
    IL_0100:  call       string [System.Runtime]System.String::Concat(string,
                                                                      string)
    IL_0105:  call       instance void StardewValley.Menus.TextBox::set_Text(string)
    IL_010a:  ret
  } // end of method TextBox::RecieveTextInput

  .method public hidebysig newslot virtual 
          instance void  RecieveTextInput(string text) cil managed
  {
    // Code size       75 (0x4b)
    .maxstack  3
    .locals init (int32 V_0)
    IL_0000:  ldc.i4.m1
    IL_0001:  stloc.0
    IL_0002:  ldarg.0
    IL_0003:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_0008:  brfalse.s  IL_004a

    IL_000a:  ldarg.0
    IL_000b:  ldfld      bool StardewValley.Menus.TextBox::numbersOnly
    IL_0010:  brfalse.s  IL_001c

    IL_0012:  ldarg.1
    IL_0013:  ldloca.s   V_0
    IL_0015:  call       bool [System.Runtime]System.Int32::TryParse(string,
                                                                     int32&)
    IL_001a:  brfalse.s  IL_004a

    IL_001c:  ldarg.0
    IL_001d:  ldfld      int32 StardewValley.Menus.TextBox::textLimit
    IL_0022:  ldc.i4.m1
    IL_0023:  beq.s      IL_0038

    IL_0025:  ldarg.0
    IL_0026:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_002b:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0030:  ldarg.0
    IL_0031:  ldfld      int32 StardewValley.Menus.TextBox::textLimit
    IL_0036:  bge.s      IL_004a

    IL_0038:  ldarg.0
    IL_0039:  ldarg.0
    IL_003a:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_003f:  ldarg.1
    IL_0040:  call       string [System.Runtime]System.String::Concat(string,
                                                                      string)
    IL_0045:  call       instance void StardewValley.Menus.TextBox::set_Text(string)
    IL_004a:  ret
  } // end of method TextBox::RecieveTextInput

  .method public hidebysig newslot virtual 
          instance void  RecieveCommandInput(char command) cil managed
  {
    // Code size       157 (0x9d)
    .maxstack  5
    .locals init (valuetype [System.Runtime]System.Nullable`1<int32> V_0)
    IL_0000:  ldarg.0
    IL_0001:  call       instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_0006:  brfalse    IL_009c

    IL_000b:  ldarg.1
    IL_000c:  ldc.i4.8
    IL_000d:  beq.s      IL_001a

    IL_000f:  ldarg.1
    IL_0010:  ldc.i4.s   9
    IL_0012:  beq.s      IL_008b

    IL_0014:  ldarg.1
    IL_0015:  ldc.i4.s   13
    IL_0017:  beq.s      IL_0079

    IL_0019:  ret

    IL_001a:  ldarg.0
    IL_001b:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_0020:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0025:  ldc.i4.0
    IL_0026:  ble.s      IL_009c

    IL_0028:  ldarg.0
    IL_0029:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_002e:  brfalse.s  IL_003d

    IL_0030:  ldarg.0
    IL_0031:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_0036:  ldarg.0
    IL_0037:  callvirt   instance void StardewValley.Menus.TextBoxEvent::Invoke(class StardewValley.Menus.TextBox)
    IL_003c:  ret

    IL_003d:  ldarg.0
    IL_003e:  ldarg.0
    IL_003f:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_0044:  ldc.i4.0
    IL_0045:  ldarg.0
    IL_0046:  call       instance string StardewValley.Menus.TextBox::get_Text()
    IL_004b:  callvirt   instance int32 [System.Runtime]System.String::get_Length()
    IL_0050:  ldc.i4.1
    IL_0051:  sub
    IL_0052:  callvirt   instance string [System.Runtime]System.String::Substring(int32,
                                                                                  int32)
    IL_0057:  call       instance void StardewValley.Menus.TextBox::set_Text(string)
    IL_005c:  call       uint8 StardewValley.Game1::get_gameMode()
    IL_0061:  ldc.i4.3
    IL_0062:  beq.s      IL_009c

    IL_0064:  ldstr      "tinyWhip"
    IL_0069:  ldloca.s   V_0
    IL_006b:  initobj    valuetype [System.Runtime]System.Nullable`1<int32>
    IL_0071:  ldloc.0
    IL_0072:  call       bool StardewValley.Game1::playSound(string,
                                                             valuetype [System.Runtime]System.Nullable`1<int32>)
    IL_0077:  pop
    IL_0078:  ret

    IL_0079:  ldarg.0
    IL_007a:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnEnterPressed
    IL_007f:  dup
    IL_0080:  brtrue.s   IL_0084

    IL_0082:  pop
    IL_0083:  ret

    IL_0084:  ldarg.0
    IL_0085:  callvirt   instance void StardewValley.Menus.TextBoxEvent::Invoke(class StardewValley.Menus.TextBox)
    IL_008a:  ret

    IL_008b:  ldarg.0
    IL_008c:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnTabPressed
    IL_0091:  dup
    IL_0092:  brtrue.s   IL_0096

    IL_0094:  pop
    IL_0095:  ret

    IL_0096:  ldarg.0
    IL_0097:  callvirt   instance void StardewValley.Menus.TextBoxEvent::Invoke(class StardewValley.Menus.TextBox)
    IL_009c:  ret
  } // end of method TextBox::RecieveCommandInput

  .method public hidebysig newslot virtual final 
          instance void  RecieveSpecialInput(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Input.Keys key) cil managed
  {
    // Code size       1 (0x1)
    .maxstack  8
    IL_0000:  ret
  } // end of method TextBox::RecieveSpecialInput

  .method public hidebysig instance void 
          Hover(int32 x,
                int32 y) cil managed
  {
    // Code size       56 (0x38)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  ldarg.0
    IL_0002:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0007:  ble.s      IL_0037

    IL_0009:  ldarg.1
    IL_000a:  ldarg.0
    IL_000b:  call       instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0010:  ldarg.0
    IL_0011:  call       instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_0016:  add
    IL_0017:  bge.s      IL_0037

    IL_0019:  ldarg.2
    IL_001a:  ldarg.0
    IL_001b:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0020:  ble.s      IL_0037

    IL_0022:  ldarg.2
    IL_0023:  ldarg.0
    IL_0024:  call       instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_0029:  ldarg.0
    IL_002a:  call       instance int32 StardewValley.Menus.TextBox::get_Height()
    IL_002f:  add
    IL_0030:  bge.s      IL_0037

    IL_0032:  call       void StardewValley.Game1::SetFreeCursorDrag()
    IL_0037:  ret
  } // end of method TextBox::Hover

  .method public hidebysig specialname instance void 
          add_OnEnterPressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnEnterPressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Combine(class [System.Runtime]System.Delegate,
                                                                                                        class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnEnterPressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::add_OnEnterPressed

  .method public hidebysig specialname instance void 
          remove_OnEnterPressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnEnterPressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Remove(class [System.Runtime]System.Delegate,
                                                                                                       class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnEnterPressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::remove_OnEnterPressed

  .method public hidebysig specialname instance void 
          add_OnTabPressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnTabPressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Combine(class [System.Runtime]System.Delegate,
                                                                                                        class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnTabPressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::add_OnTabPressed

  .method public hidebysig specialname instance void 
          remove_OnTabPressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnTabPressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Remove(class [System.Runtime]System.Delegate,
                                                                                                       class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnTabPressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::remove_OnTabPressed

  .method public hidebysig specialname instance void 
          add_OnBackspacePressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Combine(class [System.Runtime]System.Delegate,
                                                                                                        class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::add_OnBackspacePressed

  .method public hidebysig specialname instance void 
          remove_OnBackspacePressed(class StardewValley.Menus.TextBoxEvent 'value') cil managed
  {
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       41 (0x29)
    .maxstack  3
    .locals init (class StardewValley.Menus.TextBoxEvent V_0,
             class StardewValley.Menus.TextBoxEvent V_1,
             class StardewValley.Menus.TextBoxEvent V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  stloc.1
    IL_0009:  ldloc.1
    IL_000a:  ldarg.1
    IL_000b:  call       class [System.Runtime]System.Delegate [System.Runtime]System.Delegate::Remove(class [System.Runtime]System.Delegate,
                                                                                                       class [System.Runtime]System.Delegate)
    IL_0010:  castclass  StardewValley.Menus.TextBoxEvent
    IL_0015:  stloc.2
    IL_0016:  ldarg.0
    IL_0017:  ldflda     class StardewValley.Menus.TextBoxEvent StardewValley.Menus.TextBox::OnBackspacePressed
    IL_001c:  ldloc.2
    IL_001d:  ldloc.1
    IL_001e:  call       !!0 [System.Threading]System.Threading.Interlocked::CompareExchange<class StardewValley.Menus.TextBoxEvent>(!!0&,
                                                                                                                                     !!0,
                                                                                                                                     !!0)
    IL_0023:  stloc.0
    IL_0024:  ldloc.0
    IL_0025:  ldloc.1
    IL_0026:  bne.un.s   IL_0007

    IL_0028:  ret
  } // end of method TextBox::remove_OnBackspacePressed

  .method public hidebysig newslot specialname virtual final 
          instance bool  get_Selected() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      bool StardewValley.Menus.TextBox::_selected
    IL_0006:  ret
  } // end of method TextBox::get_Selected

  .method public hidebysig newslot specialname virtual final 
          instance void  set_Selected(bool 'value') cil managed
  {
    // Code size       104 (0x68)
    .maxstack  2
    .locals init (class StardewValley.SDKs.Steam.SteamHelper V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      bool StardewValley.Menus.TextBox::_selected
    IL_0006:  ldarg.1
    IL_0007:  bne.un.s   IL_000a

    IL_0009:  ret

    IL_000a:  ldarg.0
    IL_000b:  ldarg.1
    IL_000c:  stfld      bool StardewValley.Menus.TextBox::_selected
    IL_0011:  ldarg.0
    IL_0012:  ldfld      bool StardewValley.Menus.TextBox::_selected
    IL_0017:  brfalse.s  IL_002c

    IL_0019:  call       class StardewValley.KeyboardDispatcher StardewValley.Game1::get_keyboardDispatcher()
    IL_001e:  ldarg.0
    IL_001f:  callvirt   instance void StardewValley.KeyboardDispatcher::set_Subscriber(class StardewValley.IKeyboardSubscriber)
    IL_0024:  ldarg.0
    IL_0025:  ldc.i4.1
    IL_0026:  stfld      bool StardewValley.Menus.TextBox::_showKeyboard
    IL_002b:  ret

    IL_002c:  ldarg.0
    IL_002d:  ldc.i4.0
    IL_002e:  stfld      bool StardewValley.Menus.TextBox::_showKeyboard
    IL_0033:  call       class StardewValley.SDKs.SDKHelper StardewValley.Program::get_sdk()
    IL_0038:  isinst     StardewValley.SDKs.Steam.SteamHelper
    IL_003d:  stloc.0
    IL_003e:  ldloc.0
    IL_003f:  brfalse.s  IL_004f

    IL_0041:  ldloc.0
    IL_0042:  ldfld      bool StardewValley.SDKs.Steam.SteamHelper::active
    IL_0047:  brfalse.s  IL_004f

    IL_0049:  ldloc.0
    IL_004a:  callvirt   instance void StardewValley.SDKs.Steam.SteamHelper::CancelKeyboard()
    IL_004f:  call       class StardewValley.KeyboardDispatcher StardewValley.Game1::get_keyboardDispatcher()
    IL_0054:  callvirt   instance class StardewValley.IKeyboardSubscriber StardewValley.KeyboardDispatcher::get_Subscriber()
    IL_0059:  ldarg.0
    IL_005a:  bne.un.s   IL_0067

    IL_005c:  call       class StardewValley.KeyboardDispatcher StardewValley.Game1::get_keyboardDispatcher()
    IL_0061:  ldnull
    IL_0062:  callvirt   instance void StardewValley.KeyboardDispatcher::set_Subscriber(class StardewValley.IKeyboardSubscriber)
    IL_0067:  ret
  } // end of method TextBox::set_Selected

  .event StardewValley.Menus.TextBoxEvent OnEnterPressed
  {
    .addon instance void StardewValley.Menus.TextBox::add_OnEnterPressed(class StardewValley.Menus.TextBoxEvent)
    .removeon instance void StardewValley.Menus.TextBox::remove_OnEnterPressed(class StardewValley.Menus.TextBoxEvent)
  } // end of event TextBox::OnEnterPressed
  .event StardewValley.Menus.TextBoxEvent OnTabPressed
  {
    .addon instance void StardewValley.Menus.TextBox::add_OnTabPressed(class StardewValley.Menus.TextBoxEvent)
    .removeon instance void StardewValley.Menus.TextBox::remove_OnTabPressed(class StardewValley.Menus.TextBoxEvent)
  } // end of event TextBox::OnTabPressed
  .event StardewValley.Menus.TextBoxEvent OnBackspacePressed
  {
    .addon instance void StardewValley.Menus.TextBox::add_OnBackspacePressed(class StardewValley.Menus.TextBoxEvent)
    .removeon instance void StardewValley.Menus.TextBox::remove_OnBackspacePressed(class StardewValley.Menus.TextBoxEvent)
  } // end of event TextBox::OnBackspacePressed
  .property instance class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont
          Font()
  {
    .get instance class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Menus.TextBox::get_Font()
  } // end of property TextBox::Font
  .property instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color
          TextColor()
  {
    .get instance valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Menus.TextBox::get_TextColor()
  } // end of property TextBox::TextColor
  .property instance int32 X()
  {
    .get instance int32 StardewValley.Menus.TextBox::get_X()
    .set instance void StardewValley.Menus.TextBox::set_X(int32)
  } // end of property TextBox::X
  .property instance int32 Y()
  {
    .get instance int32 StardewValley.Menus.TextBox::get_Y()
    .set instance void StardewValley.Menus.TextBox::set_Y(int32)
  } // end of property TextBox::Y
  .property instance int32 Width()
  {
    .get instance int32 StardewValley.Menus.TextBox::get_Width()
    .set instance void StardewValley.Menus.TextBox::set_Width(int32)
  } // end of property TextBox::Width
  .property instance int32 Height()
  {
    .get instance int32 StardewValley.Menus.TextBox::get_Height()
    .set instance void StardewValley.Menus.TextBox::set_Height(int32)
  } // end of property TextBox::Height
  .property instance bool PasswordBox()
  {
    .get instance bool StardewValley.Menus.TextBox::get_PasswordBox()
    .set instance void StardewValley.Menus.TextBox::set_PasswordBox(bool)
  } // end of property TextBox::PasswordBox
  .property instance string Text()
  {
    .get instance string StardewValley.Menus.TextBox::get_Text()
    .set instance void StardewValley.Menus.TextBox::set_Text(string)
  } // end of property TextBox::Text
  .property instance string TitleText()
  {
    .get instance string StardewValley.Menus.TextBox::get_TitleText()
    .set instance void StardewValley.Menus.TextBox::set_TitleText(string)
  } // end of property TextBox::TitleText
  .property instance bool Selected()
  {
    .get instance bool StardewValley.Menus.TextBox::get_Selected()
    .set instance void StardewValley.Menus.TextBox::set_Selected(bool)
  } // end of property TextBox::Selected
} // end of class StardewValley.Menus.TextBox

// *********** DISASSEMBLY COMPLETE ***********************
