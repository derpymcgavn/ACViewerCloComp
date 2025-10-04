using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ACE.Server.Physics.Common
{
    public sealed class LazyRandom
    {
        private static readonly Lazy<LazyRandom> lazy =
            new Lazy<LazyRandom>(() => new LazyRandom());

        public static LazyRandom Instance { get { return lazy.Value; } }

        private LazyRandom()
        {
            Seed = Guid.NewGuid().GetHashCode();
        }
        private int Seed = 0;
        private volatile ConcurrentDictionary<int, System.Random> RNGs = new ConcurrentDictionary<int, System.Random>();

        public System.Random RNG
        {
            get
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;
                if (RNGs.ContainsKey(threadId))
                    return RNGs[threadId];
                else
                {
                    var seed = Interlocked.Add(ref Seed, 1); // possible to overflow here, but incredibly unlikely for current ACE architecture.
                    return RNGs.AddOrUpdate(threadId, new System.Random(seed), (a, b) => b);
                }
            }
        }
    }

    // probably should be moved outside the physics namespace
    // important class, ensure unit tests pass for this
    public class Random
    {
        /// <summary>
        /// Returns a random number in [min, max) for floats (upper bound exclusive).
        /// </summary>
        public static float RollDice(float min, float max)
        {
            if (max <= min) return min;
            return (float)(LazyRandom.Instance.RNG.NextDouble() * (max - min) + min);
        }

        /// <summary>
        /// Returns a random integer between min and max, inclusive (legacy semantics).
        /// CAUTION: Inclusive upper bound differs from System.Random.Next(min,maxExclusive).
        /// Prefer RollDiceExclusive for standard exclusive semantics.
        /// </summary>
        public static int RollDice(int min, int max)
        {
            if (max < min) return min;
            return LazyRandom.Instance.RNG.Next(min, max + 1);
        }

        /// <summary>
        /// Standard exclusive-upper-bound helper: returns int in [min, maxExclusive).
        /// Use to avoid off-by-one when indexing collections (size = maxExclusive - min).
        /// </summary>
        public static int RollDiceExclusive(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return LazyRandom.Instance.RNG.Next(min, maxExclusive);
        }

        public static uint RollDice(uint min, uint max)
        {
            if (max < min) return min;
            return (uint)LazyRandom.Instance.RNG.Next((int)min, (int)(max + 1));
        }
    }
}
