param(
    [string] $ProtocolPath = "$PSScriptRoot\LocalAgentProtocol.ps1",
    [string] $SchemaPath = "$PSScriptRoot\developer-result.schema.json"
)

$ErrorActionPreference = 'Stop'
. $ProtocolPath

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

function Assert-Throws([scriptblock] $action, [string] $expectedText) {
    try {
        & $action
    }
    catch {
        if ($_.Exception.Message -notlike "*$expectedText*") {
            throw "Expected error containing '$expectedText', got: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected error containing '$expectedText', but no error was thrown."
}

function New-ResultJson(
    [string] $status,
    [string] $blocker,
    [string] $patch,
    [object[]] $implemented = @(),
    [object[]] $changedFiles = @(),
    [object[]] $validation = @(),
    [object[]] $remainingRisks = @(),
    [object[]] $blockers = @()) {
    return [pscustomobject]@{
        status = $status
        blocker = $blocker
        patch = $patch
        implemented = $implemented
        changed_files = $changedFiles
        validation = $validation
        remaining_risks = $remainingRisks
        blockers = $blockers
    } | ConvertTo-Json -Depth 10 -Compress
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ($schema.type -eq 'object') 'Schema root must be an object.'
Assert-True ($schema.additionalProperties -eq $false) 'Schema must reject additional properties.'
$expectedProperties = @(
    'status',
    'blocker',
    'patch',
    'implemented',
    'changed_files',
    'validation',
    'remaining_risks',
    'blockers'
)
Assert-True (@($schema.required).Count -eq $expectedProperties.Count) 'Schema required-property count changed.'
foreach ($propertyName in $expectedProperties) {
    Assert-True (@($schema.required) -contains $propertyName) "Schema does not require '$propertyName'."
    Assert-True ($null -ne $schema.properties.$propertyName) "Schema does not define '$propertyName'."
}
Assert-True (@($schema.properties.status.enum) -contains 'patch') 'Schema does not allow patch status.'
Assert-True (@($schema.properties.status.enum) -contains 'no_patch') 'Schema does not allow no_patch status.'
Assert-True (@($schema.properties.status.enum) -contains 'blocked') 'Schema does not allow blocked status.'

$samplePatch = @'
diff --git a/HallowBlaze/Docs/Sample.md b/HallowBlaze/Docs/Sample.md
index 0000000.. bad-local-model-hash
--- a/HallowBlaze/Docs/Sample.md
+++ b/HallowBlaze/Docs/Sample.md
@@ -1 +1 @@
-old
+new — exact `text`
'@
$patchResult = ConvertFrom-DeveloperResultJson (New-ResultJson `
    'patch' `
    'none' `
    $samplePatch `
    @('Updated sample') `
    @('Docs/Sample.md') `
    @('Read-only inspection') `
    @() `
    @())
Assert-True ($patchResult.Status -eq 'patch') 'Patch status was not preserved.'
Assert-True ($patchResult.Patch -notlike '*bad-local-model-hash*') 'Nonsemantic blob metadata was not removed.'
Assert-True ($patchResult.Patch.Contains('+new — exact `text`')) 'Patch hunk text was not preserved.'
Assert-True ($patchResult.ChangedFiles[0] -eq 'Docs/Sample.md') 'Changed-file report was not parsed.'

$noPatchResult = ConvertFrom-DeveloperResultJson (New-ResultJson `
    'no_patch' 'none' '' @('No change required') @() @('Inspected') @() @())
Assert-True ($noPatchResult.Patch -eq '') 'No-patch result must normalize to an empty patch.'

$blockedResult = ConvertFrom-DeveloperResultJson (New-ResultJson `
    'blocked' 'SCOPE_CHANGE_REQUIRED' '' @() @() @('Inspected') @() @('Need another path'))
Assert-True ($blockedResult.Blocker -eq 'SCOPE_CHANGE_REQUIRED') 'Blocker code was not preserved.'

Assert-Throws {
    ConvertFrom-DeveloperResultJson '{not-json}'
} 'not valid JSON'
Assert-Throws {
    ConvertFrom-DeveloperResultJson (New-ResultJson 'patch' 'none' '' @() @() @() @() @())
} 'empty patch'
Assert-Throws {
    ConvertFrom-DeveloperResultJson (New-ResultJson 'no_patch' 'none' 'unexpected' @() @() @() @() @())
} 'unexpectedly contains a patch'
Assert-Throws {
    ConvertFrom-DeveloperResultJson (New-ResultJson 'blocked' 'none' '' @() @() @() @() @())
} 'must declare a blocker code'
Assert-Throws {
    ConvertFrom-DeveloperResultJson '{"status":"no_patch"}'
} 'missing required property'
$unexpectedPropertyResult = New-ResultJson 'no_patch' 'none' '' @() @() @() @() @() | ConvertFrom-Json
$unexpectedPropertyResult | Add-Member -NotePropertyName unexpected_extra -NotePropertyValue 'rejected'
Assert-Throws {
    ConvertFrom-DeveloperResultJson ($unexpectedPropertyResult | ConvertTo-Json -Depth 10 -Compress)
} 'unexpected properties'

$summary = Format-DeveloperResultSummary $patchResult
Assert-True ($summary -like '*status: patch*') 'Summary does not include status.'
Assert-True ($summary -like '*Docs/Sample.md*') 'Summary does not include changed files.'
Assert-True ($summary -notlike '*diff --git*') 'Summary must not echo the patch.'

Write-Host 'PASS: LocalAgent structured Developer protocol' -ForegroundColor Green
exit 0
