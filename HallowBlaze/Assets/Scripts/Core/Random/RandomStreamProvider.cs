using System;
using System.Globalization;
using System.Text;

namespace HallowBlaze.Core.Random
{
    /// <summary>Creates independent, restartable gameplay streams without Unity or shared random state.</summary>
    public sealed class RandomStreamProvider
    {
        /// <summary>Identifies PCG32 XSH-RR with the frozen HallowBlaze seed derivation.</summary>
        public const string AlgorithmId = "hallowblaze.pcg32";

        /// <summary>Versions both the generator and its length-framed FNV-1a64 seed derivation.</summary>
        public const int AlgorithmVersion = 1;

        /// <summary>Constructs a provider for a persisted root seed and supported algorithm identity.</summary>
        /// <param name="rootSeed">All 32 bits, including the sign bit, participate in derivation.</param>
        /// <param name="algorithmId">The persisted algorithm identifier; unknown identifiers are rejected.</param>
        /// <param name="algorithmVersion">The persisted derivation/generator version.</param>
        /// <exception cref="ArgumentException">The algorithm identity is unsupported.</exception>
        public RandomStreamProvider(int rootSeed, string algorithmId = AlgorithmId,
            int algorithmVersion = AlgorithmVersion)
        {
            if (!IsSupported(algorithmId, algorithmVersion))
                throw new ArgumentException("Unsupported random algorithm identity.", nameof(algorithmId));
            RootSeed = rootSeed;
        }

        /// <summary>Gets the run-owned root seed used by every stream created by this provider.</summary>
        public int RootSeed { get; }

        /// <summary>Checks the complete persisted algorithm identity without substituting a newer version.</summary>
        public static bool IsSupported(string algorithmId, int algorithmVersion)
        {
            return algorithmId == AlgorithmId && algorithmVersion == AlgorithmVersion;
        }

        /// <summary>
        /// Starts a fresh stream at its initial state. Repeated calls do not share a cursor.
        /// Derivation hashes framed UTF-8 domain, algorithm ID, Int32 version/root seed, and framed stream ID.
        /// Int32 values and string byte lengths are little-endian; arithmetic wraps modulo 2^64.
        /// </summary>
        /// <param name="streamId">A stable, nonblank identity, not an object hash or catalogue index.</param>
        /// <returns>A caller-owned mutable stream with a diagnostic label and cursor.</returns>
        /// <exception cref="ArgumentException">The stream identity is blank or has surrounding whitespace.</exception>
        public DeterministicRandomStream Create(string streamId)
        {
            RequireIdentity(streamId, nameof(streamId));
            ulong seed = Derive("hallowblaze.random.seed.v1", streamId);
            ulong sequence = Derive("hallowblaze.random.sequence.v1", streamId);
            return new DeterministicRandomStream(seed, sequence, streamId);
        }

        /// <summary>Frames stable identity parts so separator characters cannot alias another stream.</summary>
        /// <param name="purpose">The named responsibility, such as board-layout or route-supply.</param>
        /// <param name="identity">Ordered stable IDs; published IDs must not be replaced with list positions.</param>
        /// <returns>A canonical stream ID with UTF-8 byte lengths and invariant decimal formatting.</returns>
        /// <exception cref="ArgumentException">A purpose or identity part is blank or padded.</exception>
        /// <exception cref="ArgumentNullException">The identity array is null.</exception>
        public static string StreamId(string purpose, params string[] identity)
        {
            RequireIdentity(purpose, nameof(purpose));
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            StringBuilder label = new StringBuilder();
            AppendLabel(label, purpose);
            foreach (string part in identity)
            {
                RequireIdentity(part, nameof(identity));
                AppendLabel(label, part);
            }
            return label.ToString();
        }

        private ulong Derive(string domain, string streamId)
        {
            ulong hash = 14695981039346656037UL;
            AppendString(ref hash, domain);
            AppendString(ref hash, AlgorithmId);
            AppendInt(ref hash, AlgorithmVersion);
            AppendInt(ref hash, RootSeed);
            AppendString(ref hash, streamId);
            return hash;
        }

        private static void AppendLabel(StringBuilder label, string value)
        {
            label.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture));
            label.Append(':');
            label.Append(value);
        }

        private static void AppendString(ref ulong hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            AppendInt(ref hash, bytes.Length);
            foreach (byte valueByte in bytes)
                hash = unchecked((hash ^ valueByte) * 1099511628211UL);
        }

        private static void AppendInt(ref ulong hash, int value)
        {
            for (int shift = 0; shift < 32; shift += 8)
                hash = unchecked((hash ^ (byte)(value >> shift)) * 1099511628211UL);
        }

        private static void RequireIdentity(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("A stable nonblank identity without padding is required.", parameter);
        }
    }
}