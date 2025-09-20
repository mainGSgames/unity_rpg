# Unity RPG Project Overview (Agents Guide)

This document maps the core systems, flows, and extension points of this Unity RPG project (based on the Boss Room sample). Use it as a practical guide when adding features, debugging, or onboarding a new engineer.

This game is build in Unity 6.2

## Unity MCP Quick Reference (agent tooling)

* validate: `validate_script` (syntax/diagnostics).
* script edits: `script_apply_edits` (insert/replace/delete methods).
* text edits: `apply_text_edits` (raw range edits).
* read file: `read_resource` (under `Assets/`).
* list/search: `list_resources`, `find_in_file` (regex).
* sha/size: `get_sha`.
* scripts CRUD: `create_script` / `delete_script` (or `manage_script`).
* shaders: `manage_shader`.
* scenes: `manage_scene`.
* assets: `manage_asset`.
* editor: `manage_editor`.
* menu: `manage_menu_item`.
* console: `read_console`.

Tips

* Prefer `script_apply_edits` over raw text edits; run `validate_script` after changes.
* Use `find_in_file` + `list_resources` to locate targets.
* For Unity 6.x, replace obsolete Find APIs with ByType/First/Any variants.

### Unity 6.x migration reminders (code-impacting)

* **Object.Find → ByType/First/Any**

  * `FindObjectsOfType<T>()` → `FindObjectsByType<T>(FindObjectsSortMode.None)`
  * `FindObjectOfType<T>()` → `FindFirstObjectByType<T>()` (or `FindAnyObjectByType<T>()`)
  * Cache results; don’t call per-frame.

* **UI Toolkit**

  * `ExecuteDefaultAction*` and `PreventDefault` deprecated.
  * Use `HandleEventTrickleDown` / `HandleEventBubbleUp`; stop via `StopPropagation`/`StopImmediatePropagation`.

* **Lighting**

  * Auto-Generate removed; click **Generate Lighting**.
  * Default Lighting Data Asset; probes not baked by default.
  * `LightingSettings` Gaussian radius fields now `float` (`filteringGaussianRadius*`).

* **Android embedding**

  * `UnityPlayer` split: use `UnityPlayerForActivityOrService` or `UnityPlayerForGameActivity`.
  * `UnityPlayer` no longer extends `FrameLayout`; use `getFrameLayout()`.
  * Recreate custom Gradle templates or use new modification API (stack versions changed).

* **Runtime textures**

  * Quality Settings no longer clamp runtime mip levels; use `MipmapLimitDescriptor` or set at load.

* **Scripting/runtime**

  * C# **9** in Unity 6.2; avoid C# 10+.
  * API compatibility: **.NET Standard 2.0** or **.NET 4.x** (pick per plugin).

* **Packages & caches**

  * Global package cache no longer `packages/`; `UPM_CACHE_PATH` unsupported → use `UPM_CACHE_ROOT`.

* **Render-pipeline editor API**

  * Prefer `CustomEditor`, `VolumeComponentMenu`, `SupportedOnRenderPipelineAttribute`.

* **NGO versions**

  * Use NGO built for 6000.2 (e.g., 2.5.x). Pin packages (Transport/Collections).

* **Editor UX**

  * **Assets/Create** menu and ScriptTemplates renamed; update any menu path automation.

## Architecture Overview

* **Netcode**: Server-authoritative via NGO; clients send input RPCs; state via NetworkVariables/client RPCs.
* **DI**: VContainer for composition, scoped services, and message channels.
* **Data-Driven**: ScriptableObjects for Avatars/CharacterClasses/Actions via central `GameDataSource`.
* **Scenes & State**: Discrete states per scene; server drives transitions with NGO SceneManager.
* **Input/Actions**: Client gathers input → server validates/executes → clients visualize.
* **AI/Interactables**: Server-side AI states; interactables as networked behaviours.

## Entry Point & Composition

* `ApplicationController`: registers DI, persists core singletons (`ConnectionManager`, `NetworkManager`, `UpdateRunner`, `LocalSession(User)`, `PersistentGameState`), initializes UGS (Auth/Sessions/Relay), loads `MainMenu`.
* Editor: `SceneBootstrapper` ensures Bootstrap scene first for NGO prefab hashing.

## Networking Stack

