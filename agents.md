# Unity RPG Project Overview (Agents Guide)

This document maps the core systems, flows, and extension points of this Unity RPG project (based on the Boss Room sample). Use it as a practical guide when adding features, debugging, or onboarding a new engineer.

## Architecture Overview

- Netcode: Server-authoritative using Netcode for GameObjects (NGO). Most simulation runs on server; clients send input RPCs and render state via NetworkVariables and client RPCs.
- Dependency Injection: VContainer used for composition, lifetime-scoped services, and message channels.
- Data-Driven Gameplay: ScriptableObjects define Avatars, CharacterClasses, and Actions (abilities). Runtime pulls from a central `GameDataSource`.
- Scenes & State: Discrete game states mapped to scenes; transitions flow via NGO SceneManager. Server and client each have a `GameStateBehaviour` instance.
- Input and Actions: Client gathers input → server validates → server-side action queue executes → client visualizes via action FX.
- AI & Interactables: Simple server-side AI state machine; interactables modeled as networked behaviours with clear interfaces.

## Entry Point & Composition

- `Assets/Scripts/ApplicationLifecycle/ApplicationController.cs`
  - Registers DI singletons and message channels.
  - Persists key systems across scenes: `ConnectionManager`, `NetworkManager`, `UpdateRunner`, `LocalSession(User)`, `PersistentGameState`.
  - Initializes Unity Services surface (Authentication, Sessions/Relay via `MultiplayerServicesFacade`).
  - Loads `MainMenu` scene on startup.

- Editor bootstrap: `Assets/Scripts/Editor/SceneBootstrapper.cs`
  - Ensures the Bootstrap scene loads first when entering Play Mode so NGO Prefab hashes are valid in the editor.

## Networking Stack

- Core manager: `Assets/Scripts/ConnectionManagement/ConnectionManager.cs`
  - A runtime state machine over NGO: binds `OnConnectionEvent`, `OnServerStarted`, `OnServerStopped`, `OnTransportFailure`, and `ConnectionApprovalCallback`.
  - States in `Assets/Scripts/ConnectionManagement/ConnectionState/`:
    - `OfflineState`, `ClientConnectingState`, `ClientConnectedState`, `ClientReconnectingState`, `StartingHostState`, `HostingState`, `OnlineState`.
  - Connection payload: `ConnectionPayload` JSON set on `NetworkConfig.ConnectionData` for playerId, playerName, isDebug.

- Transports / Methods: `Assets/Scripts/ConnectionManagement/ConnectionMethod.cs`
  - `ConnectionMethodIP`: Direct UTP IP/port.
  - `ConnectionMethodRelay`: UGS Relay via `MultiplayerServicesFacade` and Sessions; includes reconnection path.

- Services integration:
  - `Assets/Scripts/UnityServices/Sessions/MultiplayerServicesFacade.cs` (and related) encapsulate Signing In, Session create/join, Relay data, reconnection.

## Scenes and Game State

- Base: `Assets/Scripts/Gameplay/GameState/GameStateBehaviour.cs`
  - One active instance per side; can persist across loads; server drives transitions via NGO SceneManager.

- Server gameplay: `Assets/Scripts/Gameplay/GameState/ServerBossRoomState.cs`
  - Spawns player avatars on load/late-join at spawn points.
  - Mirrors persistent player data (avatar GUID, name) onto the spawned `ServerCharacter` via `NetworkAvatarGuidState`/`NetworkNameState`.
  - Listens to `LifeStateChangedEventMessage` to detect win/loss conditions; transitions to `PostGame` via `SceneLoaderWrapper` (from the utilities package).

- Shared session win/loss: `Assets/Scripts/Gameplay/GameState/PersistentGameState.cs`.

Typical flow:
1) MainMenu → CharSelect → BossRoom → PostGame.
2) Scene transitions are initiated server-side; client visuals and physics state sync via NGO.

## Player Avatar & Controllers

