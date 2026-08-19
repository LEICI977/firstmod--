
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.0.30319.33440
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit StardewValley.Menus.NamingMenu
       extends StardewValley.Menus.IClickableMenu
{
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(class StardewValley.Menus.NamingMenu/doneNamingBehavior b,
                               string title,
                               [opt] string defaultName) cil managed
  {
    .param [3] = nullref
    // Code size       654 (0x28e)
    .maxstack  7
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.1
    IL_0002:  stfld      int32 StardewValley.Menus.NamingMenu::minLength
    IL_0007:  ldarg.0
    IL_0008:  ldc.i4.1
    IL_0009:  stfld      bool StardewValley.Menus.NamingMenu::FilterInput
    IL_000e:  ldarg.0
    IL_000f:  call       instance void StardewValley.Menus.IClickableMenu::.ctor()
    IL_0014:  ldarg.0
    IL_0015:  ldarg.1
    IL_0016:  stfld      class StardewValley.Menus.NamingMenu/doneNamingBehavior StardewValley.Menus.NamingMenu::doneNaming
    IL_001b:  ldarg.0
    IL_001c:  ldc.i4.0
    IL_001d:  stfld      int32 StardewValley.Menus.IClickableMenu::xPositionOnScreen
    IL_0022:  ldarg.0
    IL_0023:  ldc.i4.0
    IL_0024:  stfld      int32 StardewValley.Menus.IClickableMenu::yPositionOnScreen
    IL_0029:  ldarg.0
    IL_002a:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_002f:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Width()
    IL_0034:  stfld      int32 StardewValley.Menus.IClickableMenu::width
    IL_0039:  ldarg.0
    IL_003a:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_003f:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Height()
    IL_0044:  stfld      int32 StardewValley.Menus.IClickableMenu::height
    IL_0049:  ldarg.0
    IL_004a:  ldarg.2
    IL_004b:  stfld      string StardewValley.Menus.NamingMenu::title
    IL_0050:  ldarg.0
    IL_0051:  ldarg.0
    IL_0052:  ldfld      int32 StardewValley.Menus.IClickableMenu::xPositionOnScreen
    IL_0057:  ldarg.0
    IL_0058:  ldfld      int32 StardewValley.Menus.IClickableMenu::width
    IL_005d:  add
    IL_005e:  ldc.i4.s   51
    IL_0060:  add
    IL_0061:  ldc.i4.s   64
    IL_0063:  add
    IL_0064:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_0069:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Height()
    IL_006e:  ldc.i4.2
    IL_006f:  div
    IL_0070:  ldc.i4.s   64
    IL_0072:  ldc.i4.s   64
    IL_0074:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0079:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::mouseCursors
    IL_007e:  ldc.i4     0x17d
    IL_0083:  ldc.i4     0x169
    IL_0088:  ldc.i4.s   10
    IL_008a:  ldc.i4.s   10
    IL_008c:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0091:  ldc.r4     4.
    IL_0096:  ldc.i4.0
    IL_0097:  newobj     instance void StardewValley.Menus.ClickableTextureComponent::.ctor(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                            valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            float32,
                                                                                            bool)
    IL_009c:  stfld      class StardewValley.Menus.ClickableTextureComponent StardewValley.Menus.NamingMenu::randomButton
    IL_00a1:  ldarg.0
    IL_00a2:  ldnull
    IL_00a3:  ldnull
    IL_00a4:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont StardewValley.Game1::dialogueFont
    IL_00a9:  ldsfld     valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color StardewValley.Game1::textColor
    IL_00ae:  newobj     instance void StardewValley.Menus.TextBox::.ctor(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                          class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                          class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.SpriteFont,
                                                                          valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Color)
    IL_00b3:  stfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_00b8:  ldarg.0
    IL_00b9:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_00be:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_00c3:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Width()
    IL_00c8:  ldc.i4.2
    IL_00c9:  div
    IL_00ca:  ldc.i4     0xc0
    IL_00cf:  sub
    IL_00d0:  callvirt   instance void StardewValley.Menus.TextBox::set_X(int32)
    IL_00d5:  ldarg.0
    IL_00d6:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_00db:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_00e0:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Height()
    IL_00e5:  ldc.i4.2
    IL_00e6:  div
    IL_00e7:  callvirt   instance void StardewValley.Menus.TextBox::set_Y(int32)
    IL_00ec:  ldarg.0
    IL_00ed:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_00f2:  ldc.i4     0x100
    IL_00f7:  callvirt   instance void StardewValley.Menus.TextBox::set_Width(int32)
    IL_00fc:  ldarg.0
    IL_00fd:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0102:  ldc.i4     0xc0
    IL_0107:  callvirt   instance void StardewValley.Menus.TextBox::set_Height(int32)
    IL_010c:  ldarg.0
    IL_010d:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0112:  ldarg.0
    IL_0113:  ldftn      instance void StardewValley.Menus.NamingMenu::textBoxEnter(class StardewValley.Menus.TextBox)
    IL_0119:  newobj     instance void StardewValley.Menus.TextBoxEvent::.ctor(object,
                                                                               native int)
    IL_011e:  callvirt   instance void StardewValley.Menus.TextBox::add_OnEnterPressed(class StardewValley.Menus.TextBoxEvent)
    IL_0123:  call       class StardewValley.KeyboardDispatcher StardewValley.Game1::get_keyboardDispatcher()
    IL_0128:  ldarg.0
    IL_0129:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_012e:  callvirt   instance void StardewValley.KeyboardDispatcher::set_Subscriber(class StardewValley.IKeyboardSubscriber)
    IL_0133:  ldarg.0
    IL_0134:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0139:  ldarg.3
    IL_013a:  brtrue.s   IL_0143

    IL_013c:  call       string StardewValley.Dialogue::randomName()
    IL_0141:  br.s       IL_0144

    IL_0143:  ldarg.3
    IL_0144:  callvirt   instance void StardewValley.Menus.TextBox::set_Text(string)
    IL_0149:  ldarg.0
    IL_014a:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_014f:  ldc.i4.1
    IL_0150:  callvirt   instance void StardewValley.Menus.TextBox::set_Selected(bool)
    IL_0155:  ldarg.0
    IL_0156:  ldarg.0
    IL_0157:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_015c:  callvirt   instance int32 StardewValley.Menus.TextBox::get_X()
    IL_0161:  ldarg.0
    IL_0162:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0167:  callvirt   instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_016c:  add
    IL_016d:  ldc.i4.s   64
    IL_016f:  add
    IL_0170:  ldc.i4.s   48
    IL_0172:  add
    IL_0173:  ldc.i4.8
    IL_0174:  sub
    IL_0175:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_017a:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Height()
    IL_017f:  ldc.i4.2
    IL_0180:  div
    IL_0181:  ldc.i4.4
    IL_0182:  add
    IL_0183:  ldc.i4.s   64
    IL_0185:  ldc.i4.s   64
    IL_0187:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_018c:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::mouseCursors
    IL_0191:  ldc.i4     0x17d
    IL_0196:  ldc.i4     0x169
    IL_019b:  ldc.i4.s   10
    IL_019d:  ldc.i4.s   10
    IL_019f:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_01a4:  ldc.r4     4.
    IL_01a9:  ldc.i4.0
    IL_01aa:  newobj     instance void StardewValley.Menus.ClickableTextureComponent::.ctor(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                            valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            float32,
                                                                                            bool)
    IL_01af:  dup
    IL_01b0:  ldc.i4.s   103
    IL_01b2:  stfld      int32 StardewValley.Menus.ClickableComponent::myID
    IL_01b7:  dup
    IL_01b8:  ldc.i4.s   102
    IL_01ba:  stfld      int32 StardewValley.Menus.ClickableComponent::leftNeighborID
    IL_01bf:  stfld      class StardewValley.Menus.ClickableTextureComponent StardewValley.Menus.NamingMenu::randomButton
    IL_01c4:  ldarg.0
    IL_01c5:  ldarg.0
    IL_01c6:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_01cb:  callvirt   instance int32 StardewValley.Menus.TextBox::get_X()
    IL_01d0:  ldarg.0
    IL_01d1:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_01d6:  callvirt   instance int32 StardewValley.Menus.TextBox::get_Width()
    IL_01db:  add
    IL_01dc:  ldc.i4.s   32
    IL_01de:  add
    IL_01df:  ldc.i4.4
    IL_01e0:  add
    IL_01e1:  ldsflda    valuetype [xTile]xTile.Dimensions.Rectangle StardewValley.Game1::uiViewport
    IL_01e6:  call       instance int32 [xTile]xTile.Dimensions.Rectangle::get_Height()
    IL_01eb:  ldc.i4.2
    IL_01ec:  div
    IL_01ed:  ldc.i4.8
    IL_01ee:  sub
    IL_01ef:  ldc.i4.s   64
    IL_01f1:  ldc.i4.s   64
    IL_01f3:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_01f8:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::mouseCursors
    IL_01fd:  ldsfld     class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D StardewValley.Game1::mouseCursors
    IL_0202:  ldc.i4.s   46
    IL_0204:  ldc.i4.m1
    IL_0205:  ldc.i4.m1
    IL_0206:  call       valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle StardewValley.Game1::getSourceRectForStandardTileSheet(class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                                                                                int32,
                                                                                                                                                int32,
                                                                                                                                                int32)
    IL_020b:  ldc.r4     1.
    IL_0210:  ldc.i4.0
    IL_0211:  newobj     instance void StardewValley.Menus.ClickableTextureComponent::.ctor(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            class [MonoGame.Framework]Microsoft.Xna.Framework.Graphics.Texture2D,
                                                                                            valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                            float32,
                                                                                            bool)
    IL_0216:  dup
    IL_0217:  ldc.i4.s   102
    IL_0219:  stfld      int32 StardewValley.Menus.ClickableComponent::myID
    IL_021e:  dup
    IL_021f:  ldc.i4.s   103
    IL_0221:  stfld      int32 StardewValley.Menus.ClickableComponent::rightNeighborID
    IL_0226:  dup
    IL_0227:  ldc.i4.s   104
    IL_0229:  stfld      int32 StardewValley.Menus.ClickableComponent::leftNeighborID
    IL_022e:  stfld      class StardewValley.Menus.ClickableTextureComponent StardewValley.Menus.NamingMenu::doneNamingButton
    IL_0233:  ldarg.0
    IL_0234:  ldarg.0
    IL_0235:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_023a:  callvirt   instance int32 StardewValley.Menus.TextBox::get_X()
    IL_023f:  ldarg.0
    IL_0240:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0245:  callvirt   instance int32 StardewValley.Menus.TextBox::get_Y()
    IL_024a:  ldc.i4     0xc0
    IL_024f:  ldc.i4.s   48
    IL_0251:  newobj     instance void [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle::.ctor(int32,
                                                                                                    int32,
                                                                                                    int32,
                                                                                                    int32)
    IL_0256:  ldstr      ""
    IL_025b:  newobj     instance void StardewValley.Menus.ClickableComponent::.ctor(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Rectangle,
                                                                                     string)
    IL_0260:  dup
    IL_0261:  ldc.i4.s   104
    IL_0263:  stfld      int32 StardewValley.Menus.ClickableComponent::myID
    IL_0268:  dup
    IL_0269:  ldc.i4.s   102
    IL_026b:  stfld      int32 StardewValley.Menus.ClickableComponent::rightNeighborID
    IL_0270:  stfld      class StardewValley.Menus.ClickableComponent StardewValley.Menus.NamingMenu::textBoxCC
    IL_0275:  call       class StardewValley.Options StardewValley.Game1::get_options()
    IL_027a:  callvirt   instance bool StardewValley.Options::get_SnappyMenus()
    IL_027f:  brfalse.s  IL_028d

    IL_0281:  ldarg.0
    IL_0282:  callvirt   instance void StardewValley.Menus.IClickableMenu::populateClickableComponentList()
    IL_0287:  ldarg.0
    IL_0288:  callvirt   instance void StardewValley.Menus.IClickableMenu::snapToDefaultClickableComponent()
    IL_028d:  ret
  } // end of method NamingMenu::.ctor

} // end of class StardewValley.Menus.NamingMenu

// *********** DISASSEMBLY COMPLETE ***********************
