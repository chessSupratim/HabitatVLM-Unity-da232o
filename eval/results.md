# HabitatVLM results

Episodes: 72  ·  tasks: 30  ·  filter: all

## Summary by backend

| Backend | Model | Episodes | Success | Easy | Medium | Hard | Avg steps (successes) | Avg model latency / step | Invalid replies |
|---|---|---|---|---|---|---|---|---|---|
| kaggle · prompt v1 | qwen3-vl:8b-instruct | 5 | **3/5 (60%)** | 2/3 (67%) | 1/2 (50%) | – | 7.7 | 6.2 s | 15% (6/40) |
| kaggle · prompt v2 | qwen3-vl:8b-instruct | 28 | **26/28 (93%)** | 10/10 (100%) | 10/10 (100%) | 6/8 (75%) | 6.0 | 4.7 s | 0% (0/185) |
| kaggle · prompt v3 | qwen3-vl:8b-instruct | 24 | **20/24 (83%)** | 7/8 (88%) | 8/8 (100%) | 5/8 (62%) | 5.9 | 4.9 s | 0% (0/165) |
| kaggle · prompt v3.2 | qwen3-vl:8b-instruct | 9 | **6/9 (67%)** | 6/9 (67%) | – | – | 3.5 | 6.2 s | 12% (3/25) |
| kaggle · prompt v3.3 | qwen3-vl:8b-instruct | 1 | **1/1 (100%)** | 1/1 (100%) | – | – | 3.0 | 5.1 s | 0% (0/3) |
| kaggle · prompt v3.4 | qwen3-vl:8b-instruct | 3 | **2/3 (67%)** | 2/3 (67%) | – | – | 4.0 | 5.6 s | 20% (2/10) |
| mock · prompt v1 | scripted-demo | 2 | **1/2 (50%)** | 0/1 (0%) | 1/1 (100%) | – | 7.0 | 0.0 s | 0% (0/14) |

## Per task

| Room | Task | Tier | Instruction | kaggle · prompt v1 | kaggle · prompt v2 | kaggle · prompt v3 | kaggle · prompt v3.2 | kaggle · prompt v3.3 | kaggle · prompt v3.4 | mock · prompt v1 |
|---|---|---|---|---|---|---|---|---|---|---|
| room_01 | T01 | easy | Open the left cabinet | ✅ 1/1 · 5 steps | ✅ 1/1 · 3 steps | ✅ 1/1 · 3 steps | ◐ 1/3 · 2 steps | – | – | ❌ 0/1 · 7 steps |
| room_01 | T02 | easy | Open the right cabinet | ✅ 1/1 · 5 steps | ✅ 2/2 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T03 | easy | Pick up the blue canister | ❌ 0/1 · 11 steps | ✅ 2/2 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T09 | easy | Pick up the red canister | – | ✅ 1/1 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T10 | easy | Pick up the green canister | – | ✅ 1/1 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T11 | easy | Pick up the toolbox | – | ✅ 1/1 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T12 | easy | Open the purple cabinet | – | ✅ 1/1 · 3 steps | ❌ 0/1 · 3 steps | – | – | – | – |
| room_01 | T13 | easy | Pick up the canister that is on the table | – | ✅ 1/1 · 3 steps | ✅ 1/1 · 3 steps | – | – | – | – |
| room_01 | T04 | medium | Put the red canister in the left cabinet | ◐ 1/2 · 10 steps | ✅ 3/3 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | ✅ 1/1 · 7 steps |
| room_01 | T05 | medium | Put the toolbox on the shelf | – | ✅ 1/1 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T06 | medium | Move the green canister from the table to the shelf | – | ✅ 1/1 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T14 | medium | Put the blue canister on the table | – | ✅ 1/1 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T15 | medium | Put the blue canister in the right cabinet | – | ✅ 1/1 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T16 | medium | Put the toolbox in the orange cabinet | – | ✅ 1/1 · 13 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T17 | medium | Open both cabinets | – | ✅ 1/1 · 5 steps | ✅ 1/1 · 4 steps | – | – | – | – |
| room_01 | T18 | medium | Put the red canister on the table | – | ✅ 1/1 · 6 steps | ✅ 1/1 · 6 steps | – | – | – | – |
| room_01 | T07 | hard | Put the green canister in the right cabinet and close it | – | ✅ 1/1 · 8 steps | ✅ 1/1 · 7 steps | – | – | – | – |
| room_01 | T08 | hard | Put the red and blue canisters in the right cabinet | – | ❌ 0/1 · 14 steps | ❌ 0/1 · 14 steps | – | – | – | – |
| room_01 | T19 | hard | Put the red canister in the left cabinet and the blue canister in the right cabinet | – | ✅ 1/1 · 11 steps | ✅ 1/1 · 11 steps | – | – | – | – |
| room_01 | T20 | hard | Put the toolbox in the left cabinet and close it | – | ✅ 1/1 · 7 steps | ✅ 1/1 · 7 steps | – | – | – | – |
| room_01 | T21 | hard | Move both canisters from the shelf to the table | – | ❌ 0/1 · 15 steps | ✅ 1/1 · 15 steps | – | – | – | – |
| room_01 | T22 | hard | Put the toolbox in the right cabinet and the green canister on the shelf | – | ✅ 1/1 · 10 steps | ❌ 0/1 · 15 steps | – | – | – | – |
| room_01 | T23 | hard | Put the red, blue and green canisters in the left cabinet | – | ✅ 1/1 · 14 steps | ❌ 0/1 · 15 steps | – | – | – | – |
| room_01 | T24 | hard | Put the red canister in the left cabinet and make sure both cabinets are closed | – | ✅ 1/1 · 9 steps | ✅ 1/1 · 11 steps | – | – | – | – |
| room_02 | B01 | easy | Pick up the oxygen tank | – | – | – | ◐ 2/3 · 2 steps | – | – | – |
| room_02 | B02 | easy | Open the teal locker | – | – | – | ✅ 1/1 · 6 steps | – | – | – |
| room_03 | L01 | easy | Pick up the magenta tube | – | – | – | ✅ 1/1 · 3 steps | – | – | – |
| room_04 | G01 | easy | Open the fridge | – | – | – | ✅ 1/1 · 3 steps | – | – | – |
| room_05 | S01 | easy | Pick up the pencil cup | – | – | – | – | – | ◐ 2/3 · 3 steps | – |
| room_06 | R01 | easy | Open the storage tub | – | – | – | – | ✅ 1/1 · 3 steps | – | – |