- Server authority: `Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacter.cs`
  - NetworkVariables: `MovementStatus`, `HeldNetworkObject`, `IsStealthy`, `TargetId`; reads/writes `NetworkHealthState` and `NetworkLifeState`.
  - RPCs:
    - `ServerSendCharacterInputRpc(Vector3)`: server-side movement target.
    - `ServerPlayActionRpc(ActionRequestData)`: server action request.
    - `ServerStopChargingUpRpc()`: finalize charge actions.
  - Integrates `ServerCharacterMovement` (NavMesh + forced movement), `ServerActionPlayer` (action queue), `AIBrain` (for NPCs), and `DamageReceiver`.

- Client visualization: `Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs`
  - Receives server broadcasts to play/cancel actions and end charge (`ClientPlayActionRpc`, etc.).
  - Smooths visuals (lerped position/rotation on host) and drives Animator via `VisualizationConfiguration`.
  - For local owner: attaches `CameraController`, starts Target reticle action, subscribes to inputs from `ClientInputSender`.

- Input capture: `Assets/Scripts/Gameplay/UserInput/ClientInputSender.cs`
  - Uses Input System actions for move/target/abilities.
  - Context-sensitive Action1 (e.g., revive ally, pick up, drop) via `UpdateAction1()`.
  - Performs raycasts vs PCs/NPCs/Ground; packages `ActionRequestData` and sends to server.
  - Movement throttled (`k_MoveSendRateSeconds`) and validated against NavMesh.

- Movement: `Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacterMovement.cs`
  - Server-only; NavMeshAgent driven with `DynamicNavPath` and `NavigationSystem`.
  - Modes: PathFollowing, Charging, Knockback; updates `MovementStatus` for client animation.
  - Provides `FollowTransform`, `Teleport`, and forced-movement helpers.

## Action System (Abilities, FX, Combat)

- Base type: `Assets/Scripts/Gameplay/Action/Action.cs` (ScriptableObject)
  - Configured via `ActionConfig` (range, timings, blocking mode, animation triggers, projectile/spawn data, etc.).
  - Life-cycle: OnStart → per-frame Update → End/Cancel; may chain into new action; can contribute buffs/debuffs via `BuffValue`.
  - Client visualization lifecycle mirrors server via `OnStartClient`/`OnUpdateClient`/`EndClient`/`CancelClient`.

- Runtime players:
  - Server: `ServerActionPlayer` orchestrates queueing, reuse cooldowns, target/chase synthesis, cancellation, non-blocking actions, and per-frame updates.
  - Client: `ClientActionPlayer` handles anticipated actions and FX playback (see ClientCharacter).

- Data source: `Assets/Scripts/Gameplay/GameplayObjects/RuntimeDataContainers/GameDataSource.cs`
  - Registers common actions (target, chase, emotes, revive, stunned, pickup/drop) and the project’s action prototypes.
  - Assigns stable `ActionID`s at runtime; lookup by ID used in RPCs.

- Concrete actions live under `Assets/Scripts/Gameplay/Action/ConcreteActions/`.

Extension pattern:
- New ability = create Action ScriptableObject + optionally new Action logic class + hook data into `GameDataSource` + wire input (if player-driven) + add VFX.

## AI

- `Assets/Scripts/Gameplay/GameplayObjects/Character/AI/`
  - `AIBrain`: lightweight state machine; maintains hated enemies; selects best eligible state.
  - States: `IdleAIState`, `AttackAIState` implementing `AIState` (IsEligible/Initialize/Update).
  - Executes actions via the owning `ServerActionPlayer` and uses CharacterClass data (e.g., DetectRange).

## Interactables & World Objects

- Targetable pickups: `PickUpState` implements `ITargetable`.
- Floor switches: `FloorSwitch` server-detects colliders in trigger, writes `IsSwitchedOn`; animator sync on clients.
- Doors: `SwitchedDoor` observes linked switches; toggles physics object, animator, and publishes `DoorStateChangedEventMessage`.
- Damage system: `IDamageable` interface for anything that can receive damage; `DamageReceiver` routes collisions and damage to characters and objects.
- Projectiles: see `Gameplay/GameplayObjects/Projectiles/*` and pooled spawning.

## Navigation

- `Assets/Scripts/Navigation/NavigationSystem.cs` and `DynamicNavPath.cs` handle authoring and runtime updates (e.g., for dynamic obstacles).
- Movement is server-side only; clients render based on synced transforms.

## Object Pooling (Networked)

