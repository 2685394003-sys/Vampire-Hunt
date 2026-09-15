using System;
using VampireHunt.Contracts;

namespace VampireHunt.Boss.Abilities
{
    public sealed class BossAbilityController : IDisposable
    {
        private readonly BossAbilityScheduler m_Scheduler = new BossAbilityScheduler();
        private readonly BossAbilityServices m_Services;

        private BossPhaseDefinition m_Phase;
        private BossAbilityDefinition m_CurrentAbility;
        private IBossAbilityLogicRuntime m_CurrentLogic;
        private BossAbilityCastContext m_CurrentContext;
        private BossAbilityCastPhase m_CastPhase;
        private double m_PhaseStartServerTime;
        private ulong m_NextCastSequence;
        private uint m_Revision;

        public bool HasActiveCast => m_CurrentAbility != null;
        public uint Revision => m_Revision;

        public BossAbilityController(BossAbilityServices services = null)
        {
            m_Services = services ?? BossAbilityServices.Empty;
        }

        public void LoadPhase(BossPhaseDefinition phase, double serverTime)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            Cancel(serverTime);
            m_Phase = phase;
            m_Scheduler.Load(phase, serverTime);
            IncrementRevision();
        }

        public bool Tick(
            double serverTime,
            in BossAbilitySelectionContext selection,
            ulong targetEntityId,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed,
            bool allowNewCast)
        {
            Float3 sourcePosition = Float3.Zero;
            return Tick(serverTime, selection, targetEntityId, sourcePosition,
                targetPosition, direction, randomSeed, allowNewCast);
        }

        public bool Tick(
            double serverTime,
            in BossAbilitySelectionContext selection,
            ulong targetEntityId,
            in Float3 sourcePosition,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed,
            bool allowNewCast)
        {
            uint previousRevision = m_Revision;

            if (m_CurrentAbility != null)
            {
                AdvanceActiveCast(serverTime);
            }

            if (allowNewCast && m_CurrentAbility == null && m_Phase != null)
            {
                float random01 = (randomSeed & 0x00FFFFFFu) / 16777216f;
                if (m_Scheduler.TrySelect(serverTime, selection, random01, out BossAbilityScheduleSlot slot))
                {
                    BeginCast(
                        slot.Entry.Ability,
                        slot,
                        serverTime,
                        targetEntityId,
                        sourcePosition,
                        targetPosition,
                        direction,
                        randomSeed);
                }
            }

            m_CurrentLogic?.Tick(serverTime);
            return previousRevision != m_Revision;
        }

        public void Cancel(double serverTime)
        {
            if (m_CurrentLogic != null)
            {
                m_CurrentLogic.Cancel(serverTime);
                m_CurrentLogic.Dispose();
            }

            bool wasCasting = m_CurrentAbility != null;
            m_CurrentLogic = null;
            m_CurrentAbility = null;
            m_CastPhase = BossAbilityCastPhase.None;
            m_PhaseStartServerTime = serverTime;
            if (wasCasting) IncrementRevision();
        }

        public bool TryParry(double serverTime)
        {
            if (m_CurrentAbility == null ||
                m_CastPhase != BossAbilityCastPhase.Telegraph ||
                !m_CurrentAbility.ParryableDuringTelegraph) return false;
            Cancel(serverTime);
            return true;
        }

        public bool TryStartAbility(
            BossAbilityDefinition ability,
            double serverTime,
            ulong targetEntityId,
            in Float3 sourcePosition,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed)
        {
            if (ability == null || m_CurrentAbility != null) return false;
            BeginCast(
                ability,
                null,
                serverTime,
                targetEntityId,
                sourcePosition,
                targetPosition,
                direction,
                randomSeed);
            return true;
        }