* **ConnectionManager**: runtime NGO state machine; handles connection events and approval; states under `ConnectionState/*` (`Offline`, `ClientConnecting/Connected/Reconnecting`, `StartingHost`, `Hosting`, `Online`).
* **Payload**: `ConnectionPayload` JSON on `NetworkConfig.ConnectionData` (playerId/name/isDebug).
* **Transports**: `ConnectionMethodIP` (UTP), `ConnectionMethodRelay` (UGS Relay via `MultiplayerServicesFacade`), including reconnection.
* **UGS**: `MultiplayerServicesFacade` encapsulates Sign-In, Session create/join, Relay data, reconnection.

## Scenes and Game State

* **Base**: `GameStateBehaviour` (one active per side; optional persistence).
* **ServerBossRoomState**: spawns players, mirrors avatar/name via `NetworkAvatarGuidState`/`NetworkNameState`, listens to `LifeStateChangedEventMessage`, transitions to `PostGame` via `SceneLoaderWrapper`.
* **Shared**: `PersistentGameState` holds win/loss across scenes.
* **Flow**: `MainMenu → CharSelect → BossRoom → PostGame` (server initiates; clients sync visuals/physics).

## Player Avatar & Controllers

* **ServerCharacter**: authoritative state (Movement/HeldObject/Stealth/Target; Health/Life), RPCs (`ServerSendCharacterInputRpc`, `ServerPlayActionRpc`, `ServerStopChargingUpRpc`), integrates Movement/ActionPlayer/AI/Damage.
* **ClientCharacter**: receives play/cancel/stop-charge RPCs, smooths motion, drives Animator via `VisualizationConfiguration`, attaches `CameraController` for local owner, subscribes to `ClientInputSender`.
* **ClientInputSender**: Input System actions, context-sensitive Action1, raycasts vs PCs/NPCs/Ground, builds `ActionRequestData`, throttles movement sends, validates against NavMesh.
* **ServerCharacterMovement**: NavMeshAgent; modes PathFollowing/Charging/Knockback; updates `MovementStatus`; helpers for Follow/Teleport/forced movement.

## Action System (Abilities/FX/Combat)

* **Base**: `Action` (SO) + `ActionConfig` (range/timing/blocking/anim/projectile/spawn).
* **Lifecycle**: Start → Update → End/Cancel; optional chaining; buffs via `BuffValue`. Client mirrors via `OnStartClient/OnUpdateClient/EndClient/CancelClient`.
* **Players**: `ServerActionPlayer` (queue/reuse/cancel/chase/updates), `ClientActionPlayer` (anticipation/FX).
* **Data**: `GameDataSource` registers actions and assigns stable `ActionID` used in RPCs.
* **Concrete**: under `Gameplay/Action/ConcreteActions/*`.
* **Extend**: Create Action SO (+ optional logic), add to `GameDataSource`, bind to `CharacterClass`, wire input if player-driven, add VFX.

## AI

* **AIBrain** (state machine, hate list, best eligible state). States like `IdleAIState`, `AttackAIState` implement `AIState` (`IsEligible/Initialize/Update`), execute via `ServerActionPlayer`. Tune detection via `CharacterClass.DetectRange` or overrides; `IsAppropriateFoe` filters targets.

## Interactables & World

* **Pickups**: `PickUpState` (`ITargetable`), client FX in `ClientPickUpPotEffects`.
* **Switches/Doors**: `FloorSwitch` sets `IsSwitchedOn`; `SwitchedDoor` observes switches, toggles physics/animator, publishes `DoorStateChangedEventMessage`.
* **Damage**: `IDamageable` + `DamageReceiver` route collisions/damage.
* **Projectiles**: `Gameplay/GameplayObjects/Projectiles/*` with pooled spawning.

## Navigation

* `NavigationSystem` + `DynamicNavPath` (authoring/runtime helpers, dynamic obstacles).
* Movement is server-side; clients render synced transforms.

## Object Pooling (Networked)

* `NetworkObjectPool`: prefab list + prewarm; registers `INetworkPrefabInstanceHandler` for server/clients; used for projectiles/FX (extend as needed).

## Messaging (Pub/Sub)

* In-process: `MessageChannel<T>`, `BufferedMessageChannel<T>` via `IPublisher<T>`/`ISubscriber<T>`.
* Networked: `NetworkedMessageChannel<T>` (server→client).
* Examples: `DoorStateChangedEventMessage`, `LifeStateChangedEventMessage`, `ReconnectMessage`.

## Data & Configuration