- `Assets/Scripts/Infrastructure/NetworkObjectPool.cs`
  - Pool configured via a list of prefabs and prewarm counts.
  - Registers `INetworkPrefabInstanceHandler` to spawn/return pooled instances across server and clients.
  - Used primarily for projectiles; extend list to include other frequently spawned objects.

## Messaging (Pub/Sub)

- `Assets/Scripts/Infrastructure/PubSub/*`
  - In-process channels: `MessageChannel<T>`, `BufferedMessageChannel<T>` via `IPublisher<T>`/`ISubscriber<T>`.
  - Networked channels: `NetworkedMessageChannel<T>` for server→client broadcasts (e.g., life state, connection events).
  - Examples: `DoorStateChangedEventMessage`, `LifeStateChangedEventMessage`, `ReconnectMessage`.

## Data & Configuration

- Avatars: `Gameplay/Configuration/Avatar.cs` and `AvatarRegistry.cs` map character visuals and portrait to a `CharacterClass`.
- Character classes: `Gameplay/Configuration/CharacterClass.cs` parameters (base HP, speed, skills, detect range, etc.).
- Visualization: `Gameplay/Configuration/VisualizationConfiguration.cs` drives animator variables and reticule materials.
- Action prototypes and character/class skill bindings are authored in `Assets/GameData/...`.

## Typical End-to-End Flows

Movement (client→server):
1) `ClientInputSender` raycasts ground and samples NavMesh.
2) Sends `ServerSendCharacterInputRpc(hit.position)`.
3) `ServerCharacterMovement` sets path and moves in `FixedUpdate`; updates `MovementStatus`.
4) `ClientCharacter` lerps visuals and animator based on `MovementStatus`.

Ability (client→server→all):
1) `ClientInputSender` builds `ActionRequestData` (context-sensitive target/chase flags) and sends `ServerPlayActionRpc`.
2) `ServerActionPlayer` queues, optionally synthesizes `Target`/`Chase`, starts and updates action lifecycle.
3) Server broadcasts `ClientPlayActionRpc` and other RPCs (cancel, stop-charge) to clients for FX.

AI Attack (server):
1) `AIBrain` picks `AttackAIState`, uses `ServerActionPlayer` to perform ability.
2) Damage/heal events feed back to `ServerCharacter.ReceiveHP()`; buffs applied by active actions via `GetBuffedValue`.

## Extending the Project

- Add a New Ability
  - Create a new `Action` ScriptableObject and configure `ActionConfig` (range, duration, blocking mode, animations, spawns/projectiles).
  - Implement a new Action subclass if needed (server logic; optional client FX overrides).
  - Add your prototype to `GameDataSource` and bind to `CharacterClass` skills if it’s a player ability.
  - Add input binding in `ClientInputSender` if it’s a new input path.

- Add an Interactable
  - Create a networked MonoBehaviour; implement `ITargetable` (and `IDamageable` if applicable).
  - Drive state via `NetworkVariable`s; for physics interactions, colliders/triggers on server and client anims.

- Add an AI Behavior
  - Create a new `AIState` and add to `AIBrain`’s state map; gate via `IsEligible()` and implement `Initialize/Update`.
  - Use `ServerActionPlayer` to request actions.

- New Networked Object Type
  - Create prefab with `NetworkObject` and required components.
  - If frequently spawned/despawned, register in `NetworkObjectPool` to reduce allocations.
  - Add to `NetworkManager`’s Network Prefabs list.

- Scene or State Changes
  - Create the scene and place the appropriate `GameStateBehaviour` variant.
  - Drive transitions server-side via the SceneManager (`SceneLoaderWrapper.Instance.LoadScene(..., useNetworkSceneManager: true)`).

## Conventions & Gotchas

- Server Authority: All gameplay state changes (HP, life state, movement, actions) happen on server. Clients visualize and request; server validates.
- Action Queue Depth: Server limits total blocking time in queue to prevent unbounded growth.
- Anticipation: Most actions anticipate on local client; Target action doesn’t. Ensure animations are resilient to anticipated start/cancel.
- Reconnects: With Sessions+Relay, reconnection is supported during the configured grace period.
- Layers & Raycasts: Input uses `PCs`, `NPCs`, `Ground` masks. Ensure new interactables use proper layers.
- NavMesh Sampling: Movement uses sampled point on NavMesh; moving colliders need navmesh obstacle handling.

