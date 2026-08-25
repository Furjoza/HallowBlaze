# HallowBlaze Testing

## HB-000F

The project uses Unity `6000.3.21f1` and Unity Test Framework `1.6.0`.

Test assemblies:

- `Assets/Tests/EditMode/HallowBlaze.Tests.EditMode.asmdef`
- `Assets/Tests/PlayMode/HallowBlaze.Tests.PlayMode.asmdef`

Both assemblies are marked as Unity Test Assemblies. EditMode runs in the
Editor; PlayMode can run in the Editor and in a Player test target.

The Test Runner may list the same test assembly in more than one platform
view. A standard NUnit `[Test]` can be shown in the PlayMode tree as well as
the EditMode tree; the assembly name identifies its owner. Player view is a
separate execution target, not a separate copy of the tests.

## Unity Test Runner

Run each test platform in a separate Unity process. Do not start two Unity instances for this project at the same time.

From the repository root:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe'
$project = 'E:\Repos\HallowBlaze\HallowBlaze'
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform editmode -testResults "$project\Temp\TestResults\HB-000F-EditMode.xml" -logFile "$project\Temp\TestResults\HB-000F-EditMode.log"
```

```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform playmode -testResults "$project\Temp\TestResults\HB-000F-PlayMode.xml" -logFile "$project\Temp\TestResults\HB-000F-PlayMode.log"
```

The commands return Unity's process exit code. Test result XML and logs are generated under `Temp/TestResults/`, which is ignored and must not be committed.
