using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation.Pipeline
{
    /// <summary>Requires an exact authored definition and position, optionally bound to a stable fixed item source.</summary>
    public sealed class BoardRequiredFeature
    {
        /// <summary>Creates a detached requirement; a source ID is legal only for non-exit item content.</summary>
        /// <exception cref="ArgumentException">The position/source binding is invalid.</exception>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        public BoardRequiredFeature(BoardEntityDefinition definition, GridPosition position, string sourceId = null,
            bool initiallyObservable = true)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (!GridPosition.DefaultBoardBounds.Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (sourceId != null)
            {
                GenerationContract.RequireId(sourceId, nameof(sourceId));
                if (definition.Layer != BoardLayer.Item || definition.Traits.IsExit)
                    throw new ArgumentException("A fixed source must bind a non-exit item.", nameof(sourceId));
            }
            Position = position;
            SourceId = sourceId;
            InitiallyObservable = initiallyObservable;
        }

        /// <summary>Gets the required immutable definition; catalog resolution remains mandatory.</summary>
        public BoardEntityDefinition Definition { get; }
        /// <summary>Gets the exact authored board cell, not a presentation anchor.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets the required stable source ID, or null for a feature without a source binding.</summary>
        public string SourceId { get; }
        /// <summary>Gets required initial item observability; fallback may not reveal hidden source content.</summary>
        public bool InitiallyObservable { get; }
    }

    /// <summary>Owns immutable generation inputs; attempts never replace the saved board address or budgets.</summary>
    public sealed class BoardGenerationRequest
    {
        /// <summary>Creates an addressed request; mismatched configuration identity is rejected before any work.</summary>
        /// <exception cref="ArgumentNullException">A required input is null.</exception>
        /// <exception cref="ArgumentException">The configuration does not match the saved address.</exception>
        /// <param name="address">Saved complete board identity.</param>
        /// <param name="configuration">Unrelaxed targets, including any authored resource bands.</param>
        /// <param name="catalog">Authoritative content and fixed-source definitions.</param>
        /// <param name="runSeed">Persisted run root seed.</param>
        /// <param name="requiredFeatures">Optional exact authored elements, copied and canonically ordered.</param>
        public BoardGenerationRequest(BoardAddress address, BoardGenerationConfig configuration,
            BoardValidationCatalog catalog, int runSeed, IEnumerable<BoardRequiredFeature> requiredFeatures = null)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (address.GenerationConfigurationId != configuration.Id || address.GenerationVersion != configuration.Version)
                throw new ArgumentException(BoardValidationReport.Context(address, configuration) + " | ConfigurationMismatch");
            RunSeed = runSeed;
            var features = new List<BoardRequiredFeature>();
            var occupied = new HashSet<(BoardLayer, GridPosition)>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (BoardRequiredFeature feature in requiredFeatures ?? Array.Empty<BoardRequiredFeature>())
            {
                if (feature == null || !occupied.Add((feature.Definition.Layer, feature.Position)) ||
                    (feature.SourceId != null && !sources.Add(feature.SourceId)))
                    throw new ArgumentException("Required features must have unique layer/cells and source IDs.", nameof(requiredFeatures));
                features.Add(feature);
            }
            features.Sort((left, right) =>
            {
                int layer = left.Definition.Layer.CompareTo(right.Definition.Layer);
                return layer != 0 ? layer : left.Position.CompareTo(right.Position);
            });
            RequiredFeatures = features.AsReadOnly();
        }

        /// <summary>Gets the unchanged saved identity, including the board seed.</summary>
        public BoardAddress Address { get; }
        /// <summary>Gets the original targets; retries and fallbacks cannot relax them.</summary>
        public BoardGenerationConfig Configuration { get; }
        /// <summary>Gets the authoritative detached content registry.</summary>
        public BoardValidationCatalog Catalog { get; }
        /// <summary>Gets the persisted root seed used to derive independent attempt seeds.</summary>
        public int RunSeed { get; }
        /// <summary>Gets exact authored features; neither retry nor fallback may omit or move one.</summary>
        public IReadOnlyList<BoardRequiredFeature> RequiredFeatures { get; }
    }

    /// <summary>Names one bounded authored fallback builder, independent of candidate random generation.</summary>
    public sealed class BoardGenerationFallback
    {
        /// <summary>Registers a stable fallback ID and a synchronous, bounded builder; the pipeline validates its output.</summary>
        /// <exception cref="ArgumentException">The ID is invalid.</exception>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        public BoardGenerationFallback(string id, Func<BoardGenerationRequest, BoardBlueprint> build)
        {
            GenerationContract.RequireId(id, nameof(id));
            Id = id;
            Build = build ?? throw new ArgumentNullException(nameof(build));
        }

        /// <summary>Gets the published fallback identity for diagnostics.</summary>
        public string Id { get; }
        internal Func<BoardGenerationRequest, BoardBlueprint> Build { get; }
    }

    /// <summary>Records one candidate or fallback validation, including reproducible seed and rejection reasons.</summary>
    public sealed class BoardGenerationAttempt
    {
        internal BoardGenerationAttempt(int index, int? subSeed, string fallbackId, BoardValidationReport report)
        {
            Index = index;
            SubSeed = subSeed;
            FallbackId = fallbackId;
            Report = report;
        }

        /// <summary>Gets the zero-based index within candidate attempts or the ordered fallback library.</summary>
        public int Index { get; }
        /// <summary>Gets the attempt's derived seed, or null for authored fallback selection.</summary>
        public int? SubSeed { get; }
        /// <summary>Gets the fallback ID, or null for a generated candidate.</summary>
        public string FallbackId { get; }
        /// <summary>Gets the independent validation result with full requested board identity.</summary>
        public BoardValidationReport Report { get; }

        /// <summary>Formats bounded concise rule codes plus full identity, seed and selected attempt/fallback.</summary>
        public override string ToString()
        {
            var codes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (BoardValidationError error in Report.Errors) codes.Add(error.Code);
            return BoardValidationReport.Context(Report.Address, Report.Configuration) + " | attempt=" +
                Index.ToString(CultureInfo.InvariantCulture) + ", sub-seed=" +
                (SubSeed.HasValue ? SubSeed.Value.ToString(CultureInfo.InvariantCulture) : "<none>") +
                ", fallback=" + (FallbackId ?? "<none>") + ", result=" +
                (Report.IsValid ? "Accepted" : string.Join(",", codes));
        }
    }

    /// <summary>Owns a validated result or an explicit exhausted failure, never an invalid published blueprint.</summary>
    public sealed class BoardGenerationResult
    {
        internal BoardGenerationResult(BoardBlueprint blueprint, List<BoardGenerationAttempt> attempts)
        {
            Blueprint = blueprint;
            Attempts = attempts.AsReadOnly();
        }

        /// <summary>Gets the validated blueprint, or null when both attempts and fallbacks failed.</summary>
        public BoardBlueprint Blueprint { get; }
        /// <summary>Gets every executed attempt in order, including the accepted result when present.</summary>
        public IReadOnlyList<BoardGenerationAttempt> Attempts { get; }
        /// <summary>Gets whether a valid board was produced.</summary>
        public bool IsSuccess => Blueprint != null;
        /// <summary>Gets whether the accepted board came from the authored library, not hidden difficulty relaxation.</summary>
        public bool UsedFallback => IsSuccess && Attempts[Attempts.Count - 1].FallbackId != null;
        /// <summary>Gets the accepted zero-based candidate index, or null on fallback/failure.</summary>
        public int? SelectedAttempt => IsSuccess && !UsedFallback ? Attempts[Attempts.Count - 1].Index : (int?)null;
        /// <summary>Gets the accepted fallback ID, or null on candidate success/failure.</summary>
        public string FallbackId => UsedFallback ? Attempts[Attempts.Count - 1].FallbackId : null;

        /// <summary>Formats the complete bounded attempt history; diagnostics never write files or contact services.</summary>
        public override string ToString()
        {
            var lines = new List<string>();
            foreach (BoardGenerationAttempt attempt in Attempts) lines.Add(attempt.ToString());
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Orchestrates generate, validate, retry and authored fallback with hard synchronous work-count bounds.</summary>
    public static class BoardGenerationPipeline
    {
        /// <summary>Versions ordinary entry-safety validation and bounded retry/fallback selection; versions 1 and 2 retain their saved layouts.</summary>
        public const int OrdinaryConfigurationVersion = 3;

        /// <summary>Caps candidate calls; larger requested limits are rejected before executing a builder.</summary>
        public const int MaximumAttempts = 8;
        /// <summary>Caps the ordered fallback library; builders must themselves perform bounded work.</summary>
        public const int MaximumFallbacks = 8;

        /// <summary>Runs ordinary version 3 with bounded entry-safety validation, or retained version 2 with its unchanged validation and selection.</summary>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ArgumentException">The request does not name the supported ordinary pipeline version.</exception>
        public static BoardGenerationResult GenerateOrdinary(BoardGenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Configuration.Id != OrdinaryBoardGenerator.ConfigurationId ||
                (request.Configuration.Version != 2 && request.Configuration.Version != OrdinaryConfigurationVersion))
                throw new ArgumentException("Ordinary pipeline requires ordinary.board.v1/2 or /3.", nameof(request));
            return Generate(request, (input, subSeed) => OrdinaryBoardGenerator.Generate(input.Address,
                input.Configuration, input.Catalog, subSeed), OrdinaryBoardFallbacks.CreateLibrary());
        }

        /// <summary>Derives a restartable PCG32 sub-seed from full address, root seed and zero-based attempt index.</summary>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside the hard attempt bound.</exception>
        public static int DeriveSubSeed(BoardGenerationRequest request, int attemptIndex)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (attemptIndex < 0 || attemptIndex >= MaximumAttempts) throw new ArgumentOutOfRangeException(nameof(attemptIndex));
            BoardAddress address = request.Address;
            return unchecked((int)new RandomStreamProvider(request.RunSeed).Create(RandomStreamProvider.StreamId(
                "board-generation-attempt-v1", address.RunId, address.LegId, address.EdgeId, address.SegmentId,
                Number(address.DayNumber), address.GenerationConfigurationId, Number(address.GenerationVersion),
                Number(address.BoardSeed), Number(attemptIndex))).NextUInt32());
        }

        /// <summary>
        /// Runs at most the requested candidate count, then each fallback once in supplied order.
        /// All outputs are independently validated against the original request. Expected generation/argument
        /// rejection is recorded; unexpected implementation exceptions propagate rather than hiding a bug.
        /// </summary>
        /// <param name="request">Immutable address, budget, catalog and root seed.</param>
        /// <param name="generate">Bounded synchronous candidate builder; the Int32 argument is a derived root sub-seed.</param>
        /// <param name="fallbacks">At most eight uniquely named bounded authored builders in stable priority order.</param>
        /// <param name="maximumAttempts">Inclusive candidate-call limit in 1..8; never an unbounded retry count.</param>
        /// <returns>Validated success or an explicit failure with all rejection evidence.</returns>
        /// <exception cref="ArgumentException">The limit or fallback registrations are invalid.</exception>
        /// <exception cref="ArgumentNullException">A required input is null.</exception>
        public static BoardGenerationResult Generate(BoardGenerationRequest request,
            Func<BoardGenerationRequest, int, BoardBlueprint> generate,
            IReadOnlyList<BoardGenerationFallback> fallbacks, int maximumAttempts = MaximumAttempts)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (generate == null) throw new ArgumentNullException(nameof(generate));
            if (fallbacks == null) throw new ArgumentNullException(nameof(fallbacks));
            if (maximumAttempts < 1 || maximumAttempts > MaximumAttempts) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            if (fallbacks.Count > MaximumFallbacks) throw new ArgumentException("Fallback library exceeds its hard bound.", nameof(fallbacks));
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var library = new List<BoardGenerationFallback>();
            foreach (BoardGenerationFallback fallback in fallbacks)
            {
                if (fallback == null || !identifiers.Add(fallback.Id)) throw new ArgumentException("Fallbacks require unique non-null registrations.", nameof(fallbacks));
                library.Add(fallback);
            }
            var attempts = new List<BoardGenerationAttempt>();
            for (int index = 0; index < maximumAttempts; index++)
            {
                int seed = DeriveSubSeed(request, index);
                BoardBlueprint blueprint = Inspect(request, () => generate(request, seed), out BoardValidationReport report);
                attempts.Add(new BoardGenerationAttempt(index, seed, null, report));
                if (report.IsValid) return new BoardGenerationResult(blueprint, attempts);
            }
            for (int index = 0; index < library.Count; index++)
            {
                BoardGenerationFallback fallback = library[index];
                BoardBlueprint blueprint = Inspect(request, () => fallback.Build(request), out BoardValidationReport report);
                attempts.Add(new BoardGenerationAttempt(index, null, fallback.Id, report));
                if (report.IsValid) return new BoardGenerationResult(blueprint, attempts);
            }
            return new BoardGenerationResult(null, attempts);
        }

        private static BoardBlueprint Inspect(BoardGenerationRequest request, Func<BoardBlueprint> build,
            out BoardValidationReport report)
        {
            BoardBlueprint blueprint;
            try { blueprint = build(); }
            catch (BoardGenerationException failure)
            {
                report = new BoardValidationReport(request.Address, request.Configuration,
                    new List<BoardValidationError>(failure.Report.Errors), failure.Report.Metrics);
                if (report.IsValid) report = Rejection(request, "CandidateRejected", "Builder rejected without validation errors.");
                return null;
            }
            catch (ArgumentException failure)
            {
                report = Rejection(request, "CandidateInput", failure.Message);
                return null;
            }
            report = BoardValidator.Validate(request.Address, request.Configuration, blueprint?.Entities, blueprint?.Items, request.Catalog);
            if (blueprint != null && (!request.Address.Equals(blueprint.Address) ||
                !blueprint.Equals(new BoardBlueprint(request.Address, request.Configuration, blueprint.Entities, blueprint.Items))))
                report = Rejection(request, "CandidateIdentityMismatch", "Candidate changed the requested address or budget.");
            if (blueprint != null && report.IsValid)
            {
                var errors = new List<BoardValidationError>();
                foreach (BoardRequiredFeature feature in request.RequiredFeatures)
                {
                    BoardEntityState matched = null;
                    foreach (BoardEntityState entity in blueprint.Entities)
                        if (entity.Position.Equals(feature.Position) && entity.Definition.Equals(feature.Definition)) matched = entity;
                    bool satisfied = matched != null;
                    if (satisfied && feature.Definition.Layer == BoardLayer.Item && !feature.Definition.Traits.IsExit)
                    {
                        satisfied = false;
                        foreach (BoardItemSpawn item in blueprint.Items)
                            if (item.EntityId.Equals(matched.Id) && item.SourceId == feature.SourceId &&
                                item.InitiallyObservable == feature.InitiallyObservable) satisfied = true;
                    }
                    if (!satisfied) errors.Add(new BoardValidationError("RequiredFeature", feature.Definition.ContentId +
                        ", source=" + (feature.SourceId ?? "<none>"),
                        BoardValidationReport.Context(request.Address, request.Configuration), feature.Position));
                }
                report = new BoardValidationReport(request.Address, request.Configuration, errors, report.Metrics);
            }
            return report.IsValid ? blueprint : null;
        }

        private static BoardValidationReport Rejection(BoardGenerationRequest request, string code, string message) =>
            new BoardValidationReport(request.Address, request.Configuration,
                new List<BoardValidationError> { new BoardValidationError(code, message,
                    BoardValidationReport.Context(request.Address, request.Configuration)) }, null);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}