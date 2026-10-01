using ProtoBuf;
using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace precisionknapping
{
    /// <summary>
    /// One charged strike while the server runs it through OnUseOver.
    /// </summary>
    public sealed class ChargedStrike
    {
        /// <summary>0..1, how long the strike was held relative to FullChargeTimeMs.</summary>
        public float Charge;

        /// <summary>Fully charged: knocks off a flake (see KnappingSurfacePatch.HandlePowerStrike).</summary>
        public bool IsPower;

        /// <summary>
        /// Advanced Mode fracture cone size: a light strike makes a narrower fracture,
        /// a power strike a wider one.
        /// </summary>
        public float FractureMultiplier => IsPower ? 1.5f : 0.5f + 0.5f * Charge;
    }

    /// <summary>
    /// Lets KnappingSurfacePatch know that the OnUseOver call it sees is a charged strike.
    /// Thread-static: in singleplayer the client and server threads share static fields.
    /// </summary>
    public static class ChargedStrikeContext
    {
        [ThreadStatic] private static ChargedStrike current;

        public static ChargedStrike Current => current;

        public static void Run(ChargedStrike strike, Action action)
        {
            var previous = current;
            current = strike;
            try { action(); }
            finally { current = previous; }
        }
    }

    /// <summary>
    /// A charge in progress (left mouse held on a knapping surface).
    /// </summary>
    public sealed class ChargeState
    {
        public BlockPos Pos;
        public long StartMs;
        public long LastStepMs;
        public bool Cancelled;

        /// <summary>Client cue progress: 0 = none, 1 = strike ready played, 2 = power ready played.</summary>
        public int CuesPlayed;
    }

    /// <summary>
    /// Charge state per player entity. Keyed by the entity object, so the client and server
    /// copies of a player (same process in singleplayer) never share a state.
    /// </summary>
    public static class ChargeTracker
    {
        /// <summary>A charge whose Step calls stopped this long ago was abandoned by the engine.</summary>
        public const int StaleAfterMs = 600;

        private static readonly ConditionalWeakTable<Entity, ChargeState> states =
            new ConditionalWeakTable<Entity, ChargeState>();

        public static ChargeState Get(Entity entity)
        {
            if (entity == null) return null;
            return states.TryGetValue(entity, out var state) ? state : null;
        }

        public static ChargeState Begin(Entity entity, BlockPos pos)
        {
            long now = entity.World.ElapsedMilliseconds;
            var state = new ChargeState { Pos = pos.Copy(), StartMs = now, LastStepMs = now };
            states.AddOrUpdate(entity, state);
            return state;
        }

        public static void End(Entity entity)
        {
            if (entity != null) states.Remove(entity);
        }

        public static bool IsStale(ChargeState state, long nowMs) => nowMs - state.LastStepMs > StaleAfterMs;

        /// <summary>
        /// Charge level 0..1 for a hold time.
        /// </summary>
        public static float GetChargeLevel(long heldMs, PrecisionKnappingConfig config)
        {
            int full = Math.Max(1, config?.FullChargeTimeMs ?? 1200);
            return Math.Clamp(heldMs / (float)full, 0f, 1f);
        }

        /// <summary>
        /// Charge level below which a release is only a tap.
        /// </summary>
        public static float GetMinChargeLevel(PrecisionKnappingConfig config)
        {
            int full = Math.Max(1, config?.FullChargeTimeMs ?? 1200);
            return Math.Clamp((config?.MinChargeTimeMs ?? 350) / (float)full, 0f, 1f);
        }

        public static bool IsPowerCharge(float charge) => charge >= 0.999f;
    }

    /// <summary>
    /// Client -> server: the player released a charged strike.
    /// </summary>
    [ProtoContract]
    public class ChargedStrikePacket
    {
        [ProtoMember(1)] public int X;
        /// <summary>BlockPos.InternalY (includes the dimension).</summary>
        [ProtoMember(2)] public int InternalY;
        [ProtoMember(3)] public int Z;
        [ProtoMember(4)] public int VoxelX;
        [ProtoMember(5)] public int VoxelZ;
        [ProtoMember(6)] public int FacingIndex;
        [ProtoMember(7)] public float Charge;
    }

    /// <summary>
    /// Server -> client: the server's gameplay settings, so client and server always agree
    /// (Charged Strikes on/off and timing, and Advanced Mode for the Learn Mode overlay).
    /// </summary>
    [ProtoContract]
    public class KnappingSettingsPacket
    {
        [ProtoMember(1)] public bool ChargedStrikes;
        [ProtoMember(2)] public int MinChargeTimeMs;
        [ProtoMember(3)] public int FullChargeTimeMs;
        [ProtoMember(4)] public int PowerStrikeRadius;
        [ProtoMember(5)] public bool AdvancedMode;
    }
}
