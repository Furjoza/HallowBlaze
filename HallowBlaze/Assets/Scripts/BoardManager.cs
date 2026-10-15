using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

public class BoardManager : MonoBehaviour
{
    [Serializable]
    public class Count
    {
        public int minimum;
        public int maximum;

        public Count(int min, int max)
        {
            minimum = min;
            maximum = max;
        }
    }

    public int columns = 8;
    public int rows = 8;
    public Count wallCount = new Count(5, 9);
    public Count foodCount = new Count(1, 5);
    public Count aidCount = new Count(0, 1);
    public Count buriedCount = new Count(0, 1);
    public GameObject exit;
    public GameObject[] floorTiles;
    public GameObject[] wallTiles;
    public GameObject[] groundFoodTiles;
    public GameObject bushFoodTiles;
    public GameObject[] buriedFoodTiles;
    public GameObject[] aidTiles;
    public GameObject[] enemyTiles;
    public GameObject[] outerWallTiles;

    private Transform boardHolder;
    private readonly List<Vector3> gridPositions = new List<Vector3>();
    private DeterministicRandomStream gameplayRandom;
    private DeterministicRandomStream cosmeticRandom;
    private System.Random legacyGameplayRandom;
    private BoardRequest activeRequest;
    private BoardRuntime activeRuntime;
    private BoardBlueprint activeBlueprint;

    private void InitialiseList()
    {
        gridPositions.Clear();
        for (int x = 1; x < columns - 1; x++)
        {
            for (int y = 1; y < rows - 1; y++)
                gridPositions.Add(new Vector3(x, y, 0f));
        }
    }

    private void BoardSetup(Transform targetHolder, ICollection<LegacyBoardView> layout)
    {
        RequirePrefabs(floorTiles, nameof(floorTiles));
        RequirePrefabs(outerWallTiles, nameof(outerWallTiles));

        for (int x = -1; x < columns + 1; x++)
        {
            for (int y = -1; y < rows + 1; y++)
            {
                bool isOuterWall = x == -1 || x == columns || y == -1 || y == rows;
                GameObject[] choices = isOuterWall ? outerWallTiles : floorTiles;
                GameObject prefab = choices[cosmeticRandom.NextInt(0, choices.Length)];
                GameObject instance = Instantiate(
                    prefab,
                    new Vector3(x, y, 0f),
                    Quaternion.identity,
                    targetHolder);
                layout.Add(new LegacyBoardView(
                    instance,
                    isOuterWall
                        ? LegacyBoardContentKind.OuterWall
                        : LegacyBoardContentKind.Floor,
                    new GridPosition(x, y)));
            }
        }
    }

    private Vector3 RandomPosition()
    {
        if (gridPositions.Count == 0)
        {
            throw new BoardRuntimeCompositionException(
                BoardRuntimeDiagnosticCode.InvalidInput,
                "generator:no-free-position");
        }

        int randomIndex = NextGameplayInt(0, gridPositions.Count);
        Vector3 randomPosition = gridPositions[randomIndex];
        gridPositions.RemoveAt(randomIndex);
        return randomPosition;
    }

    private void LayoutObjectAtRandom(
        GameObject[] tileArray,
        int minimum,
        int maximum,
        LegacyBoardContentKind contentKind,
        Transform targetHolder,
        ICollection<LegacyBoardView> layout)
    {
        int objectCount = legacyGameplayRandom != null
            ? legacyGameplayRandom.Next(minimum, maximum + 1)
            : gameplayRandom.NextIntInclusive(minimum, maximum);
        if (objectCount > 0)
            RequirePrefabs(tileArray, contentKind.ToString());

        for (int index = 0; index < objectCount; index++)
        {
            Vector3 randomPosition = RandomPosition();
            GameObject tileChoice = tileArray[NextGameplayInt(0, tileArray.Length)];
            int? enemyDamage = contentKind == LegacyBoardContentKind.Enemy
                ? ReadEnemyDamage(tileChoice) : (int?)null;
            GameObject instance = Instantiate(
                tileChoice,
                randomPosition,
                Quaternion.identity,
                targetHolder);
            layout.Add(new LegacyBoardView(
                instance,
                ResolveContentKind(tileChoice, contentKind),
                new GridPosition((int)randomPosition.x, (int)randomPosition.y),
                enemyDamage));

            if (contentKind == LegacyBoardContentKind.Wall &&
                NextGameplayInt(0, 100) > 90)
            {
                if (bushFoodTiles == null)
                {
                    throw new BoardRuntimeCompositionException(
                        BoardRuntimeDiagnosticCode.InvalidInput,
                        "prefab:BushFood");
                }

                GameObject bushFood = Instantiate(
                    bushFoodTiles,
                    randomPosition,
                    Quaternion.identity,
                    targetHolder);
                layout.Add(new LegacyBoardView(
                    bushFood,
                    LegacyBoardContentKind.BushFood,
                    new GridPosition((int)randomPosition.x, (int)randomPosition.y)));
            }
        }
    }