## Key Files Index (jumping-off points)

- Networking
  - `Assets/Scripts/ConnectionManagement/ConnectionManager.cs`
  - `Assets/Scripts/ConnectionManagement/ConnectionMethod.cs`
  - `Assets/Scripts/ConnectionManagement/ConnectionState/*`
- Game State & Scenes
  - `Assets/Scripts/Gameplay/GameState/*`
- Characters & Movement
  - `Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacter.cs`
  - `Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs`
  - `Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacterMovement.cs`
- Input & Camera
  - `Assets/Scripts/Gameplay/UserInput/ClientInputSender.cs`
  - `Assets/Scripts/CameraUtils/CameraController.cs`
- Actions
  - `Assets/Scripts/Gameplay/Action/Action.cs`
  - `Assets/Scripts/Gameplay/Action/ActionConfig.cs`
  - `Assets/Scripts/Gameplay/Action/ActionPlayers/*`
  - `Assets/Scripts/Gameplay/Action/ConcreteActions/*`
- AI
  - `Assets/Scripts/Gameplay/GameplayObjects/Character/AI/*`
- Interactables & World
  - `Assets/Scripts/Gameplay/GameplayObjects/*` (switches, doors, pick-ups, projectiles)
- Data & Configuration
  - `Assets/Scripts/Gameplay/Configuration/*`
  - `Assets/Scripts/Gameplay/GameplayObjects/RuntimeDataContainers/GameDataSource.cs`
- Infrastructure
  - `Assets/Scripts/Infrastructure/NetworkObjectPool.cs`
  - `Assets/Scripts/Infrastructure/PubSub/*`

If you want, I can tailor this guide with diagrams or link specific prefabs/scenes in your repo. Let me know what area you’d like deeper notes on (e.g., Action authoring, AI state design, or Sessions/Relay setup).

## Script Purpose Index (by folder)

Note: One-line purpose per script to help you find the right place to change. This is descriptive, not a logic dump.

ApplicationLifecycle
- `Assets/Scripts/ApplicationLifecycle/ApplicationController.cs`: Root composition; registers DI services, message channels, and loads MainMenu.
- `Assets/Scripts/ApplicationLifecycle/Messages/QuitApplicationMessage.cs`: Message type to request app quit.

Audio
- `Assets/Scripts/Audio/AudioMixerConfigurator.cs`: Applies runtime mixer settings (volumes, groups) at startup.
- `Assets/Scripts/Audio/ClientMusicPlayer.cs`: Handles music playback and switching on client.

CameraUtils
- `Assets/Scripts/CameraUtils/CameraController.cs`: Third‑person camera follow and smoothing for local player.

ConnectionManagement
- `Assets/Scripts/ConnectionManagement/ConnectionManager.cs`: NGO connection state machine; wires callbacks and delegates to states.
- `Assets/Scripts/ConnectionManagement/ConnectionMethod.cs`: Strategies to configure transport (IP/Relay) and payload; reconnection support.
- `Assets/Scripts/ConnectionManagement/SessionPlayerData.cs`: Stores/restores per‑client session data (e.g., HP, position) for respawn/rejoin.
- `Assets/Scripts/ConnectionManagement/ConnectionState/ConnectionState.cs`: Abstract base for connection state handlers.
- `Assets/Scripts/ConnectionManagement/ConnectionState/OfflineState.cs`: Configures and transitions into client/host connect flows.
- `Assets/Scripts/ConnectionManagement/ConnectionState/ClientConnectingState.cs`: Kicks off client connect and handles callbacks.
- `Assets/Scripts/ConnectionManagement/ConnectionState/ClientConnectedState.cs`: Client connected steady‑state behaviors.
- `Assets/Scripts/ConnectionManagement/ConnectionState/ClientReconnectingState.cs`: Reconnect attempts (Relay sessions flow).
- `Assets/Scripts/ConnectionManagement/ConnectionState/StartingHostState.cs`: Prepares and starts host.
- `Assets/Scripts/ConnectionManagement/ConnectionState/HostingState.cs`: Host steady‑state; approvals and disconnect events.
- `Assets/Scripts/ConnectionManagement/ConnectionState/OnlineState.cs`: Shared logic used by online states.

