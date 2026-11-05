# NPC talk text: one readable style

**Intent.** Players struggle to read what NPCs say. Every NPC line, voiced or not, should render in the same readable text style.

**Goals.** One shared talk size (16, Nunito body font) used by every NPC text renderer. Answer buttons stop using the pixel font.

**Non-goals.** No wording changes (the voice clip ids hash the Mongolian words, so changed words would silence them). No new settings. Box frames and colours, speaker names (pixel font), typing speed stay as they are.

**Anti-goal.** Any voiced story line losing its clip.

**Approaches considered.**
1. One shared talk size applied to all 7 renderers. Small diff. **Chosen.**
2. Option 1 plus restyling the dark story box to cream. Bigger visual change to the cinematics.
3. Option 1 plus a player text-size setting. More code and a new setting.

**Decided.** `GameUI.TALK = 16`. Renderers:
- `Story.cs` `DrawLine` story box text: 17 to 16.
- `GameUI.sOpt` answer buttons: pixel font at F(14) to body font at 16.
- `GameUI.sSpeech` bubbles: 12 to 16 (story actors, race crowd, battle shouts, online chat).
- `WalkScene.cs` info box: `Ts(12)` to 16.
- `GameUI.sTeach` (Basar and the teachers): 15 to 16.
- `GameUI.sPopText` (Basar's toast card): 15 to 16.
- `Tasks.cs` task-giver box: 15 (from `sLabel`) to 16.

**Left to the builder.** Bubble `maxW` stays unless the text at 16 looks cramped.