* **Avatars**: `Avatar.cs`, `AvatarRegistry.cs` map visuals/portrait to `CharacterClass`.
* **Classes**: `CharacterClass.cs` (base HP/speed/skills/detect range).
* **Visualization**: `VisualizationConfiguration.cs` (anim IDs/materials).
* **Authoring**: action prototypes & bindings in `Assets/GameData/...`.

## Typical End-to-End Flows

* **Movement (client→server)**: `ClientInputSender` raycasts/samples NavMesh → `ServerSendCharacterInputRpc(position)` → `ServerCharacterMovement` sets path in `FixedUpdate` and updates `MovementStatus` → `ClientCharacter` lerps/animates.
* **Ability (client→server→all)**: `ClientInputSender` builds `ActionRequestData` → `ServerActionPlayer` queues/synthesizes Target/Chase/updates → server broadcasts client RPCs (play/cancel/stop-charge) for FX.
* **AI Attack (server)**: `AIBrain` selects `AttackAIState` → uses `ServerActionPlayer` → damage/heal feed into `ServerCharacter.ReceiveHP()`; buffs via active actions.

## Extending the Project

* **New Ability**: Action SO (+ optional logic), add to `GameDataSource`, bind to `CharacterClass`, wire input if player-driven, add FX.
* **Interactable**: Networked MonoBehaviour; implement `ITargetable` (and `IDamageable` if needed); drive state via `NetworkVariable`s.
* **AI Behavior**: Implement `AIState`, add to `AIBrain` map; drive via `ServerActionPlayer`.
* **New Networked Object**: Prefab with `NetworkObject`; add to Network Prefabs; register in `NetworkObjectPool` if frequently spawned.
* **Scene/State**: Place the correct `GameStateBehaviour`; trigger transitions via `SceneLoaderWrapper.Instance.LoadScene(..., useNetworkSceneManager: true)`.

## Conventions & Gotchas

* Server authority for gameplay state; clients visualize/request; server validates.
* Limit action queue blocking time.
* Most actions anticipate locally; ensure animations tolerate start/cancel.
* Sessions+Relay support reconnection during grace period.
* Layers: input uses `PCs`/`NPCs`/`Ground`; set layers on new interactables.
* NavMesh: sample on mesh; handle moving colliders with navmesh obstacles.

## Key Files Index (jumping-off points)

* **Networking**: `ConnectionManager.cs`, `ConnectionMethod.cs`, `ConnectionState/*`
* **Game State & Scenes**: `Gameplay/GameState/*`
* **Characters & Movement**: `ServerCharacter.cs`, `ClientCharacter.cs`, `ServerCharacterMovement.cs`
* **Input & Camera**: `ClientInputSender.cs`, `CameraController.cs`
* **Actions**: `Action.cs`, `ActionConfig.cs`, `ActionPlayers/*`, `ConcreteActions/*`
* **AI**: `Gameplay/GameplayObjects/Character/AI/*`
* **World**: `Gameplay/GameplayObjects/*` (switches, doors, pickups, projectiles)
* **Config**: `Gameplay/Configuration/*`, `GameDataSource.cs`
* **Infrastructure**: `NetworkObjectPool.cs`, `PubSub/*`

If you want, I can tailor this guide with diagrams or link specific prefabs/scenes in your repo. Let me know what area you’d like deeper notes on (e.g., Action authoring, AI state design, or Sessions/Relay setup).

## Script Purpose Index (by folder, highlights)

*One-line purpose per representative script; similarly named peers follow the same pattern.*

**ApplicationLifecycle**

* `ApplicationController.cs`: Root composition (DI/services/message channels), loads MainMenu.
* `Messages/QuitApplicationMessage.cs`: Request app quit.

**Audio**

* `AudioMixerConfigurator.cs`: Applies runtime mixer settings.
* `ClientMusicPlayer.cs`: Client music playback/switching.

**CameraUtils**

* `CameraController.cs`: Local third-person follow/smoothing.

**ConnectionManagement**

* `ConnectionManager.cs`: NGO connection state machine.
* `ConnectionMethod.cs`: Transport strategies (IP/Relay) + payload/reconnect.
* `SessionPlayerData.cs`: Per-client session data.
* `ConnectionState/*`: Concrete connection state handlers.

**Editor**

* `BakingMenu.cs`, `BuildHelpers.cs`: Build/bake helpers.
* `SceneBootstrapper.cs`: Ensures Bootstrap scene for NGO hashing.

**Gameplay/Action (core)**