Editor
- `Assets/Scripts/Editor/BakingMenu.cs`: Editor menu entries for baking/build helpers.
- `Assets/Scripts/Editor/BuildHelpers.cs`: Build‑time utilities (defines, versioning, etc.).
- `Assets/Scripts/Editor/SceneBootstrapper.cs`: Ensures Bootstrap scene is used entering play mode for NGO prefab hashing.

Gameplay/Action (core)
- `Assets/Scripts/Gameplay/Action/Action.cs`: Base ScriptableObject for abilities/action lifecycle (server + client hooks).
- `Assets/Scripts/Gameplay/Action/ActionConfig.cs`: Data for actions (timings, range, animations, projectiles, spawns, blocking).
- `Assets/Scripts/Gameplay/Action/ActionFactory.cs`: Pools/creates Action instances from `ActionRequestData`.
- `Assets/Scripts/Gameplay/Action/ActionID.cs`: Stable ID struct for networkable action identity.
- `Assets/Scripts/Gameplay/Action/ActionUtils.cs`: Helpers like target validation and action utilities.
- `Assets/Scripts/Gameplay/Action/BlockingModeType.cs`: Enum describing how actions block.
- `Assets/Scripts/Gameplay/Action/ActionPlayers/ClientActionPlayer.cs`: Client‑side FX/anticipation playback for actions.
- `Assets/Scripts/Gameplay/Action/ActionPlayers/ServerActionPlayer.cs`: Server queue/execute/update/cancel of actions.

Gameplay/Action/Input
- `ActionLogic.cs`: Enum of logic kinds used by concrete actions.
- `ActionRequestData.cs`: Network payload describing a requested action (IDs, targets, flags).
- `BaseActionInput.cs`: Base component for actions that require special multi‑frame input.
- `ChargedActionInput.cs`: Input handler for “hold to charge” actions.
- `AoeActionInput.cs`: Input handler for AOE targeting.

Gameplay/Action/ConcreteActions (selected)
- `AOEAction.cs`: Server logic for area‑of‑effect ability.
- `ChaseAction.cs`: Server “close distance to target” synthesized by server when needed.
- `DropAction.cs`: Server logic to drop held item.
- `EmoteAction.cs`: Emote action (non‑combat, client anim triggers).
- `FXProjectileTargetedAction(.Client).cs`: Targeted FX ability with client visualization.
- `LaunchProjectileAction.cs`: Launches projectile(s) toward direction/target.
- `MeleeAction(.Client).cs`: Melee ability server+client.
- `PickUpAction.cs`: Server logic to pick up item.
- `ReviveAction.cs`: Revives fainted ally.
- `StealthModeAction.cs`: Toggles stealth state.
- `StunnedAction.cs`: Applies stunned effect.
- `TargetAction(.Client).cs`: Sets/updates active target (also used for selection reticle).
- `TossAction.cs`: Throws an object.
- `DashAttackAction.cs`: Short teleport/charge dash plus hit logic.
- `ChargedLaunchProjectileAction(.Client).cs`: Charged projectile variant.
- `ChargedShieldAction(.Client).cs`: Charged defensive/absorbing ability.
- `TrampleAction(.Client).cs`: Movement‑based attack (knockback/stun chance).
- `ProjectileInfo.cs`: Data describing projectile variants per action.

Gameplay/Configuration
- `Avatar.cs`: Avatar asset linking CharacterClass, visuals, and portrait.
- `AvatarRegistry.cs`: Registry for available avatars in menus/selection.
- `CharacterClass.cs`: Stats and abilities for a class; used by ServerCharacter.
- `NameGenerationData.cs`: Data set to randomize player names.
- `VisualizationConfiguration.cs`: Animator IDs/materials for per‑class visual tuning.

Gameplay/DebugCheats
- `DebugCheatsManager.cs`: Toggles dev cheats (speed, teleport, etc.) in development.

Gameplay/GameplayObjects/AnimationCallbacks
- `AnimatorFootstepSounds.cs`: Plays SFX on footstep animation events.
- `AnimatorNodeHook.cs`: Forwards animation state events to code.
- `AnimatorTriggeredSpecialFX.cs`: Triggers VFX from animation events.
- `BossDeathHelmetHandler.cs`: Controls boss death helmet VFX.

