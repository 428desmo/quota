using System;
using System.Collections.Generic;

namespace Quota
{
    /// <summary>
    /// Python's random.Random for integer seeds, randrange, and shuffle.
    /// The opening deal must match the Python engine for the same seed.
    /// </summary>
    public sealed class PythonRandom
    {
        const int N = 624;
        const int M = 397;
        readonly uint[] mt = new uint[N];
        int index;

        public PythonRandom(int seed)
        {
            Seed((uint)seed);
        }

        public void Seed(uint seed)
        {
            unchecked
            {
                InitByArray(new[] { seed });
            }
        }

        public int RandBelow(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var bits = BitLength((uint)n);
            var r = GetRandBits(bits);
            while (r >= (uint)n) r = GetRandBits(bits);
            return (int)r;
        }

        public T Choice<T>(IReadOnlyList<T> items)
        {
            return items[RandBelow(items.Count)];
        }

        public void Shuffle<T>(IList<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = RandBelow(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        void InitGenRand(uint seed)
        {
            unchecked
            {
                mt[0] = seed;
                for (var i = 1; i < N; i++)
                    mt[i] = 1812433253u * (mt[i - 1] ^ (mt[i - 1] >> 30)) + (uint)i;
                index = N;
            }
        }

        void InitByArray(uint[] key)
        {
            unchecked
            {
                InitGenRand(19650218u);
                var i = 1;
                var j = 0;
                var k = N > key.Length ? N : key.Length;
                for (; k > 0; k--)
                {
                    mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j;
                    i++;
                    j++;
                    if (i >= N)
                    {
                        mt[0] = mt[N - 1];
                        i = 1;
                    }
                    if (j >= key.Length) j = 0;
                }
                for (k = N - 1; k > 0; k--)
                {
                    mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1566083941u)) - (uint)i;
                    i++;
                    if (i >= N)
                    {
                        mt[0] = mt[N - 1];
                        i = 1;
                    }
                }
                mt[0] = 0x80000000u;
                index = N;
            }
        }

        uint GenRand()
        {
            uint[] mag01 = { 0u, 0x9908b0dfu };
            if (index >= N)
            {
                int kk;
                for (kk = 0; kk < N - M; kk++)
                {
                    var y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                    mt[kk] = mt[kk + M] ^ (y >> 1) ^ mag01[y & 1u];
                }
                for (; kk < N - 1; kk++)
                {
                    var y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                    mt[kk] = mt[kk + (M - N)] ^ (y >> 1) ^ mag01[y & 1u];
                }
                {
                    var y = (mt[N - 1] & 0x80000000u) | (mt[0] & 0x7fffffffu);
                    mt[N - 1] = mt[M - 1] ^ (y >> 1) ^ mag01[y & 1u];
                }
                index = 0;
            }
            var value = mt[index++];
            value ^= value >> 11;
            value ^= (value << 7) & 0x9d2c5680u;
            value ^= (value << 15) & 0xefc60000u;
            value ^= value >> 18;
            return value;
        }

        uint GetRandBits(int k)
        {
            return GenRand() >> (32 - k);
        }

        static int BitLength(uint n)
        {
            var bits = 0;
            while (n > 0)
            {
                bits++;
                n >>= 1;
            }
            return bits;
        }
    }
}
