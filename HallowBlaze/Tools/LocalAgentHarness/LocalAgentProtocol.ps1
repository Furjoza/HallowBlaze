$ErrorActionPreference = 'Stop'

function Normalize-DeveloperPatch([string] $patch) {
    if ([string]::IsNullOrWhiteSpace($patch)) { return '' }

    $normalizedLines = @((($patch -replace "`r`n", "`n") -replace "`r", "`n") -split "`n" | Where-Object {
        # Blob hashes are advisory metadata and are not required by git apply. Local models
        # sometimes format this line incorrectly even when paths and hunks are otherwise valid.
        -not ([string] $_).StartsWith('index ', [StringComparison]::Ordinal)
    })
    return ($normalizedLines -join "`n").Trim()
}

function ConvertFrom-DeveloperResultJson([string] $json) {
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw 'Developer result is empty.'
    }

    try {
        $result = $json | ConvertFrom-Json
    }
    catch {
        throw "Developer result is not valid JSON: $($_.Exception.Message)"
    }

    if ($null -eq $result -or $result -is [Array]) {
        throw 'Developer result must be one JSON object.'
    }

    $requiredProperties = @(
        'status',
        'blocker',
        'patch',
        'implemented',
        'changed_files',
        'validation',
        'remaining_risks',
        'blockers'
    )
    foreach ($propertyName in $requiredProperties) {
        if ($null -eq $result.PSObject.Properties[$propertyName]) {
            throw "Developer result is missing required property '$propertyName'."
        }
    }

    $unexpectedProperties = @($result.PSObject.Properties.Name | Where-Object {
        $requiredProperties -notcontains [string] $_
    })
    if ($unexpectedProperties.Count -gt 0) {
        throw "Developer result contains unexpected properties: $($unexpectedProperties -join ', ')"
    }

    $status = [string] $result.status
    $blocker = [string] $result.blocker
    $patch = [string] $result.patch
    $validStatuses = @('patch', 'no_patch', 'blocked')
    $validBlockers = @(
        'none',
        'BASELINE_REQUIRED',
        'SCOPE_CHANGE_REQUIRED',
        'ARCHITECTURE_DECISION_REQUIRED',
        'BLOCKED'
    )

    if ($validStatuses -notcontains $status) {
        throw "Developer result has unsupported status '$status'."
    }
    if ($validBlockers -notcontains $blocker) {
        throw "Developer result has unsupported blocker '$blocker'."
    }

    switch ($status) {
        'patch' {
            if ($blocker -ne 'none') {
                throw 'Developer patch result cannot declare a blocker.'
            }
            if ([string]::IsNullOrWhiteSpace($patch)) {
                throw 'Developer patch result contains an empty patch.'
            }
            $patch = Normalize-DeveloperPatch $patch
        }
        'no_patch' {
            if ($blocker -ne 'none') {
                throw 'Developer no-patch result cannot declare a blocker.'
            }
            if (-not [string]::IsNullOrWhiteSpace($patch)) {
                throw 'Developer no-patch result unexpectedly contains a patch.'
            }
            $patch = ''
        }
        'blocked' {
            if ($blocker -eq 'none') {
                throw 'Developer blocked result must declare a blocker code.'
            }
            if (-not [string]::IsNullOrWhiteSpace($patch)) {
                throw 'Developer blocked result unexpectedly contains a patch.'
            }
            $patch = ''
        }
    }

    $arrayProperties = @(
        'implemented',
        'changed_files',
        'validation',
        'remaining_risks',
        'blockers'
    )
    $normalizedArrays = @{}
    foreach ($propertyName in $arrayProperties) {
        $items = @($result.$propertyName)
        foreach ($item in $items) {
            if ($item -isnot [string]) {
                throw "Developer result property '$propertyName' must contain only strings."
            }
        }
        $normalizedArrays[$propertyName] = [string[]] $items
    }

    return [pscustomobject]@{
        Status = $status
        Blocker = $blocker
        Patch = $patch
        Implemented = $normalizedArrays.implemented
        ChangedFiles = $normalizedArrays.changed_files
        Validation = $normalizedArrays.validation
        RemainingRisks = $normalizedArrays.remaining_risks
        Blockers = $normalizedArrays.blockers
    }
}

function Format-DeveloperResultSummary([object] $result) {
    if ($null -eq $result) {
        throw 'Developer result is required.'
    }

    $lines = [Collections.Generic.List[string]]::new()
    [void] $lines.Add("status: $($result.Status)")
    [void] $lines.Add("blocker: $($result.Blocker)")
    foreach ($section in @(
        @{ Name = 'implemented'; Values = @($result.Implemented) },
        @{ Name = 'changed_files'; Values = @($result.ChangedFiles) },
        @{ Name = 'validation'; Values = @($result.Validation) },
        @{ Name = 'remaining_risks'; Values = @($result.RemainingRisks) },
        @{ Name = 'blockers'; Values = @($result.Blockers) }
    )) {
        [void] $lines.Add('')
        [void] $lines.Add("$($section.Name):")
        if ($section.Values.Count -eq 0) {
            [void] $lines.Add('- none')
            continue
        }
        foreach ($value in $section.Values) {
            [void] $lines.Add("- $value")
        }
    }

    return $lines -join "`r`n"
}