    private int NextGameplayInt(int minimumInclusive, int maximumExclusive)
    {
        return legacyGameplayRandom != null
            ? legacyGameplayRandom.Next(minimumInclusive, maximumExclusive)
            : gameplayRandom.NextInt(minimumInclusive, maximumExclusive);
    }

    private static int ReadEnemyDamage(GameObject prefab)
    {
        Enemy enemy = prefab.GetComponent<Enemy>();
        if (enemy == null || (enemy.playerDamage != 10 && enemy.playerDamage != 20))
            throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput,
                "prefab:Enemy:missing-or-unsupported-damage:" + prefab.name);
        return enemy.playerDamage;
    }

    /// <summary>
    /// Creates and validates the addressed layout before publishing one authoritative runtime.
    /// Ordinary v1 boards generate model-first; checkpoint and legacy v1 boards preserve their original layout path.
    /// A failed setup destroys its staged views and leaves no active request or runtime.
    /// Ordinary enemy rules come from the catalog; legacy variants retain prefab 10/20 HP data.
    /// Initial domain plans exist before activation.
    /// Independent addressed layout/cosmetic streams keep presentation draws out of gameplay; v1/v2 migrations retain seeded legacy layout.
    /// </summary>
    /// <param name="request">The immutable run, node, and generation inputs for this board.</param>
    /// <param name="runState">The active run referenced by <paramref name="request"/>.</param>
    /// <param name="playerView">The scene-owned player view at its initial grid position.</param>
    /// <returns>The complete runtime owned for this board lifetime.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="runState"/> is null.
    /// </exception>
    /// <exception cref="BoardRuntimeCompositionException">Input identity, layout bounds or view mappings are invalid.</exception>
    /// <exception cref="BoardGenerationException">The ordinary candidate does not pass the independent validator.</exception>
    public BoardRuntime SetupScene(
        BoardRequest request,
        RunState runState,
        GameObject playerView)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (runState == null)
            throw new ArgumentNullException(nameof(runState));
        ReleaseCurrentBoard();
        if (request.Address != null && request.Address.GenerationConfigurationId == OrdinaryBoardGenerator.ConfigurationId)
            return SetupOrdinaryScene(request, runState, playerView);
        if (request.Address != null && (request.Address.GenerationConfigurationId != "legacy.board.v1" ||
            request.Address.GenerationVersion != 1))
            throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "generator:unsupported-identity");
        if (columns < 1 || rows < 1)
        {
            throw new BoardRuntimeCompositionException(
                BoardRuntimeDiagnosticCode.InvalidInput,
                "bounds:" + columns + "x" + rows);
        }

        var provider = new RandomStreamProvider(request.RunSeed);
        cosmeticRandom = provider.Create(BoardRandomStreams.CosmeticId(request));
        bool legacy = runState.RandomAlgorithmId == RunState.LegacyRandomAlgorithmId;
        legacyGameplayRandom = legacy ? new System.Random(request.BoardSeed) : null;
        gameplayRandom = legacy ? null : new RandomStreamProvider(request.RunSeed,
            runState.RandomAlgorithmId, runState.RandomAlgorithmVersion).Create(BoardRandomStreams.LayoutId(request));
        Transform stagingHolder = new GameObject("Board").transform;
        stagingHolder.gameObject.SetActive(false);
        var layout = new List<LegacyBoardView>();

        try
        {
            BoardSetup(stagingHolder, layout);
            InitialiseList();
            LayoutObjectAtRandom(
                wallTiles,
                wallCount.minimum,
                wallCount.maximum,
                LegacyBoardContentKind.Wall,
                stagingHolder,
                layout);
            LayoutObjectAtRandom(
                groundFoodTiles,
                foodCount.minimum,
                foodCount.maximum,
                LegacyBoardContentKind.Food,
                stagingHolder,
                layout);
            LayoutObjectAtRandom(
                buriedFoodTiles,
                buriedCount.minimum,
                buriedCount.maximum,
                LegacyBoardContentKind.BuriedFood,
                stagingHolder,
                layout);
            LayoutObjectAtRandom(
                aidTiles,
                aidCount.minimum,
                aidCount.maximum,
                LegacyBoardContentKind.Aid,
                stagingHolder,
                layout);

            int enemyCount = (int)Mathf.Log(request.LegacyDifficultyLevel, 2f);
            LayoutObjectAtRandom(
                enemyTiles,
                enemyCount,
                enemyCount,
                LegacyBoardContentKind.Enemy,
                stagingHolder,
                layout);

            if (exit == null)
            {
                throw new BoardRuntimeCompositionException(
                    BoardRuntimeDiagnosticCode.InvalidInput,
                    "prefab:Exit");
            }

            Vector3 exitPosition = new Vector3(columns - 1, rows - 1, 0f);
            GameObject exitView = Instantiate(
                exit,
                exitPosition,
                Quaternion.identity,
                stagingHolder);
            layout.Add(new LegacyBoardView(
                exitView,
                LegacyBoardContentKind.Exit,
                new GridPosition(columns - 1, rows - 1)));

            if (playerView != null)
            {
                layout.Add(new LegacyBoardView(
                    playerView,
                    LegacyBoardContentKind.Player,
                    ToGridPosition(playerView.transform.position)));
            }

            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(
                request,
                runState,
                new GridBounds(0, 0, columns - 1, rows - 1),
                layout);

            boardHolder = stagingHolder;
            activeRequest = request;
            activeRuntime = runtime;
            stagingHolder.gameObject.SetActive(true);
            return runtime;
        }
        catch
        {
            DestroyBoardHolder(stagingHolder);
            activeRequest = null;
            activeRuntime = null;
            throw;
        }
    }

    /// <summary>Gets the request that identifies the currently generated board.</summary>
    public BoardRequest ActiveRequest => activeRequest;

    /// <summary>Gets the immutable accepted ordinary blueprint, or null for legacy setup and released/failed boards.</summary>
    public BoardBlueprint ActiveBlueprint => activeBlueprint;

    /// <summary>Gets the complete active runtime, or null when startup failed or the board was released.</summary>
    public BoardRuntime ActiveRuntime => activeRuntime;

    internal void ClearActiveRequest()
    {
        ReleaseCurrentBoard();
    }

    private BoardRuntime SetupOrdinaryScene(BoardRequest request, RunState runState, GameObject playerView)
    {
        if (columns != 8 || rows != 8 || playerView == null ||
            !BoardOutcomeIdentityGuard.ValidateRequest(request, runState, out _))
            throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "ordinary:bounds-player-or-identity");
        BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
        BoardGenerationConfig config = OrdinaryBoardContent.CreateConfiguration(request.Address);
        BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(request.Address, config, catalog, request.RunSeed);
        RequirePrefabs(floorTiles, nameof(floorTiles));
        RequirePrefabs(outerWallTiles, nameof(outerWallTiles));
        if (exit == null)
            throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "prefab:Exit");
        cosmeticRandom = new RandomStreamProvider(request.RunSeed).Create(BoardRandomStreams.CosmeticId(request));
        Transform stagingHolder = new GameObject("Board").transform;
        stagingHolder.gameObject.SetActive(false);
        var views = new Dictionary<EntityId, GameObject>();
        Vector3 previousPlayerPosition = playerView.transform.position;
        try
        {
            for (int coordinateX = -1; coordinateX <= 8; coordinateX++)
                for (int coordinateY = -1; coordinateY <= 8; coordinateY++)
                    if (coordinateX == -1 || coordinateX == 8 || coordinateY == -1 || coordinateY == 8)
                        Instantiate(outerWallTiles[cosmeticRandom.NextInt(0, outerWallTiles.Length)],
                            new Vector3(coordinateX, coordinateY, 0), Quaternion.identity, stagingHolder);
            foreach (BoardEntityState entity in blueprint.Entities)
            {
                Vector3 position = new Vector3(entity.Position.X, entity.Position.Y, 0);
                GameObject view;
                if (entity.Definition.Kind.Equals(EntityKind.Player))
                {
                    SetPlayerPosition(playerView, position);
                    view = playerView;
                }
                else
                {
                    GameObject prefab = ResolveOrdinaryPrefab(entity.Definition);
                    view = Instantiate(prefab, position, Quaternion.identity, stagingHolder);
                }
                views.Add(entity.Id, view);
                if (entity.Definition.Traits.IsExit)
                    Instantiate(exit, position, Quaternion.identity, stagingHolder);
            }
            BoardRuntime runtime = BlueprintBoardRuntimeComposer.Compose(request, runState, blueprint, catalog, views);
            boardHolder = stagingHolder;
            activeRequest = request;
            activeBlueprint = blueprint;
            activeRuntime = runtime;
            stagingHolder.gameObject.SetActive(true);
            return runtime;
        }
        catch
        {
            SetPlayerPosition(playerView, previousPlayerPosition);
            DestroyBoardHolder(stagingHolder);
            throw;
        }
    }

    private GameObject ResolveOrdinaryPrefab(BoardEntityDefinition definition)
    {
        GameObject[] choices;
        if (definition.Layer == BoardLayer.Terrain) choices = floorTiles;
        else if (definition.Layer == BoardLayer.Obstacle) choices = wallTiles;
        else if (definition.Kind.Equals(EntityKind.Enemy))
        {
            int damage = definition.ContentId == OrdinaryBoardContent.Enemy10Id ? 10 : 20;
            RequirePrefabs(enemyTiles, nameof(enemyTiles));
            var matching = new List<GameObject>();
            foreach (GameObject candidate in enemyTiles)
                if (ReadEnemyDamage(candidate) == damage) matching.Add(candidate);
            choices = matching.ToArray();
        }
        else
        {
            RequirePrefabs(groundFoodTiles, nameof(groundFoodTiles));
            var matching = new List<GameObject>();
            bool soda = definition.ContentId == "legacy.soda";
            foreach (GameObject candidate in groundFoodTiles)
                if (candidate.CompareTag("Soda") == soda) matching.Add(candidate);
            choices = matching.ToArray();
        }
        RequirePrefabs(choices, definition.ContentId);
        return choices[cosmeticRandom.NextInt(0, choices.Length)];
    }

    private static void SetPlayerPosition(GameObject playerView, Vector3 position)
    {
        Rigidbody2D body = playerView.GetComponent<Rigidbody2D>();
        if (body != null) body.position = new Vector2(position.x, position.y);
        playerView.transform.position = position;
    }

    private static LegacyBoardContentKind ResolveContentKind(
        GameObject prefab,
        LegacyBoardContentKind fallback)
    {
        if (fallback == LegacyBoardContentKind.Food && prefab != null && prefab.CompareTag("Soda"))
            return LegacyBoardContentKind.Soda;
        return fallback;
    }

    private static void RequirePrefabs(GameObject[] prefabs, string category)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            throw new BoardRuntimeCompositionException(
                BoardRuntimeDiagnosticCode.InvalidInput,
                "prefab:" + category);
        }

        for (int index = 0; index < prefabs.Length; index++)
        {
            if (prefabs[index] == null)
            {
                throw new BoardRuntimeCompositionException(
                    BoardRuntimeDiagnosticCode.InvalidInput,
                    "prefab:" + category + "[" + index + "]");
            }
        }
    }

    private static GridPosition ToGridPosition(Vector3 position)
    {
        int x = Mathf.RoundToInt(position.x);
        int y = Mathf.RoundToInt(position.y);
        if (!Mathf.Approximately(position.x, x) || !Mathf.Approximately(position.y, y))
        {
            throw new BoardRuntimeCompositionException(
                BoardRuntimeDiagnosticCode.ViewPositionMismatch,
                "player:view(" + position.x + ", " + position.y + ")");
        }

        return new GridPosition(x, y);
    }

    private void ReleaseCurrentBoard()
    {
        if (activeRuntime != null)
            activeRuntime.Dispose();

        activeRuntime = null;
        activeRequest = null;
        activeBlueprint = null;
        if (boardHolder != null)
        {
            DestroyBoardHolder(boardHolder);
            boardHolder = null;
        }
    }

    private static void DestroyBoardHolder(Transform holder)
    {
        if (holder == null)
            return;

        holder.gameObject.SetActive(false);
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(holder.gameObject);
        else
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
    }

    private void OnDestroy()
    {
        ReleaseCurrentBoard();
    }
}