* `Action.cs` / `ActionConfig.cs` / `ActionFactory.cs` / `ActionID.cs` / `ActionUtils.cs` / `BlockingModeType.cs`
* `ActionPlayers/ClientActionPlayer.cs` / `ActionPlayers/ServerActionPlayer.cs`

**Gameplay/Action/Input**

* `ActionRequestData.cs`, `BaseActionInput.cs`, `ChargedActionInput.cs`, `AoeActionInput.cs`, `ActionLogic.cs`

**Gameplay/Action/ConcreteActions (sample)**

* `AOEAction.cs`, `ChaseAction.cs`, `DropAction.cs`, `EmoteAction.cs`, `FXProjectileTargetedAction(.Client).cs`, `LaunchProjectileAction.cs`, `MeleeAction(.Client).cs`, `PickUpAction.cs`, `ReviveAction.cs`, `StealthModeAction.cs`, `StunnedAction.cs`, `TargetAction(.Client).cs`, `TossAction.cs`, `DashAttackAction.cs`, `Charged*Action(.Client).cs`, `TrampleAction(.Client).cs`, `ProjectileInfo.cs`

**Gameplay/Configuration**

* `Avatar.cs`, `AvatarRegistry.cs`, `CharacterClass.cs`, `NameGenerationData.cs`, `VisualizationConfiguration.cs`

**Gameplay/DebugCheats**

* `DebugCheatsManager.cs`: Dev cheats (speed/teleport, etc.).

**Gameplay/GameplayObjects/AnimationCallbacks**

* `AnimatorFootstepSounds.cs`, `AnimatorNodeHook.cs`, `AnimatorTriggeredSpecialFX.cs`, `BossDeathHelmetHandler.cs`

**Gameplay/GameplayObjects/Audio**

* `BossMusicStarter.cs`, `MainMenuMusicStarter.cs`

**Gameplay/GameplayObjects (world/entities)**

* `Breakable.cs`, `EnemyPortal.cs`, `FloorSwitch.cs`, `SwitchedDoor.cs`, `TossedItem.cs`, `PublishMessageOnLifeChange.cs`, `ServerWaveSpawner.cs`, `ServerDisplacerOnParentChange.cs`, `ClientPickUpPotEffects.cs`, `PickUpState.cs`

**Gameplay/GameplayObjects/Character (core)**

* `ServerCharacter.cs`, `ServerCharacterMovement.cs`, `ClientCharacter.cs`, `ClientPlayerAvatar.cs`, `ClientPlayerAvatarNetworkAnimator.cs`, `NetworkAvatarGuidState.cs`, `NetworkHealthState.cs`, `NetworkLifeState.cs`, `MovementStatus.cs`, `CharacterSwap.cs`, `CharacterTypeEnum.cs`, `PhysicsWrapper.cs`, `ServerAnimationHandler.cs`, `PlayerServerCharacter.cs`

**Gameplay/GameplayObjects/Projectiles**

* `PhysicsProjectile.cs`, `FXProjectile.cs`

**Gameplay/GameplayObjects/RuntimeDataContainers**

* `GameDataSource.cs`, `PersistentPlayerRuntimeCollection.cs`, `ClientPlayerAvatarRuntimeCollection.cs`

**Gameplay/GameplayObjects (targeting & damage)**

* `IDamageable.cs`, `ITargetable.cs`, `DamageReceiver.cs`

**Gameplay/GameState**

* `GameStateBehaviour.cs`, `ServerBossRoomState.cs`, `ServerCharSelectState.cs`, `ClientCharSelectState.cs`, `ClientMainMenuState.cs`, `NetworkCharSelection.cs`, `NetworkPostGame.cs`, `ServerPostGameState.cs`, `PersistentGameState.cs`

**Gameplay/Messages**

* Payloads for Pub/Sub (e.g., `LifeStateChangedEventMessage`, UI/events).

**Gameplay/UI (selected)**

* `Ability_UI_Base.cs` (+ peers), `AllyHUD.cs`, `PartyHUD.cs`, `CharacterSelect*` / `UICharSelect*`, `UIHealth.cs`, `UIName.cs`, `UIMessageFeed.cs`, `PopupPanel.cs`, `PopupManager.cs`, `UnityServicesUIHandler.cs`, `UIStateDisplay*.cs`, `UI*Settings*.cs`

**Gameplay/UserInput**

* `ClientInputSender.cs`

**Infrastructure**

