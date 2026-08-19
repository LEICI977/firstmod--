
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.0.30319.33440
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit StardewValley.Menus.NamingMenu
       extends StardewValley.Menus.IClickableMenu
{
  .method public hidebysig virtual instance void 
          receiveKeyPress(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Input.Keys key) cil managed
  {
    // Code size       44 (0x2c)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class StardewValley.Menus.TextBox StardewValley.Menus.NamingMenu::textBox
    IL_0006:  callvirt   instance bool StardewValley.Menus.TextBox::get_Selected()
    IL_000b:  brtrue.s   IL_002b

    IL_000d:  call       class StardewValley.Options StardewValley.Game1::get_options()
    IL_0012:  call       class StardewValley.Options StardewValley.Game1::get_options()
    IL_0017:  ldfld      valuetype StardewValley.InputButton[] StardewValley.Options::menuButton
    IL_001c:  ldarg.1
    IL_001d:  callvirt   instance bool StardewValley.Options::doesInputListContain(valuetype StardewValley.InputButton[],
                                                                                   valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Input.Keys)
    IL_0022:  brtrue.s   IL_002b

    IL_0024:  ldarg.0
    IL_0025:  ldarg.1
    IL_0026:  call       instance void StardewValley.Menus.IClickableMenu::receiveKeyPress(valuetype [MonoGame.Framework]Microsoft.Xna.Framework.Input.Keys)
    IL_002b:  ret
  } // end of method NamingMenu::receiveKeyPress

} // end of class StardewValley.Menus.NamingMenu

// *********** DISASSEMBLY COMPLETE ***********************