Gameplay/GameplayObjects/Audio
- `BossMusicStarter.cs`: Starts boss music on entry.
- `MainMenuMusicStarter.cs`: Starts main menu music.

Gameplay/GameplayObjects (world and entities)
- `Breakable.cs`: Server‑side health/destroy logic for breakables.
- `EnemyPortal.cs`: Spawns enemies via waves/conditions.
- `FloorSwitch.cs`: Networked pressure plate with trigger detection.
- `SwitchedDoor.cs`: Networked door that opens when linked switches are on.
- `TossedItem.cs`: Networked item that can be thrown/picked up.
- `PublishMessageOnLifeChange.cs`: Publishes message when life state changes.
- `ServerWaveSpawner.cs`: Spawns enemy waves server‑side.
- `ServerDisplacerOnParentChange.cs`: Repositions GameObject when parent changes.
- `ClientPickUpPotEffects.cs`: Client VFX for pickup.
- `PickUpState.cs`: Targetable marker for pickup items.

Gameplay/GameplayObjects/Character (core)
- `ServerCharacter.cs`: Server authority for a character; HP/life/target/action/movement/AI.
- `ServerCharacterMovement.cs`: Server movement using NavMesh; forced movement modes.
- `ClientCharacter.cs`: Client visualization/animator and FX; reads NetworkVariables.
- `ClientPlayerAvatar.cs`: Client component specific to player avatar visuals.
- `ClientPlayerAvatarNetworkAnimator.cs`: Provides Animator for network syncing.
- `NetworkAvatarGuidState.cs`: Syncs avatar selection via GUID.
- `NetworkHealthState.cs`: NetworkVariable for current/max HP.
- `NetworkLifeState.cs`: NetworkVariable for life state (Alive/Fainted/Dead) and god mode flag.
- `MovementStatus.cs`: Enum for animation movement states.
- `CharacterSwap.cs`: Swaps character models/materials (e.g., stealth visuals).
- `CharacterTypeEnum.cs`: List of character archetypes (Tank/Mage/etc.).
- `PhysicsWrapper.cs`: Shared root transform for movement vs. graphics.
- `ServerAnimationHandler.cs`: Server network animator helpers (triggers on hits, etc.).
- `PlayerServerCharacter.cs`: Utility to fetch player‑owned ServerCharacters and helpers.

Gameplay/GameplayObjects/Projectiles
- `PhysicsProjectile.cs`: Kinematic/rigidbody projectile server logic.
- `FXProjectile.cs`: VFX‑driven projectile logic and despawn.

Gameplay/GameplayObjects/RuntimeDataContainers
- `GameDataSource.cs`: Central registry for actions and character data; assigns ActionIDs.
- `PersistentPlayerRuntimeCollection.cs`: Tracks persistent players.
- `ClientPlayerAvatarRuntimeCollection.cs`: Tracks client avatar visual instances.

Gameplay/GameplayObjects (targeting & damage)
- `IDamageable.cs`: Interface for anything that can take damage/heal.
- `ITargetable.cs`: Interface for selectable/targetable things.
- `DamageReceiver.cs`: Routes collisions/damage to `IDamageable` implementers.

Gameplay/GameState
- `GameStateBehaviour.cs`: Base state component; ensures one active per side; optional persistence across scenes.
- `ServerBossRoomState.cs`: Spawns players, tracks win/loss, scene transitions to PostGame.
- `ServerCharSelectState.cs`: Server logic for character select scene.
- `ClientCharSelectState.cs`: Client binding for character select UI/state.
- `ClientMainMenuState.cs`: Client main menu behaviors.
- `NetworkCharSelection.cs`: NGO bridge for selection sync between clients/server.
- `NetworkPostGame.cs`: NGO bridge for post‑game UI data.
- `ServerPostGameState.cs`: Server post‑game scene logic.
- `PersistentGameState.cs`: Holds win/loss; survives across scenes.

Gameplay/Messages
- Various message payloads (e.g., `LifeStateChangedEventMessage`, UI/event messages) used with pub/sub channels.

