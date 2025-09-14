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

