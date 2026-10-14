using System;

namespace HallowBlaze.Core.Random
{
    /// <summary>A caller-owned PCG32 XSH-RR cursor; it is deterministic and not cryptographically secure.</summary>
    public sealed class DeterministicRandomStream
    {
        private ulong state;
        private readonly ulong increment;

        internal DeterministicRandomStream(ulong seed, ulong sequence, string label)
        {
            DebugLabel = label;
            increment = unchecked((sequence << 1) | 1UL);
            Step();
            state = unchecked(state + seed);
            Step();
        }

        /// <summary>Gets the stable stream ID used to reproduce this cursor from its provider.</summary>
        public string DebugLabel { get; }

        /// <summary>Gets the current 64-bit state for diagnostics; board saves recreate rather than snapshot it.</summary>
        public ulong State => state;

        /// <summary>Gets the odd PCG sequence increment derived from the stream identity.</summary>
        public ulong Increment => increment;

        /// <summary>Counts raw draws, including range rejections but excluding initialization.</summary>
        public ulong DrawCount { get; private set; }

        /// <summary>Returns an unsigned value in [0, 2^32), consuming one raw draw.</summary>
        public uint NextUInt32()
        {
            DrawCount = unchecked(DrawCount + 1);
            return Step();
        }

        /// <summary>Returns an unbiased integer in [minimumInclusive, maximumExclusive).</summary>
        /// <param name="minimumInclusive">The lower included bound.</param>
        /// <param name="maximumExclusive">The upper excluded bound, strictly greater than the lower bound.</param>
        /// <returns>A uniformly bounded integer using rejection sampling, possibly consuming multiple draws.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The range is empty or reversed; no draw is consumed.</exception>
        public int NextInt(int minimumInclusive, int maximumExclusive)
        {
            if (maximumExclusive <= minimumInclusive)
                throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
            uint width = (uint)((long)maximumExclusive - minimumInclusive);
            return (int)(minimumInclusive + (long)Bounded(width));
        }

        /// <summary>Returns an unbiased integer with both bounds included, including the full Int32 range.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The range is reversed; no draw is consumed.</exception>
        public int NextIntInclusive(int minimumInclusive, int maximumInclusive)
        {
            if (maximumInclusive < minimumInclusive)
                throw new ArgumentOutOfRangeException(nameof(maximumInclusive));
            ulong width = (ulong)((long)maximumInclusive - minimumInclusive) + 1UL;
            uint offset = width == 4294967296UL ? NextUInt32() : Bounded((uint)width);
            return (int)(minimumInclusive + (long)offset);
        }

        /// <summary>Returns a 53-bit double in [0, 1), consuming exactly two raw draws.</summary>
        public double NextUnitDouble()
        {
            ulong upper = NextUInt32() >> 5;
            ulong lower = NextUInt32() >> 6;
            return ((upper << 26) | lower) / 9007199254740992.0;
        }

        private uint Bounded(uint width)
        {
            uint threshold = unchecked(0U - width) % width;
            uint sample;
            do
                sample = NextUInt32();
            while (sample < threshold);
            return sample % width;
        }

        private uint Step()
        {
            ulong previous = state;
            state = unchecked(previous * 6364136223846793005UL + increment);
            uint shifted = (uint)(((previous >> 18) ^ previous) >> 27);
            int rotation = (int)(previous >> 59);
            return (shifted >> rotation) | (shifted << ((-rotation) & 31));
        }
    }
}