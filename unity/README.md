# Naadamy — Unity version

The whole of Naadamy as a Unity 6 app for Android and PC: Нутаг, the heritage school, Basar the AI dog, trips to all 21 aimags and Ulaanbaatar, the Mongol script academy, the 3D first-person view, playing together online or on one Wi-Fi, and everything around them. It uses no packages: the village, characters and animals are drawn pixel by pixel in code (`Px.cs`, `PxMore.cs`), the sounds are made in code (`Sfx.cs`), the UI is IMGUI, and the 3D view uses three small shaders in `Assets/Resources/Shaders`.

## Open and play

1. Unity Hub → **Add project from disk** → choose this `unity` folder. Editor version **6000.6.7f1**.
2. On first open, `Naadamy/Set up project` runs by itself. It creates `Assets/Scenes/Nutag.unity`, adds it to the build, and sets the app name, landscape screen and network permissions.
3. Press **Play**.

Controls: **WASD/arrows** walk · **Shift** run · **Space** jump · **E** use · **B** Basar · **Esc** close.
On a phone: drag anywhere to walk (the joystick appears under your finger), tap a place or a building to go there, and use the big blue button.
In 3D: click the view to look with the mouse, WASD, Space, Shift, E, and in a battle click or F to shoot. On a phone, drag on the left to walk and on the right to look.

## Basar AI and playing together

Both use the Naadamy server (`server/`). By default the game talks to the hosted one on Render (`Family.HOSTED` in `Family.cs`), so nothing needs setting up. To use your own on a home network instead, run `node server/server.js` in the repo root (put `ANTHROPIC_API_KEY` in `.env` for Basar AI), then in the game open ☰ → **Settings** and write the address the server prints, for example `192.168.1.10:8000`. Without a server, Basar uses his offline knowledge and you play alone. Everyone with the same **room code** (the ● button in Нутаг) shares one village: you see each other walk and ride, say phrases and emotes (with the room's recent chat), see each other's gers go up step by step, and can challenge a friend to a live wrestling match. Two people can also play on one screen: the ● Together button in Нутаг.

## Build

- **Android:** install *Android Build Support* (with OpenJDK and the Android SDK & NDK) for 6000.6.7f1 in Unity Hub → Installs → ⚙ → Add modules. Then use the menu **Naadamy → Build Android APK** (output: `Builds/Android/Naadamy.apk`).
- **Windows:** **Naadamy → Build Windows** (needs *Windows Build Support*).
- **Web (browser):** install *WebGL Build Support*, then **Naadamy → Build WebGL** (output: `Builds/WebGL`, with its own `vercel.json`). It is live at https://naadamy-unity.vercel.app: every push to `main` that changes `unity/` builds it on GitHub and publishes it there (`.github/workflows/unity-web.yml`, which needs the repo secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` and `VERCEL_TOKEN`). To publish by hand instead, run `npx vercel deploy --prod` from that folder. In a browser the game brings its own font (`Resources/Fonts`, a browser has none with Mongolian letters), plays together over the browser's own WebSocket (`Plugins/WebGL`), talks to the server over `https://` (the hosted one is; your own needs it too) for Basar AI and playing together, and has no voice recording in the memory book.

## What is where

Most of the game was first written for a web version (removed in #82); the code keeps its rules and coordinates.

| Part | Unity |
|---|---|
| Village map, collision, A* paths | `World.cs`: the same seeded generator, so the grid is identical |
| Pixel art: tiles, buildings, people, animals, icons | `Px.cs`, `PxMore.cs`, `Emo.cs` |
| Walking, joystick, HUD, windows | `Game.cs`, `GameUI.cs` |
| Milking and dairy foods, shop, wardrobe, bag, food | `GameDairy.cs`, `GameShop.cs`, `Rules.cs`, `Content.cs` |
| Fishing, horses and races, archery, wrestling and the wrestling school, Grandma, townspeople | `GameFish.cs`, `GameHorse.cs`, `GameArchery.cs`, `GameWrestle.cs`, `GameEmee.cs` |
| Hands-on tasks: drag, rhythm, spots, hold, turn, choose | `Tasks.cs` |
| Making the white foods by hand | `DairyWork.cs`, `GameDairy.cs` |
| Building your own ger by hand: shearing, felt, Grandpa Craftsman, the 9 steps | `GerWork.cs` |
| Its inside, furniture and colours | `GameHome.cs`, `WalkScene.cs` |
| Two players on one screen | `Together.cs` |
| The historian and defending the homeland | `History.cs` |
| Profiles, picture passwords, parent gate | `Profiles.cs`, `AppState.cs`, `SaveData.cs` |
| Basar (offline knowledge, AI chat, quizzes) | `Basar.cs` |
| Heritage lessons, quiz party, family activities | `Lessons.cs`, `Scenes.cs` |
| Aimag passport and trips, travel pass | `Aimags.cs`, `Travel.cs` |
| 3D first person (voxel worlds) | `FirstPerson.cs` |
| Mongol script academy | `Bichig.cs` |
| Bolormaa's tutorial, the story box, voices | `Tutorial.cs` |
| Achievements, seasons | `Achievements.cs`, `Seasons.cs` |
| Menu, parent dashboard, memory book, settings, about | `AppPages.cs` |
| Playing together, family login, cloud save (the server's protocol: `docs/PROTOCOL.md`) | `Net.cs`, `Family.cs`, `Cloud.cs` |
| Background music | `Music.cs`, `Resources/Music` |

## Content and pictures

The stories, questions and facts are in `Assets/Resources/Data/*.json`, and the pictures made from them (the aimag trip maps, the trip animals, Nomin, the 3D block textures, the 3D worlds in `Resources/World` and the Mongol script letters) are in `Assets/Resources`. Edit those files directly.

They were first exported from the web version, which was removed in #82 along with the tools that exported it.

`Js.cs` copies JavaScript's number rules (`Math.round`, the 32-bit `hash`, the seeded random), so the generated content still comes out exactly as the web version made it.