* `NetworkObjectPool.cs`, `DisposableGroup.cs`, `NetworkGuid.cs`, PubSub (`IMessageChannel` et al.), `UpdateRunner.cs`, ScriptableObjectArchitecture/\*, network overlays (`NetworkOverlay.cs`, `NetworkLatencyWarning.cs`, `NetworkStats.cs`), `NetworkSimulatorUIMediator.cs`

**Navigation**

* `NavigationSystem.cs`, `DynamicNavPath.cs`

**UnityServices**

* `Auth/AuthenticationServiceFacade.cs`, `Sessions/MultiplayerServicesFacade.cs`, `Sessions/LocalSession(User).cs`, `Infrastructure/Messages/UnityServiceErrorMessage.cs`, `Infrastructure/RateLimitCooldown.cs`

**Utils**

* `ClientPrefs.cs`, `ProfileManager.cs`, `NetworkNameState.cs`, `PositionLerper.cs`, `RotationLerper.cs`, `PrefabSpawner.cs`, `EnableOrDisableColliderOnAwake.cs`, `SelfDisable.cs`, `TimedSelfDestruct.cs`

**VisualEffects**

* `SpecialFXGraphic.cs`, `RandomizedLight.cs`, `ScrollingMaterialUVs.cs`

## Base Prefabs and How To Reuse

* **Bootstrap**

  * `NetworkingManager.prefab` (NGO `NetworkManager` + transport)
  * `NetworkObjectPool.prefab` (global pooled spawning)
  * `SceneLoader.prefab` (NGO scene loads)

* **State prefabs (per scene)**

  * `MainMenuState.prefab`, `CharSelectState.prefab`, `BossRoomState.prefab`, `PostGameState.prefab`

* **Characters**

  * Player: `Character/PlayerAvatar.prefab` (server+client comps). Duplicate for archetypes; bind `CharacterClass` + `Avatar`.
  * Persistent: `Character/PersistentPlayer.prefab` (lives across scenes; `NetworkNameState`, `NetworkAvatarGuidState`).
  * Enemy: `Character/Enemy.prefab` or examples (`Imp.prefab`, `ImpBoss.prefab`).

* **Visuals**

  * Reticle/ground click FX under `Assets/VFX/Prefabs/UI/*` and `Assets/Prefabs/UI/*`.
  * Character graphics sets: `Assets/Prefabs/CharGFX/*` used by `CharacterSwap`/avatar assets.

Recommended reuse

* Duplicate closest prefab (Player/Enemy); keep `NetworkObject`, `ServerCharacter`, `ClientCharacter`, `ServerCharacterMovement`, `PhysicsWrapper`, `ClientPlayerAvatarNetworkAnimator`, Animator.
* Create new `CharacterClass` and, for players, an `Avatar` asset.
* Bind abilities via `CharacterClass.Skill1/2/3` to `GameDataSource` actions.
* Add prefab to Network Prefabs and, if frequently spawned, to `NetworkObjectPool`.

## Character Composition & Inheritance Notes

* Composition over inheritance: behavior via `ServerCharacter`/`ServerCharacterMovement`/`ClientCharacter` + data (`CharacterClass`) + Actions.
* Physics root vs graphics child (`PhysicsWrapper.Transform`); `ClientCharacter` updates visuals from server state.
* `NetworkAvatarGuidState` links player selection (GUID) to `Avatar` (graphics + class).
* `CharacterClass.IsNpc` controls AI instantiation (`AIBrain`) on spawn.

## AI: How to Adjust or Extend

* **Add behavior**: new `AIState` (`IsEligible/Initialize/Update`), register in `AIBrain`, trigger abilities with `ServerActionPlayer`.
* **Tune**: adjust `DetectRange` (class or override), extend `IsAppropriateFoe` for factions/stealth.
* **React**: use `AIBrain.ReceiveHP()` (called from `ServerCharacter.ReceiveHP()`).
* **Pathing**: `FollowTransform()` for chase; use Charging/Knockback for specials.

## Using This As a Base Project

* **Start**: Configure `NetworkingManager` (Relay/IP), include `NetworkObjectPool`, review/populate `GameDataSource`, bind skills in each `CharacterClass`, create/assign `Avatar` assets and hook Character Select.
* **Add content**: Abilities (SO + logic + FX + input), Enemies (duplicate `Enemy`, set `IsNpc`, pick Actions), Interactables (NetworkObject + `ITargetable`/`IDamageable`).
* **Networking checklist**: Add networked prefabs to NetworkManager; use `NetworkObjectPool` for frequent spawn/despawn to reduce GC spikes.
