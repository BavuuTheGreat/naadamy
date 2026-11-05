# NPC Talk Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every NPC text renderer uses one readable talk size: 16, in the Nunito body font.

**Architecture:** One constant, `GameUI.TALK`. The styles built in `GameUI.BuildStyles` use `T(TALK)`. The renderers that size text by hand use `Mathf.RoundToInt(TALK * UiScale)`.

**Tech Stack:** Unity 6000.6.7f1, C#, IMGUI.

**Spec:** `docs/superpowers/specs/2026-11-04-npc-talk-text-design.md`

## Global Constraints

- No string literal in any `Say`/`Ask` call changes. Voice clip ids hash the words.
- `TALK = 16`.
- Box frames, colours, the speaker-name pixel font and the typing speed do not change.

## Review Focus

- The voiced lines keep their clips: `node unity/Tools/tts/voice.mjs --list` still lists 43 lines, and all 43 have a `.wav`.
- Long story lines with 3 answers still fit the story box on a phone-height screen.
- Speech bubbles at 16 wrap inside `maxW` and do not cover the speaker.
- The Basar toast card grows to fit the bigger text. `ch` uses `sPopText.CalcHeight`, so it should.
- The project compiles. Unity batchmode reports no errors.

---

### Task 1: Shared talk size

**Files:**
- Modify: `unity/Assets/Scripts/GameUI.cs` (fields near line 119; lines 177, 179, 187, 200)
- Modify: `unity/Assets/Scripts/Story.cs:402`
- Modify: `unity/Assets/Scripts/WalkScene.cs:108`
- Modify: `unity/Assets/Scripts/Tasks.cs:250`

**Interfaces:**
- Produces: `public const float TALK = 16;` on the `Game` partial in `GameUI.cs`.

- [ ] **Step 1:** Add `TALK`. Set `sPopText`, `sTeach` and `sSpeech` to `T(TALK)`. Set `sOpt` to `bodyFont` at `T(TALK)`.
- [ ] **Step 2:** In `Story.DrawLine`, set the box `st` fontSize to `Mathf.RoundToInt(U * Game.TALK)`. In `WalkScene` set `ist` fontSize to the same. In `Tasks` set `sst` fontSize to the same.
- [ ] **Step 3:** Verify. Run Unity batchmode with `-quit -logFile -`. Expected: no `error CS` lines. Run `node unity/Tools/tts/voice.mjs --list | wc -l`. Expected: 44 lines (43 lines and a header).
- [ ] **Step 4:** Commit on a new branch: `fix(ui): NPC talk in one readable size`.
