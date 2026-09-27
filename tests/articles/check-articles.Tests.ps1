#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# Run: Invoke-Pester tests/articles
BeforeAll {
    $script:checker = (Resolve-Path "$PSScriptRoot/../../scripts/check-articles.ps1").Path
    $script:fixtures = 'tests/articles/fixtures'
    function Invoke-Checker([string[]] $Arguments) {
        $out = & pwsh -NoProfile -File $script:checker @Arguments 2>&1 | ForEach-Object { "$_" }
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = $out
            Problems = @($out | Where-Object { $_ -match '^[^\s:]+:\d+: ' })
        }
    }
}

Describe 'check-articles.ps1' {
    It 'passes the clean fixture' {
        $r = Invoke-Checker @('-Files', "$fixtures/clean.md")
        $r.Problems | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 0
        ($r.Output -join "`n") | Should -Match 'External links, not checked \(1\)'
    }

    It 'lists unlinked numbers only under -Verbose' {
        $r = Invoke-Checker @('-Files', "$fixtures/clean.md", '-Verbose')
        $r.ExitCode | Should -Be 0
        ($r.Output -join "`n") | Should -Match 'clean\.md:9: unlinked numbers: 3, 77'
    }

    It 'reports each deliberate error in the bad fixture once, on the right line' {
        $r = Invoke-Checker @('-Files', "$fixtures/bad.md")
        $r.ExitCode | Should -Be 1
        $r.Problems | Should -Be @(
            "$fixtures/bad.md:3: dead link 'missing-report.json'"
            "$fixtures/bad.md:5: number 81.2% not found in $fixtures/report.json"
            "$fixtures/bad.md:7: banned term 'fleet'"
            "$fixtures/bad.md:9: em dash in prose"
            "$fixtures/bad.md:11: semicolon in prose"
            "$fixtures/bad.md:13: en dash used as a dash"
        )
    }

    It 'replaces the default banned list with -Banned' {
        $r = Invoke-Checker @('-Files', "$fixtures/bad.md", '-Banned', 'Laptops,expected')
        $r.Problems | Should -Contain "$fixtures/bad.md:7: banned term 'laptops'"
        $r.Problems | Should -Contain "$fixtures/bad.md:13: banned term 'expected'"
        $r.Problems | Should -Not -Contain "$fixtures/bad.md:7: banned term 'fleet'"
    }

    It 'checks several files in one run' {
        $r = Invoke-Checker @('-Files', "$fixtures/clean.md,$fixtures/bad.md")
        $r.ExitCode | Should -Be 1
        $r.Problems.Count | Should -Be 6
        ($r.Output -join "`n") | Should -Match 'check-articles: 2 file\(s\), 6 problem\(s\)'
    }
}