        public BossAbilitySnapshot CaptureSnapshot()
        {
            if (m_CurrentAbility == null)
            {
                Float3 zero = Float3.Zero;
                return new BossAbilitySnapshot(
                    m_Phase?.PhaseNumber ?? 0,
                    0,
                    m_NextCastSequence,
                    BossAbilityCastPhase.None,
                    0d,
                    m_PhaseStartServerTime,
                    0d,
                    0,
                    zero,
                    zero,
                    0,
                    m_Revision);
            }

            return new BossAbilitySnapshot(
                m_Phase?.PhaseNumber ?? 0,
                m_CurrentAbility.AbilityId,
                m_CurrentContext.CastSequence,
                m_CastPhase,
                m_CurrentContext.StartServerTime,
                m_PhaseStartServerTime,
                m_CurrentContext.StartServerTime + m_CurrentAbility.TotalDuration,
                m_CurrentContext.TargetEntityId,
                m_CurrentContext.TargetPosition,
                m_CurrentContext.Direction,
                m_CurrentContext.RandomSeed,
                m_Revision);
        }

        public void Dispose()
        {
            Cancel(0d);
            m_Phase = null;
        }

        private void BeginCast(
            BossAbilityDefinition ability,
            BossAbilityScheduleSlot slot,
            double serverTime,
            ulong targetEntityId,
            in Float3 sourcePosition,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed)
        {
            IBossAbilityLogicRuntime runtime = ability.CreateLogic();

            if (runtime is IBossAbilityServiceConsumer consumer)
            {
                consumer.BindServices(m_Services);
            }

            m_CurrentAbility = ability;
            m_CurrentLogic = runtime;
            m_CurrentContext = new BossAbilityCastContext(
                ability.AbilityId,
                ++m_NextCastSequence,
                serverTime,
                targetEntityId,
                sourcePosition,
                targetPosition,
                direction,
                randomSeed);
            m_CastPhase = BossAbilityCastPhase.Telegraph;
            m_PhaseStartServerTime = serverTime;
            if (slot != null) BossAbilityScheduler.CommitCast(slot, serverTime);
            m_CurrentLogic.OnCastStarted(m_CurrentContext);
            m_CurrentLogic.OnPhaseEntered(m_CastPhase, serverTime);
            IncrementRevision();
            AdvanceActiveCast(serverTime);
        }

        private void AdvanceActiveCast(double serverTime)
        {
            if (m_CurrentAbility == null) return;

            double telegraphEnd = m_CurrentContext.StartServerTime + m_CurrentAbility.TelegraphDuration;
            double resolveEnd = telegraphEnd + m_CurrentAbility.ResolveDuration;
            double recoverEnd = resolveEnd + m_CurrentAbility.RecoverDuration;

            bool transitioned;
            do
            {
                transitioned = false;
                switch (m_CastPhase)
                {
                    case BossAbilityCastPhase.Telegraph when serverTime >= telegraphEnd:
                        EnterPhase(BossAbilityCastPhase.Resolve, telegraphEnd);
                        transitioned = true;
                        break;

                    case BossAbilityCastPhase.Resolve when serverTime >= resolveEnd:
                        EnterPhase(BossAbilityCastPhase.Recover, resolveEnd);
                        transitioned = true;
                        break;

                    case BossAbilityCastPhase.Recover when serverTime >= recoverEnd:
                        CompleteCast(recoverEnd);
                        transitioned = true;
                        break;
                }
            } while (transitioned && m_CurrentAbility != null);
        }

        private void EnterPhase(BossAbilityCastPhase phase, double serverTime)
        {
            m_CastPhase = phase;
            m_PhaseStartServerTime = serverTime;
            m_CurrentLogic?.OnPhaseEntered(phase, serverTime);
            IncrementRevision();
        }

        private void CompleteCast(double serverTime)
        {
            m_CurrentLogic?.Dispose();
            m_CurrentLogic = null;
            m_CurrentAbility = null;
            m_CastPhase = BossAbilityCastPhase.None;
            m_PhaseStartServerTime = serverTime;
            IncrementRevision();
        }

        private void IncrementRevision()
        {
            unchecked { m_Revision++; }
        }
    }
}