Gameplay/UI (selected)
- `Ability_UI_Base.cs` (and peers): Shared UI logic for ability bar items.
- `AllyHUD.cs`, `PartyHUD.cs`: Party/ally HUD bindings to NetworkVariables.
- `CharacterSelect*` and `UICharSelect*`: Character select screen components.
- `UIHealth.cs`, `UIName.cs`: Per‑entity HUD elements.
- `UIMessageFeed.cs`, `PopupPanel.cs`, `PopupManager.cs`: Message and popup UI flow.
- `UnityServicesUIHandler.cs`: UGS login/session UI glue.
- `UIStateDisplay*.cs`: Displays state messages (e.g., network/connect).
- `UI*Settings*.cs`: Client settings UI.
  (There are many small UI scripts; names reflect their panel/widget role.)

Gameplay/UserInput
- `ClientInputSender.cs`: Collects local input, raycasts, builds `ActionRequestData`, sends movement/action RPCs.

Infrastructure
- `NetworkObjectPool.cs`: Networked object pooling; prefab handlers for NGO spawn/return.
- `DisposableGroup.cs`: Helper to group `IDisposable` subscriptions.
- `NetworkGuid.cs`: Typed GUID used in network state (e.g., avatar selection).
- PubSub (`IMessageChannel` et al.): In‑process and networked message channels for decoupled events.
- ScriptableObjectArchitecture/*: Minimal SO utilities (events, variables, collections) used by data/config.
- `UpdateRunner.cs`: Dispatcher to run update loops for services.

Navigation
- `NavigationSystem.cs`: Centralized access/tag for navigation; authoring/runtime helpers.
- `DynamicNavPath.cs`: Computes and updates per‑agent paths with NavMeshAgent.

UnityServices
- `Auth/AuthenticationServiceFacade.cs`: Anonymous auth/login wrapper for UGS.
- `Sessions/MultiplayerServicesFacade.cs`: Session/Relay orchestration and tracking; reconnection helpers.
- `Sessions/LocalSession(User).cs`: Local mirror of current session and signed‑in user.
- `Infrastructure/Messages/UnityServiceErrorMessage.cs`: Error message payload for UGS issues.
- `Infrastructure/RateLimitCooldown.cs`: Small utility for throttling/cooldowns.

Utils
- `ClientPrefs.cs`: Stores client GUID/profile in PlayerPrefs.
- `ProfileManager.cs`: Active local profile management.
- `NetworkNameState.cs`: NetworkVariable for player name.
- `PositionLerper.cs`, `RotationLerper.cs`: Simple interpolation helpers.
- `PrefabSpawner.cs`: Utility to instantiate prefabs at runtime.
- `EnableOrDisableColliderOnAwake.cs`, `SelfDisable.cs`, `TimedSelfDestruct.cs`: Small lifecycle utilities.
- Network overlay: `NetworkOverlay.cs`, `NetworkLatencyWarning.cs`, `NetworkStats.cs` display network stats.
- `NetworkSimulatorUIMediator.cs`: UI controls for network simulation settings.

VisualEffects
- `SpecialFXGraphic.cs`: Marker/controller for spawned FX graphics lifetimes.
- `RandomizedLight.cs`, `ScrollingMaterialUVs.cs`: Simple VFX helpers.

## Base Prefabs and How To Reuse

- Core bootstrap
  - `Assets/Prefabs/NetworkingManager.prefab`: Drop into bootstrap scene; holds NGO `NetworkManager` and transport setup.
  - `Assets/Prefabs/NetworkObjectPool.prefab`: Global pooled spawning for networked projectiles/FX.
  - `Assets/Prefabs/SceneLoader.prefab`: Wrapper used by code to load scenes via NGO.

- State prefabs (one per scene)
  - `Assets/Prefabs/State/MainMenuState.prefab`, `CharSelectState.prefab`, `BossRoomState.prefab`, `PostGameState.prefab`: Place in their scenes to bind `GameStateBehaviour` logic.

- Characters
  - Player: `Assets/Prefabs/Character/PlayerAvatar.prefab` (server+client components, `ServerCharacter`, `ClientCharacter`, movement, animator wiring). Duplicate for new player archetypes; point to a `CharacterClass` and `Avatar` asset.
  - Persistent player: `Assets/Prefabs/Character/PersistentPlayer.prefab` (lives across scenes; stores `NetworkNameState`, `NetworkAvatarGuidState`). Present in bootstrap/init.
  - Enemy base: `Assets/Prefabs/Character/Enemy.prefab` (NPC with `ServerCharacter` and AI), or use concrete examples like `Imp.prefab`, `ImpBoss.prefab`. Duplicate and adjust `CharacterClass.IsNpc` and visuals.
  - Shared rigs: `Assets/Prefabs/Character/Character.prefab` (base rig/components), specialized by `PlayerAvatar`/`Enemy` variants.

- Visuals
  - Reticle/ground click FX: `Assets/VFX/Prefabs/UI/*` and `Assets/Prefabs/UI/*` for UI elements.
  - Character graphics sets: `Assets/Prefabs/CharGFX/*` used by `CharacterSwap` and avatar assets for appearance.

Recommended pattern
- Duplicate the closest prefab (PlayerAvatar or Enemy), keep `NetworkObject`, `ServerCharacter`, `ClientCharacter`, `ServerCharacterMovement`, `PhysicsWrapper`, `ClientPlayerAvatarNetworkAnimator`, and animator setup.
- Create a new `CharacterClass` and, if player‑controlled, create an `Avatar` asset referencing graphics and portrait.
- Bind abilities by setting `CharacterClass.Skill1/2/3` to Action prototypes in `GameDataSource` list.
- Add new prefab to NGO Network Prefabs list (on NetworkManager) and to pooling list if frequently spawned.

## Character Composition & Inheritance Notes

- Composition over inheritance: Characters are composed from `ServerCharacter` + `ServerCharacterMovement` + `ClientCharacter` and supporting components. Behavior differences come from `CharacterClass` data and selected Actions rather than deep C# inheritance.
- Parent/child relationships: Physical movement occurs on a “physics root” (`PhysicsWrapper.Transform`); client graphics are a child with its own Animator. `ClientCharacter` reads server state and sets visuals/animations.
- Avatar mapping: `NetworkAvatarGuidState` links a player’s selection (GUID) to an `Avatar` ScriptableObject, which in turn references graphics prefabs and a `CharacterClass`.
- Player vs NPC: `CharacterClass.IsNpc` toggles AI instantiation in `ServerCharacter`; NPCs get an `AIBrain` instance on spawn.

## AI: How to Adjust or Extend

- Add a behavior
  - Create a new `AIState` implementation (e.g., `WanderAIState`) with `IsEligible`, `Initialize`, and `Update`.
  - Register it in `AIBrain`’s constructor map with a new enum value and selection order.
  - Drive abilities via `ServerActionPlayer` (e.g., request `Chase` or `Melee` when appropriate).

- Tune engagement
  - Use `CharacterClass.DetectRange` or set `AIBrain.DetectRange` override from spawner to change detection radius.
  - `AIBrain.IsAppropriateFoe` filters valid targets (non‑NPC, Alive, not stealthy). Extend if you add factions.

- Reactive behavior
  - `ServerCharacter.ReceiveHP()` forwards damage/heal events to `AIBrain.ReceiveHP()`; use this to adjust threat/hate.

- Pathing & movement
  - Use `ServerCharacterMovement.FollowTransform()` to chase moving targets; use forced modes (Charging/Knockback) for special attacks.

## Using This As a Base Project

- Start here
  - Configure `NetworkingManager.prefab` transport (Relay/IP) and ensure `NetworkObjectPool.prefab` is in bootstrap.
  - Review `GameDataSource` and populate your Actions, then bind skills in each `CharacterClass`.
  - Create/assign `Avatar` assets for player classes and hook them into character select UI.

- Add new content
  - Abilities: New Action ScriptableObject (+ optional logic class), add to `GameDataSource`, wire input if player‑driven, add FX prefabs.
  - Enemies: Duplicate `Enemy.prefab`, create `CharacterClass` (IsNpc=true), choose Actions, and tune DetectRange/speeds.
  - Interactables: Create a NetworkObject‑based prefab implementing `ITargetable` (and `IDamageable` if needed).

- Networking checklist
  - Add new networked prefabs to NetworkManager’s list.
  - Use `NetworkObjectPool` for objects with frequent spawn/despawn (projectiles/FX) to avoid GC spikes.