## Per room

| Room | kaggle · prompt v1 | kaggle · prompt v2 | kaggle · prompt v3 | kaggle · prompt v3.2 | kaggle · prompt v3.3 | kaggle · prompt v3.4 | mock · prompt v1 |
|---|---|---|---|---|---|---|---|
| room_01 | 3/5 (60%) | 26/28 (93%) | 20/24 (83%) | 1/3 (33%) | – | – | 1/2 (50%) |
| room_02 | – | – | – | 3/4 (75%) | – | – | – |
| room_03 | – | – | – | 1/1 (100%) | – | – | – |
| room_04 | – | – | – | 1/1 (100%) | – | – | – |
| room_05 | – | – | – | – | – | 2/3 (67%) | – |
| room_06 | – | – | – | – | 1/1 (100%) | – | – |

## Failure analysis

**kaggle · prompt v1** — 2 failed episodes
- stopped by user: 2

**kaggle · prompt v2** — 2 failed episodes
- said done too early / wrong result: 1
- step limit: repeated failing actions: 1
- failed robot actions (all episodes): out of reach ×7, container closed ×3, gripper full/empty ×3

**kaggle · prompt v3** — 4 failed episodes
- said done too early / wrong result: 2
- step limit: repeated failing actions: 2
- failed robot actions (all episodes): out of reach ×13, gripper full/empty ×7, container closed ×2

**kaggle · prompt v3.2** — 3 failed episodes
- said done too early / wrong result: 1
- stopped by user: 1
- infrastructure (bridge/model unreachable): 1
- failed robot actions (all episodes): model backend error ×3, out of reach ×1

**kaggle · prompt v3.3** — 0 failed episodes

**kaggle · prompt v3.4** — 1 failed episodes
- infrastructure (bridge/model unreachable): 1
- failed robot actions (all episodes): model backend error ×2, out of reach ×2

**mock · prompt v1** — 1 failed episodes
- said done too early / wrong result: 1

### Failed episodes (for review)

| Backend | Task | Steps | Reason |
|---|---|---|---|
| kaggle · prompt v1 | T03 | 11 | stopped by user / holding blue_canister (actually nothing) |
| kaggle · prompt v1 | T04 | 6 | stopped by user / red_canister in cabinet_left (actually at shelf_a) |
| kaggle · prompt v2 | T08 | 14 | model said done / OK red_canister in cabinet_right (actually at cabinet_right); NO blue_canister in cabinet_right (actually at cabinet_left) |
| kaggle · prompt v2 | T21 | 15 | step limit (15) reached / NO red_canister on table (actually at shelf_a); NO blue_canister on table (actually held by robot) |
| kaggle · prompt v3 | T08 | 14 | model said done / OK red_canister in cabinet_right (actually at cabinet_right); NO blue_canister in cabinet_right (actually held by robot) |
| kaggle · prompt v3 | T12 | 3 | model said done / cabinet_left open (actually closed) |
| kaggle · prompt v3 | T22 | 15 | step limit (15) reached / NO toolbox in cabinet_right (actually at table); OK green_canister on shelf_a (actually at shelf_a) |
| kaggle · prompt v3 | T23 | 15 | step limit (15) reached / NO red_canister in cabinet_left (actually held by robot); NO blue_canister in cabinet_left (actually at shelf_a);  |
| kaggle · prompt v3.2 | B01 | 1 | model said done / holding oxygen_tank (actually nothing) |
| kaggle · prompt v3.2 | T01 | 1 | stopped by user / cabinet_left open (actually closed) |
| kaggle · prompt v3.2 | T01 | 2 | model backend 'kaggle' unreachable - is the Kaggle session/tunnel running and KAGGLE_TUNNEL_URL in bridge/.env current? (or start the bridge |
| kaggle · prompt v3.4 | S01 | 2 | model backend 'kaggle' unreachable - is the Kaggle session/tunnel running and KAGGLE_TUNNEL_URL in bridge/.env current? (or start the bridge |
| mock · prompt v1 | T01 | 7 | model said done / cabinet_left open (actually closed) |

Charts: `charts/success_by_tier.png`, `charts/steps_by_tier.png`
