# EditorPlus

A bepinex mod to enhance your Nuclear Option mission editor experience. Adds a new nodegraph based objective/outcome UI, click and drag on the ports to change connections. full controls list is in the included readme

this is quite an early beta build, no guarantees that it wont corrupt your mission file (this hasn't actually happened in my testing yet tho). 

If you encounter any bugs then please report them.
I have many ideas on what to add in the future, but any requests are welcome.
Feedback is appreciated, especially on how to improve the visual design 


# How to install BepInEx (5 mono) guide [https://docs.bepinex.dev/articles/user_guide/installation/index.html]

TLDR:
1. Download the correct version of BepInEx (bepinex 5 mono) [https://github.com/BepInEx/BepInEx]
2. Extract the contents into the game root (where [NuclearOption.exe] lives)
3. Start the game once to generate configuration files.
4. Open [Nuclear Option\BepInEx\config\BepInEx.cfg] and make sure that the setting 
   [Chainloader]
   HideGameManagerObject = true.

5. (optional) also edit 
   [Logging.Console]
   Enabled = true.

(you can also change bepinex settings ingame using the Mod Configuration manager)


## How to install mods for BepInEx?
- For your first mod download this mod: [Mod Configuration manager][https://github.com/BepInEx/BepInEx.ConfigurationManager]
- This is an awesome plugin that gives you an ingame mod menu to control mods (i try to add support for it in my mods) [TOGGLE WITH F1]

  ![ModMenuDemo](https://github.com/user-attachments/assets/6ee561c4-f2ac-4798-9896-a3dc8bca9714)
  
- in the downloaded zip file there is a BepInEx folder, you can drop it right in the install folder.
- Some other mods might be just .dll files, drop those in [Nuclear Option\BepInEx\plugins\ (optional folder)]

