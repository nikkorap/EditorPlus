# EditorPlus

A bepinex mod to enhance your Nuclear Option mission editor experience. Adds a new nodegraph based objective/outcome UI, click and drag on the ports to change connections. full controls list is in the included readme

this is quite an early beta build, no guarantees that it wont corrupt your mission file (this hasn't actually happened in my testing yet tho). 

If you encounter any bugs then please report them.
I have many ideas on what to add in the future, but any requests are welcome.
Feedback is appreciated, especially on how to improve the visual design 

<img width="960" height="540" alt="image" src="https://github.com/user-attachments/assets/6489d11f-7bdb-4868-85cf-6edbeec75d87" />


## How to install BepInEx (5 mono) guide [https://docs.bepinex.dev/articles/user_guide/installation/index.html]

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

- in the downloaded zip file there is a folder, place it in [Nuclear Option\BepInEx\plugins\ (optional folder)]
- the mod .dll and .nobp file must be together in the same folder, they can be placed under any subfolder of plugins
