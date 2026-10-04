using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using Random = UnityEngine.Random;
using UnityEngine;

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
    private System.Random gameplayRandom;
    private BoardRequest activeRequest;
    private BoardRuntime activeRuntime;

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
                GameObject prefab = choices[Random.Range(0, choices.Length)];
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

        int randomIndex = gameplayRandom.Next(0, gridPositions.Count);
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
        int objectCount = gameplayRandom.Next(minimum, maximum + 1);
        if (objectCount > 0)
            RequirePrefabs(tileArray, contentKind.ToString());

        for (int index = 0; index < objectCount; index++)
        {
            Vector3 randomPosition = RandomPosition();
            GameObject tileChoice = tileArray[gameplayRandom.Next(0, tileArray.Length)];
            GameObject instance = Instantiate(
                tileChoice,
                randomPosition,
                Quaternion.identity,
                targetHolder);
            layout.Add(new LegacyBoardView(
                instance,
                ResolveContentKind(tileChoice, contentKind),
                new GridPosition((int)randomPosition.x, (int)randomPosition.y)));

            if (contentKind == LegacyBoardContentKind.Wall &&
                gameplayRandom.Next(0, 100) > 90)
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

    /// <summary>
    /// Creates and validates the complete legacy layout before publishing one authoritative runtime.
    /// A failed setup destroys its staged views and leaves no active request or runtime.
    /// </summary>
    /// <param name="request">The immutable run, node, and generation inputs for this board.</param>
    /// <param name="runState">The active run referenced by <paramref name="request"/>.</param>
    /// <param name="playerView">The scene-owned player view at its initial grid position.</param>
    /// <returns>The complete runtime owned for this board lifetime.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="runState"/> is null.
    /// </exception>
    /// <exception cref="BoardRuntimeCompositionException">The generated layout is incomplete or invalid.</exception>
    public BoardRuntime SetupScene(
        BoardRequest request,
        RunState runState,
        GameObject playerView)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (runState == null)
            throw new ArgumentNullException(nameof(runState));
        if (columns < 1 || rows < 1)
        {
            throw new BoardRuntimeCompositionException(
                BoardRuntimeDiagnosticCode.InvalidInput,
                "bounds:" + columns + "x" + rows);
        }

        ReleaseCurrentBoard();
        gameplayRandom = new System.Random(request.BoardSeed);
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

    /// <summary>Gets the request that identifies the currently generated legacy board.</summary>
    public BoardRequest ActiveRequest => activeRequest;

    /// <summary>Gets the complete active runtime, or null when startup failed or the board was released.</summary>
    public BoardRuntime ActiveRuntime => activeRuntime;

    internal void ClearActiveRequest()
    {
        ReleaseCurrentBoard();
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
