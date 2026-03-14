# EditorPlus (OUTDATED)
Check out the updated version by Endar: https://github.com/Endar728/EditorPlus-Endar728

A bepinex mod to enhance the Nuclear Option mission editor experience.

### New UI
* Nodegraph-based objective/outcome UI, click and drag on ports to change connections, hover over a connection or port and press delete to remove connections.
* Ghost lines from nodes to units, airbases, and waypoints.
* Add selected units to a node: open Node UI, hover the node, Shift + LMB.

### Group unit selection
* Group selection: `Shift` + `LMB` drag to box select; click any selected unit to set the pivot.
* Remove and Faction settings apply to all selected units.

### Unit placement
* Rapid placement of units while holding `Ctrl`.
* Toggle to enable hold position when placing new units.
* Grid snapping (WIP).
* Toggle terrain collision.
* hold `Ctrl` to switch between position and rotation.
* Removed height limits.

### other
* Extended all dropdowns.

If you encounter any bugs then please report them. Feedback is appreciated!
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
