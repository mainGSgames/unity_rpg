using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character.AI
{
    /// <summary>
    /// Simple roaming behavior: pick a random nearby point, walk there,
    /// wait a random time, then pick a new point. Yields to ATTACK when foes are detected.
    /// Server-only; uses ServerCharacterMovement to preserve network authority.
    /// </summary>
    public class WanderAIState : AIState
    {
        private readonly AIBrain m_Brain;

        // Center we wander around (spawn position)
        private Vector3 m_Origin;
        private bool m_OriginInitialized;

        // Current wait timer at a stop point
        private float m_WaitTimer;

        // Tunables (could be promoted to CharacterClass later if desired)
        private const float k_WanderRadius = 6f;
        private const float kMinWaitSeconds = 0.8f;
        private const float kMaxWaitSeconds = 2.2f;

        public WanderAIState(AIBrain brain)
        {
            m_Brain = brain;
        }

        public override bool IsEligible()
        {
            // Only wander when there are no foes to hate/attack
            return m_Brain.GetHatedEnemies().Count == 0;
        }

        public override void Initialize()
        {
            if (!m_OriginInitialized)
            {
                m_Origin = m_Brain.GetMyServerCharacter().physicsWrapper.Transform.position;
                m_OriginInitialized = true;
            }

            // If arriving here from ATTACK or IDLE, reset wait to pick a new spot soonish
            m_WaitTimer = 0f;
        }

        public override void Update()
        {
            // Always keep scanning for foes like Idle does
            DetectFoes();
            if (m_Brain.GetHatedEnemies().Count > 0)
            {
                // AIBrain will switch state on next tick
                return;
            }

            var movement = m_Brain.GetMyServerCharacter().Movement;

            if (movement.IsMoving())
            {
                // Still en route; nothing to do
                return;
            }

            if (m_WaitTimer > 0f)
            {
                m_WaitTimer -= Time.deltaTime;
                return;
            }

            // Choose a new random destination near origin and start moving
            var dest = PickRandomPointAroundOrigin();
            movement.SetMovementTarget(dest);

            // Set next dwell time once we reach that destination
            m_WaitTimer = Random.Range(kMinWaitSeconds, kMaxWaitSeconds);
        }

        private Vector3 PickRandomPointAroundOrigin()
        {
            var off2D = Random.insideUnitCircle * k_WanderRadius;
            var target = m_Origin + new Vector3(off2D.x, 0f, off2D.y);
            return target;
        }

        private void DetectFoes()
        {
            float detectionRange = m_Brain.DetectRange;
            float detectionRangeSqr = detectionRange * detectionRange;
            Vector3 position = m_Brain.GetMyServerCharacter().physicsWrapper.Transform.position;

            foreach (var character in PlayerServerCharacter.GetPlayerServerCharacters())
            {
                if (m_Brain.IsAppropriateFoe(character) &&
                    (character.physicsWrapper.Transform.position - position).sqrMagnitude <= detectionRangeSqr)
                {
                    m_Brain.Hate(character);
                }
            }
        }
    }
}

